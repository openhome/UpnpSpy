namespace UpnpSpy.Core.Eventing;

/// <summary>
/// One per active subscription. The opaque <see cref="Token"/> is embedded in
/// the callback URL so distinct subscriptions cannot cross-deliver NOTIFY
/// traffic.
/// </summary>
public sealed record EventCallbackRegistration(Guid Token, Uri CallbackUrl);
