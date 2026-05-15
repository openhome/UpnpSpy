using UpnpSpy.Core.Models;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.Eventing;

/// <summary>
/// One scheduler per active subscription. Waits until 30 s before the granted
/// timeout, calls <see cref="ISubscriptionClient.RenewAsync"/>, and on success
/// reschedules off the freshly granted timeout (research §12). HTTP / transport
/// failures transition the subscription to
/// <see cref="SubscriptionStatus.Lapsed"/> and stop the scheduler (FR-038).
/// </summary>
public sealed class SubscriptionRenewalScheduler : IAsyncDisposable
{
    /// <summary>Renew this many seconds before the granted timeout expires.</summary>
    public static readonly TimeSpan RenewalLead = TimeSpan.FromSeconds(30);

    /// <summary>Default timeout asked for on renewal — matches the initial SUBSCRIBE.</summary>
    public static readonly TimeSpan DefaultRequestedTimeout = TimeSpan.FromMinutes(30);

    private readonly ISubscriptionClient _client;
    private readonly IClock _clock;
    private readonly SubscriptionState _state;
    private readonly TimeSpan _requestedTimeout;
    private readonly CancellationTokenSource _cts;
    private readonly Action<RenewResult>? _onLapsed;
    private Task? _loop;
    private bool _disposed;

    public SubscriptionRenewalScheduler(
        ISubscriptionClient client,
        IClock clock,
        SubscriptionState state,
        TimeSpan requestedTimeout,
        CancellationToken linkedToken,
        Action<RenewResult>? onLapsed = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _requestedTimeout = requestedTimeout;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(linkedToken);
        _onLapsed = onLapsed;
    }

    /// <summary>Starts the renewal loop. Safe to call once.</summary>
    public void Start()
    {
        if (_loop is not null) throw new InvalidOperationException("Scheduler already started.");
        _loop = RunAsync(_cts.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_state.Sid is not { Length: > 0 } sid) return;
            if (_state.GrantedTimeout == TimeSpan.MaxValue) return;

            var wait = _state.RenewalDueUtc - _clock.UtcNow;
            if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;

            try
            {
                await _clock.DelayAsync(wait, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (cancellationToken.IsCancellationRequested) return;

            RenewResult result;
            try
            {
                result = await _client.RenewAsync(_state.Service, sid, _requestedTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            switch (result)
            {
                case RenewResult.Success success:
                    _state.GrantedTimeout = success.GrantedTimeout;
                    _state.RenewalDueUtc = success.GrantedTimeout == TimeSpan.MaxValue
                        ? DateTimeOffset.MaxValue
                        : _clock.UtcNow + success.GrantedTimeout - RenewalLead;
                    break;

                case RenewResult.HttpError httpError:
                    _state.Status = SubscriptionStatus.Lapsed;
                    _state.FailureReason = $"Renewal refused: HTTP {httpError.StatusCode} {httpError.ReasonPhrase}";
                    _onLapsed?.Invoke(result);
                    return;

                case RenewResult.TransportError transportError:
                    _state.Status = SubscriptionStatus.Lapsed;
                    _state.FailureReason = $"Renewal transport failure: {transportError.Message}";
                    _onLapsed?.Invoke(result);
                    return;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts.Cancel(); }
        catch (ObjectDisposedException) { }

        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _cts.Dispose();
    }
}
