using UpnpSpy.Core.Models;

namespace UpnpSpy.Core.Eventing;

/// <summary>
/// Wraps the three HTTP verbs UPnP eventing uses to manage a subscription:
/// <c>SUBSCRIBE</c> (initial, UDA 1.0 §4.1.1), <c>SUBSCRIBE</c> with <c>SID</c>
/// (renewal, §4.1.3), and <c>UNSUBSCRIBE</c> (§4.1.4). Implementations MUST
/// translate every documented outcome onto the result unions without throwing
/// (except <see cref="OperationCanceledException"/>).
/// </summary>
public interface ISubscriptionClient
{
    Task<SubscribeResult> SubscribeAsync(
        Service service,
        Uri callbackUrl,
        TimeSpan requestedTimeout,
        CancellationToken cancellationToken);

    Task<RenewResult> RenewAsync(
        Service service,
        string sid,
        TimeSpan requestedTimeout,
        CancellationToken cancellationToken);

    Task<UnsubscribeResult> UnsubscribeAsync(
        Service service,
        string sid,
        CancellationToken cancellationToken);
}
