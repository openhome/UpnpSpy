namespace UpnpSpy.Core.Ssdp;

public sealed record SsdpSearchResponse(
    string Uuid,
    string Usn,
    string St,
    Uri Location,
    string? Server,
    int? CacheControlMaxAge)
    : ParsedSsdpMessage(Uuid, Usn);
