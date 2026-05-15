namespace UpnpSpy.Core.Description;

/// <summary>
/// Outcome of <see cref="IScpdFetcher.FetchAsync"/>. Every variant is normal flow.
/// </summary>
public abstract record ScpdFetchResult
{
    private ScpdFetchResult() { }

    public sealed record Success(ScpdDocument Document) : ScpdFetchResult;
    public sealed record HttpError(int StatusCode, string ReasonPhrase) : ScpdFetchResult;
    public sealed record TransportError(string Message, Exception? Underlying) : ScpdFetchResult;
    public sealed record ParseError(string Message) : ScpdFetchResult;
}
