using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using UpnpSpy.Core.Collections;
using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Eventing;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.ViewModels;

/// <summary>
/// Drives the popup window opened when the user picks Subscribe on a service.
/// Owns one <see cref="SubscriptionState"/>, an <see cref="ISubscriptionClient"/>
/// SUBSCRIBE/RENEW/UNSUBSCRIBE roundtrip, and a pump that pulls
/// <see cref="EventNotification"/>s from the
/// <see cref="IEventCallbackHost"/> into <see cref="Events"/>.
///
/// Lifecycle (data-model §10, FR-032/34/35/37/38):
/// - <c>Pending</c> on construction; <c>SUBSCRIBE</c> in flight.
/// - <c>Active</c> on success; renewal scheduler is running, event pump is alive.
/// - <c>Failed</c> if SUBSCRIBE fails — <c>CloseAsync</c> does NOT send UNSUBSCRIBE.
/// - <c>Lapsed</c> if renewal fails permanently — <c>CloseAsync</c> does NOT send UNSUBSCRIBE.
/// - <c>Closed</c> if device byebye observed — <c>CloseAsync</c> does NOT send UNSUBSCRIBE.
/// - <c>Closed</c> if user closes while Active — <c>CloseAsync</c> DOES send UNSUBSCRIBE.
/// </summary>
public partial class SubscriptionPopupViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ISubscriptionClient _subscriptionClient;
    private readonly IEventCallbackHost _callbackHost;
    private readonly DeviceRegistry _registry;
    private readonly IDispatcher _dispatcher;
    private readonly IClock _clock;
    private readonly TimeSpan _requestedTimeout;
    private readonly CancellationTokenSource _cts;

    private EventCallbackRegistration? _registration;
    private SubscriptionRenewalScheduler? _renewalScheduler;
    private Task? _pumpTask;
    private SubscriptionState? _state;
    // FR-033: newest event at index 0, oldest at the tail. EvictTail keeps the
    // newest events on overflow.
    private readonly BoundedObservableCollection<EventNotification> _events =
        new(capacity: 5_000, BoundedEvictionMode.EvictTail);
    private bool _disposed;
    private bool _closeRequested;

    public Service Service { get; }
    public string Title { get; }

    /// <summary>
    /// The live subscription state, or null while the initial SUBSCRIBE is in flight.
    /// </summary>
    public SubscriptionState? State => _state;

    public BoundedObservableCollection<EventNotification> Events => _events;

    public ObservableCollection<EventPropertyRow> LatestProperties { get; } = new();

    [ObservableProperty]
    private SubscriptionStatus _status = SubscriptionStatus.Pending;

    [ObservableProperty]
    private string? _failureReason;

    [ObservableProperty]
    private bool _isDeviceUnreachable;

    /// <summary>Title shown on the popup's status InfoBar.</summary>
    public string StatusBarTitle => Status switch
    {
        SubscriptionStatus.Pending => "Subscription pending",
        SubscriptionStatus.Active => "Subscribed",
        SubscriptionStatus.Lapsed => "Subscription lapsed",
        SubscriptionStatus.Failed => "Subscribe failed",
        SubscriptionStatus.Closed when IsDeviceUnreachable => "Device no longer reachable",
        SubscriptionStatus.Closed => "Subscription closed",
        _ => string.Empty,
    };

    /// <summary>Sub-headline shown on the popup's status InfoBar.</summary>
    public string StatusBarMessage => Status switch
    {
        SubscriptionStatus.Pending => "Waiting for the device to confirm…",
        SubscriptionStatus.Active when _state?.Sid is not null
            => $"SID {_state.Sid} · granted {_state.GrantedTimeout}",
        SubscriptionStatus.Active => "Active",
        SubscriptionStatus.Lapsed => FailureReason ?? "Renewal failed.",
        SubscriptionStatus.Failed => FailureReason ?? "SUBSCRIBE failed.",
        SubscriptionStatus.Closed when IsDeviceUnreachable
            => "The device has gone away. Close this window.",
        SubscriptionStatus.Closed => "Subscription has been closed.",
        _ => string.Empty,
    };

    /// <summary>Display string for the local callback URL once subscription is active.</summary>
    public string CallbackUrlText => _state?.CallbackUrl?.ToString() ?? string.Empty;

    public SubscriptionPopupViewModel(
        Service service,
        ISubscriptionClient subscriptionClient,
        IEventCallbackHost callbackHost,
        DeviceRegistry registry,
        IDispatcher dispatcher,
        IClock clock,
        TimeSpan requestedTimeout,
        CancellationToken shutdownToken)
    {
        Service = service ?? throw new ArgumentNullException(nameof(service));
        _subscriptionClient = subscriptionClient ?? throw new ArgumentNullException(nameof(subscriptionClient));
        _callbackHost = callbackHost ?? throw new ArgumentNullException(nameof(callbackHost));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _requestedTimeout = requestedTimeout;

        Title = $"{service.Label} · Subscribe";

        _cts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
        _registry.DeviceRemoved += OnDeviceRemoved;

        // Status / failure reason / unreachable flag all feed into the
        // bound StatusBarTitle/StatusBarMessage strings — re-emit them.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Status)
                                 or nameof(FailureReason)
                                 or nameof(IsDeviceUnreachable))
            {
                OnPropertyChanged(nameof(StatusBarTitle));
                OnPropertyChanged(nameof(StatusBarMessage));
            }
            if (e.PropertyName == nameof(State))
            {
                OnPropertyChanged(nameof(CallbackUrlText));
                OnPropertyChanged(nameof(StatusBarMessage));
            }
        };
    }

    public Task StartAsync() => RunSubscribeAsync(_cts.Token);

    private async Task RunSubscribeAsync(CancellationToken cancellationToken)
    {
        try
        {
            _registration = _callbackHost.Register();

            var state = new SubscriptionState
            {
                Service = Service,
                CallbackUrl = _registration.CallbackUrl,
                CreatedUtc = _clock.UtcNow,
            };
            _state = state;
            // Mirror the bounded events collection so existing references stay valid.
            OnPropertyChanged(nameof(State));

            var result = await _subscriptionClient.SubscribeAsync(
                Service, _registration.CallbackUrl, _requestedTimeout, cancellationToken).ConfigureAwait(false);

            switch (result)
            {
                case SubscribeResult.Success success:
                    _dispatcher.Post(() => ApplySubscribeSuccess(state, success));
                    StartRenewalLoop(state);
                    _pumpTask = PumpEventsAsync(_registration, cancellationToken);
                    break;

                case SubscribeResult.HttpError http:
                    _dispatcher.Post(() => ApplyFailure(state,
                        $"SUBSCRIBE refused: HTTP {http.StatusCode} {http.ReasonPhrase}"));
                    break;

                case SubscribeResult.TransportError transport:
                    _dispatcher.Post(() => ApplyFailure(state,
                        $"SUBSCRIBE transport failure: {transport.Message}"));
                    break;
            }
        }
        catch (OperationCanceledException) { /* shutdown / close */ }
        catch (Exception ex)
        {
            var s = _state;
            _dispatcher.Post(() => ApplyFailure(s, $"SUBSCRIBE failed: {ex.Message}"));
        }
    }

    private void ApplySubscribeSuccess(SubscriptionState state, SubscribeResult.Success success)
    {
        state.Sid = success.Sid;
        state.GrantedTimeout = success.GrantedTimeout;
        state.RenewalDueUtc = success.GrantedTimeout == TimeSpan.MaxValue
            ? DateTimeOffset.MaxValue
            : _clock.UtcNow + success.GrantedTimeout - SubscriptionRenewalScheduler.RenewalLead;
        state.Status = SubscriptionStatus.Active;
        Status = SubscriptionStatus.Active;
    }

    private void ApplyFailure(SubscriptionState? state, string reason)
    {
        if (state is not null)
        {
            state.Status = SubscriptionStatus.Failed;
            state.FailureReason = reason;
        }
        Status = SubscriptionStatus.Failed;
        FailureReason = reason;
    }

    private void StartRenewalLoop(SubscriptionState state)
    {
        _renewalScheduler = new SubscriptionRenewalScheduler(
            _subscriptionClient, _clock, state, _requestedTimeout, _cts.Token,
            onLapsed: _ => _dispatcher.Post(() =>
            {
                Status = SubscriptionStatus.Lapsed;
                FailureReason = state.FailureReason;
            }));
        _renewalScheduler.Start();
    }

    private async Task PumpEventsAsync(EventCallbackRegistration registration, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var notification in _callbackHost.EventsFor(registration, cancellationToken)
                .ConfigureAwait(false))
            {
                var captured = notification;
                _dispatcher.Post(() =>
                {
                    // FR-033: newest at top.
                    _events.Insert(0, captured);
                    UpdateLatestProperties(captured);
                });
            }
        }
        catch (OperationCanceledException) { }
    }

    private void UpdateLatestProperties(EventNotification ev)
    {
        foreach (var (key, value) in ev.Properties)
        {
            var existing = LatestProperties.FirstOrDefault(p =>
                string.Equals(p.Name, key, StringComparison.Ordinal));
            if (existing is null)
                LatestProperties.Add(new EventPropertyRow(key, value));
            else
                existing.Value = value;
        }
    }

    private void OnDeviceRemoved(DeviceRemovedEvent evt)
    {
        if (!string.Equals(evt.Uuid, Service.OwningDeviceUuid, StringComparison.Ordinal))
            return;

        _dispatcher.Post(() =>
        {
            IsDeviceUnreachable = true;
            // Suppress UNSUBSCRIBE on close per FR-037 — the device has gone away.
            if (_state is not null) _state.Status = SubscriptionStatus.Closed;
            Status = SubscriptionStatus.Closed;
            try { _cts.Cancel(); }
            catch (ObjectDisposedException) { }
        });
    }

    /// <summary>
    /// Called by the view when the popup window is closing. Sends UNSUBSCRIBE
    /// only if the subscription is still Active (FR-034); otherwise the
    /// subscription is already considered terminated (FR-035, FR-037, FR-038).
    /// </summary>
    public async Task CloseAsync()
    {
        if (_closeRequested) return;
        _closeRequested = true;

        var state = _state;
        var shouldUnsubscribe = state is { Status: SubscriptionStatus.Active }
            && !string.IsNullOrEmpty(state.Sid);

        if (shouldUnsubscribe)
        {
            try
            {
                using var unsubCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _subscriptionClient.UnsubscribeAsync(Service, state!.Sid!, unsubCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* best-effort */ }
        }

        if (state is not null && state.Status == SubscriptionStatus.Active)
            state.Status = SubscriptionStatus.Closed;

        var finalStatus = state?.Status ?? Status;
        _dispatcher.Post(() => Status = finalStatus);

        try { _cts.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _registry.DeviceRemoved -= OnDeviceRemoved;

        try { _cts.Cancel(); }
        catch (ObjectDisposedException) { }

        if (_renewalScheduler is not null)
            await _renewalScheduler.DisposeAsync().ConfigureAwait(false);

        if (_pumpTask is not null)
        {
            try { await _pumpTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        if (_registration is not null)
            await _callbackHost.UnregisterAsync(_registration).ConfigureAwait(false);

        _cts.Dispose();
    }
}

public partial class EventPropertyRow : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    private string _value;

    public EventPropertyRow(string name, string value)
    {
        Name = name;
        _value = value;
    }
}

/// <summary>
/// Display extensions to <see cref="EventNotification"/>. The event list shows
/// the SEQ, the local timestamp, and a comma-separated list of which properties
/// changed in that notification, so back-to-back events with different payloads
/// can be told apart at a glance (review item #7).
/// </summary>
public static class EventNotificationDisplay
{
    public static string FormatChangedProperties(this EventNotification ev) =>
        ev.Properties.Count == 0
            ? "(no properties)"
            : string.Join(", ", ev.Properties.Keys);
}
