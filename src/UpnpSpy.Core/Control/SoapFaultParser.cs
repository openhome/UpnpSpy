using System.Xml;
using System.Xml.Linq;

namespace UpnpSpy.Core.Control;

/// <summary>
/// Extracts <c>&lt;UPnPError&gt;</c> details from a SOAP 1.1 fault body
/// (UDA 1.0 §3.1.3). Always returns a tuple — bodies without a recognisable
/// fault yield <c>(0, fallbackDescription)</c>.
/// </summary>
public static class SoapFaultParser
{
    public readonly record struct FaultDetail(int ErrorCode, string ErrorDescription);

    /// <summary>
    /// Parses a SOAP fault body. <paramref name="fallbackDescription"/> is used
    /// when the body is unparseable or has no <c>&lt;UPnPError&gt;</c>.
    /// </summary>
    public static FaultDetail Parse(string body, string fallbackDescription)
    {
        if (string.IsNullOrWhiteSpace(body))
            return new FaultDetail(0, fallbackDescription);

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
            return new FaultDetail(0, fallbackDescription);
        }

        var upnpError = doc.Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "UPnPError", StringComparison.Ordinal));
        if (upnpError is null)
            return new FaultDetail(0, fallbackDescription);

        var codeText = ChildText(upnpError, "errorCode");
        var description = ChildText(upnpError, "errorDescription") ?? fallbackDescription;

        var code = 0;
        if (!string.IsNullOrWhiteSpace(codeText)
            && int.TryParse(codeText.Trim(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            code = parsed;
        }

        return new FaultDetail(code, description);
    }

    private static string? ChildText(XElement parent, string localName) =>
        parent.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, localName, StringComparison.Ordinal))
            ?.Value;
}
