namespace UpnpSpy.Core.Eventing;

/// <summary>
/// Outcome of a renewal <c>SUBSCRIBE</c> per UDA 1.0 §4.1.3.
/// </summary>
public abstract record RenewResult
{
    public sealed record Success(TimeSpan GrantedTimeout) : RenewResult;

    public sealed record HttpError(int StatusCode, string ReasonPhrase) : RenewResult;

    public sealed record TransportError(string Message, Exception? Underlying) : RenewResult;
}
