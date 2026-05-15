namespace UpnpSpy.Core.Eventing;

/// <summary>
/// Outcome of an initial <c>SUBSCRIBE</c> per UDA 1.0 §4.1.1/§4.1.2. Implementations
/// of <see cref="ISubscriptionClient"/> must map every documented outcome onto one of
/// these cases without throwing (except <see cref="OperationCanceledException"/>).
/// </summary>
public abstract record SubscribeResult
{
    public sealed record Success(string Sid, TimeSpan GrantedTimeout) : SubscribeResult;

    public sealed record HttpError(int StatusCode, string ReasonPhrase) : SubscribeResult;

    public sealed record TransportError(string Message, Exception? Underlying) : SubscribeResult;
}
