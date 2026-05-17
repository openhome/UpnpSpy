namespace UpnpSpy.Core.Description;

/// <summary>
/// Fetches and parses a UPnP device description document (UDA 1.0 §2.1, §2.4).
/// Implementations MUST NOT throw for HTTP, transport, or parse failures —
/// every outcome is encoded in <see cref="DeviceDescriptionFetchResult"/>.
/// </summary>
public interface IDeviceDescriptionFetcher
{
    Task<DeviceDescriptionFetchResult> FetchAsync(Uri locationUrl, CancellationToken cancellationToken);
}
