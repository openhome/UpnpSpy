namespace UpnpSpy.Core.Description;

/// <summary>
/// Fetches and parses a UPnP service description (SCPD) document (UDA 1.0 §2.2, §2.4).
/// Implementations MUST NOT throw for HTTP, transport, or parse failures.
/// </summary>
public interface IScpdFetcher
{
    Task<ScpdFetchResult> FetchAsync(Uri scpdUrl, CancellationToken cancellationToken);
}
