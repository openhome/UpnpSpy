namespace UpnpSpy.Core.Models;

public abstract record InvocationResult(DateTimeOffset CompletedUtc)
{
    public sealed record Success(
        IReadOnlyDictionary<string, string> Outputs,
        DateTimeOffset CompletedUtc) : InvocationResult(CompletedUtc);

    public sealed record UpnpFault(
        int HttpStatusCode,
        int UpnpErrorCode,
        string UpnpErrorDescription,
        string RawFaultXml,
        DateTimeOffset CompletedUtc) : InvocationResult(CompletedUtc);

    public sealed record TransportError(
        string Message,
        Exception? Underlying,
        DateTimeOffset CompletedUtc) : InvocationResult(CompletedUtc);
}
