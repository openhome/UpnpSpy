namespace UpnpSpy.Core.Models;

public sealed record SsdpLogEntry(
    DateTimeOffset ReceivedUtc,
    SsdpKind Kind,
    string DeviceUuid,
    string Nt,
    string SourceInterfaceName);
