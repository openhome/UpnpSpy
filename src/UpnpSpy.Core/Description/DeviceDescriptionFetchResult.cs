namespace UpnpSpy.Core.Description;

/// <summary>
/// Outcome of <see cref="IDeviceDescriptionFetcher.FetchAsync"/>. Every variant
/// is normal flow — callers branch on it; implementations do not throw for
/// HTTP, transport, or parse failures (contracts/IDeviceDescriptionFetcher.md).
/// </summary>
public abstract record DeviceDescriptionFetchResult
{
    private DeviceDescriptionFetchResult() { }

    public sealed record Success(DeviceDescription Description) : DeviceDescriptionFetchResult;
    public sealed record HttpError(int StatusCode, string ReasonPhrase) : DeviceDescriptionFetchResult;
    public sealed record TransportError(string Message, Exception? Underlying) : DeviceDescriptionFetchResult;
    public sealed record ParseError(string Message) : DeviceDescriptionFetchResult;
}
