using System.Xml;
using System.Xml.Linq;
using UpnpSpy.Core.Models;

namespace UpnpSpy.Core.Eventing;

/// <summary>
/// Parses a GENA NOTIFY body — an <c>&lt;e:propertyset&gt;</c> containing zero
/// or more <c>&lt;e:property&gt;</c> children (UDA 1.0 §4.3) — into an
/// <see cref="EventNotification"/>. Tolerates namespace-prefixed and bare
/// element names. On malformed XML the parser returns a notification whose
/// <see cref="EventNotification.RawXml"/> is set and <see cref="EventNotification.Properties"/>
/// is empty so callers can still surface the wire bytes for diagnostics.
/// </summary>
public static class GenaNotifyParser
{
    public static EventNotification Parse(string body, uint sequenceNumber, DateTimeOffset receivedUtc)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (body.Length == 0)
        {
            return new EventNotification(
                receivedUtc,
                sequenceNumber,
                new Dictionary<string, string>(StringComparer.Ordinal),
                RawXml: null);
        }

        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            };
            using var reader = XmlReader.Create(new StringReader(body), settings);
            doc = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return new EventNotification(
                receivedUtc,
                sequenceNumber,
                new Dictionary<string, string>(StringComparer.Ordinal),
                RawXml: body);
        }

        var root = doc.Root;
        if (root is null || !LocalNameEquals(root, "propertyset"))
        {
            return new EventNotification(
                receivedUtc,
                sequenceNumber,
                new Dictionary<string, string>(StringComparer.Ordinal),
                RawXml: body);
        }

        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in root.Elements())
        {
            if (!LocalNameEquals(prop, "property")) continue;

            var inner = prop.Elements().FirstOrDefault();
            if (inner is null) continue;

            properties[inner.Name.LocalName] = inner.Value;
        }

        return new EventNotification(
            receivedUtc,
            sequenceNumber,
            properties,
            RawXml: null);
    }

    private static bool LocalNameEquals(XElement element, string localName) =>
        string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);
}
