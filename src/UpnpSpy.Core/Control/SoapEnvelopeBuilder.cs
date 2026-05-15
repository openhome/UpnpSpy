using System.Text;
using UpnpSpy.Core.Models;

namespace UpnpSpy.Core.Control;

/// <summary>
/// Builds the SOAP 1.1 request body for a UPnP action invocation
/// (UDA 1.0 §3.1.1). Inputs are emitted in SCPD declaration order; values are
/// XML-escaped for the five predefined entities only. Zero-input actions emit
/// a self-closing action element (FR-031).
/// </summary>
public static class SoapEnvelopeBuilder
{
    /// <summary>
    /// Returns the UTF-8 encoded SOAP envelope body. Does not include the XML
    /// declaration — UDA 1.0 §3.1.1 requires it, so the caller (or HTTP
    /// content layer) is responsible for prepending <c>&lt;?xml version="1.0"?&gt;</c>.
    /// </summary>
    public static byte[] Build(string serviceType, ActionDefinition action, IReadOnlyDictionary<string, string> inputs)
    {
        if (string.IsNullOrWhiteSpace(serviceType))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(serviceType));
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(inputs);

        var xml = BuildXml(serviceType, action, inputs);
        return Encoding.UTF8.GetBytes(xml);
    }

    /// <summary>String form for tests and diagnostics.</summary>
    public static string BuildXml(string serviceType, ActionDefinition action, IReadOnlyDictionary<string, string> inputs)
    {
        if (string.IsNullOrWhiteSpace(serviceType))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(serviceType));
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(inputs);

        var sb = new StringBuilder(256);
        sb.Append("<?xml version=\"1.0\"?>");
        sb.Append("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"");
        sb.Append(" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">");
        sb.Append("<s:Body>");

        if (action.Inputs.Count == 0)
        {
            sb.Append("<u:").Append(action.Name)
              .Append(" xmlns:u=\"").Append(EscapeAttribute(serviceType)).Append("\"/>");
        }
        else
        {
            sb.Append("<u:").Append(action.Name)
              .Append(" xmlns:u=\"").Append(EscapeAttribute(serviceType)).Append("\">");
            foreach (var arg in action.Inputs)
            {
                var value = inputs.TryGetValue(arg.Name, out var v) ? v : string.Empty;
                sb.Append('<').Append(arg.Name).Append('>')
                  .Append(EscapeText(value))
                  .Append("</").Append(arg.Name).Append('>');
            }
            sb.Append("</u:").Append(action.Name).Append('>');
        }

        sb.Append("</s:Body></s:Envelope>");
        return sb.ToString();
    }

    private static string EscapeText(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&apos;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private static string EscapeAttribute(string value) => EscapeText(value);
}
