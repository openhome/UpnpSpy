namespace UpnpSpy.Core.Models;

public sealed record EventNotification(
    DateTimeOffset ReceivedUtc,
    uint SequenceNumber,
    IReadOnlyDictionary<string, string> Properties,
    string? RawXml)
{
    /// <summary>
    /// Comma-separated list of property names changed in this event, for the
    /// subscription popup's event row. Lets the user distinguish back-to-back
    /// events with different payloads without expanding any details.
    /// </summary>
    public string ChangedPropertiesText =>
        Properties.Count == 0 ? "(no properties)" : string.Join(", ", Properties.Keys);
}
