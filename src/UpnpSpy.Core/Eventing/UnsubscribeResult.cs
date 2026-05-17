namespace UpnpSpy.Core.Eventing;

/// <summary>
/// Outcome of an <c>UNSUBSCRIBE</c> per UDA 1.0 §4.1.4. Best-effort: failures are
/// recorded as Information diagnostics rather than Warnings (we are no longer
/// interested in the subscription).
/// </summary>
public abstract record UnsubscribeResult
{
    public sealed record Success : UnsubscribeResult;

    public sealed record HttpError(int StatusCode, string ReasonPhrase) : UnsubscribeResult;

    public sealed record TransportError(string Message, Exception? Underlying) : UnsubscribeResult;
}
