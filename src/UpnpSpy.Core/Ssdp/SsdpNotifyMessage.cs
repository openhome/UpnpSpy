namespace UpnpSpy.Core.Ssdp;

public sealed record SsdpNotifyMessage(
    string Uuid,
    string Usn,
    string Nts,
    string Nt,
    Uri? Location,
    string? Server,
    int? CacheControlMaxAge,
    int? BootId,
    int? ConfigId)
    : ParsedSsdpMessage(Uuid, Usn);
