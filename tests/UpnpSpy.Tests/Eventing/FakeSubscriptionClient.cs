using UpnpSpy.Core.Eventing;
using UpnpSpy.Core.Models;

namespace UpnpSpy.Tests.Eventing;

/// <summary>
/// Test double for <see cref="ISubscriptionClient"/>. Each verb is fed a FIFO
/// queue of canned results; every call is recorded for assertion. If a queue is
/// empty the configured <c>DefaultXxx</c> result is returned.
/// </summary>
internal sealed class FakeSubscriptionClient : ISubscriptionClient
{
    private readonly Queue<SubscribeResult> _subscribeQueue = new();
    private readonly Queue<RenewResult> _renewQueue = new();
    private readonly Queue<UnsubscribeResult> _unsubscribeQueue = new();

    public List<RecordedSubscribe> SubscribeCalls { get; } = new();
    public List<RecordedRenew> RenewCalls { get; } = new();
    public List<RecordedUnsubscribe> UnsubscribeCalls { get; } = new();

    public SubscribeResult DefaultSubscribe { get; set; } =
        new SubscribeResult.TransportError("no canned subscribe result", null);

    public RenewResult DefaultRenew { get; set; } =
        new RenewResult.TransportError("no canned renew result", null);

    public UnsubscribeResult DefaultUnsubscribe { get; set; } = new UnsubscribeResult.Success();

    public TaskCompletionSource<bool>? SubscribeGate { get; set; }
    public TaskCompletionSource<bool>? RenewGate { get; set; }
    public TaskCompletionSource<bool>? UnsubscribeGate { get; set; }

    public void EnqueueSubscribe(SubscribeResult result) => _subscribeQueue.Enqueue(result);
    public void EnqueueRenew(RenewResult result) => _renewQueue.Enqueue(result);
    public void EnqueueUnsubscribe(UnsubscribeResult result) => _unsubscribeQueue.Enqueue(result);

    public async Task<SubscribeResult> SubscribeAsync(
        Service service, Uri callbackUrl, TimeSpan requestedTimeout, CancellationToken cancellationToken)
    {
        SubscribeCalls.Add(new RecordedSubscribe(service, callbackUrl, requestedTimeout, DateTimeOffset.UtcNow));
        if (SubscribeGate is { } gate)
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return _subscribeQueue.Count > 0 ? _subscribeQueue.Dequeue() : DefaultSubscribe;
    }

    public async Task<RenewResult> RenewAsync(
        Service service, string sid, TimeSpan requestedTimeout, CancellationToken cancellationToken)
    {
        RenewCalls.Add(new RecordedRenew(service, sid, requestedTimeout, DateTimeOffset.UtcNow));
        if (RenewGate is { } gate)
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return _renewQueue.Count > 0 ? _renewQueue.Dequeue() : DefaultRenew;
    }

    public async Task<UnsubscribeResult> UnsubscribeAsync(
        Service service, string sid, CancellationToken cancellationToken)
    {
        UnsubscribeCalls.Add(new RecordedUnsubscribe(service, sid, DateTimeOffset.UtcNow));
        if (UnsubscribeGate is { } gate)
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return _unsubscribeQueue.Count > 0 ? _unsubscribeQueue.Dequeue() : DefaultUnsubscribe;
    }

    public sealed record RecordedSubscribe(Service Service, Uri CallbackUrl, TimeSpan RequestedTimeout, DateTimeOffset At);
    public sealed record RecordedRenew(Service Service, string Sid, TimeSpan RequestedTimeout, DateTimeOffset At);
    public sealed record RecordedUnsubscribe(Service Service, string Sid, DateTimeOffset At);
}
