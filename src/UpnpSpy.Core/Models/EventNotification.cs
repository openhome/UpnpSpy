namespace UpnpSpy.Core.Models;

public sealed record EventNotification(
    DateTimeOffset ReceivedUtc,
    uint SequenceNumber,
    IReadOnlyDictionary<string, string> Properties,
    string? RawXml);
