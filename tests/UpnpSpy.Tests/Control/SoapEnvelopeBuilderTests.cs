using System.Text;
using FluentAssertions;
using UpnpSpy.Core.Control;
using UpnpSpy.Core.Models;
using Xunit;

namespace UpnpSpy.Tests.Control;

public sealed class SoapEnvelopeBuilderTests
{
    private const string AvtServiceType = "urn:schemas-upnp-org:service:AVTransport:1";

    [Fact]
    public void Zero_input_action_emits_self_closing_action_element()
    {
        var action = new ActionDefinition(
            "Stop",
            Array.Empty<ArgumentDefinition>(),
            Array.Empty<ArgumentDefinition>());

        var xml = SoapEnvelopeBuilder.BuildXml(AvtServiceType, action, new Dictionary<string, string>());

        xml.Should().Contain("<u:Stop xmlns:u=\"urn:schemas-upnp-org:service:AVTransport:1\"/>");
        xml.Should().NotContain("</u:Stop>");
    }

    [Fact]
    public void Arguments_emitted_in_scpd_declaration_order()
    {
        var action = new ActionDefinition(
            "Seek",
            new[]
            {
                new ArgumentDefinition("InstanceID", ArgumentDirection.In, "A_ARG_TYPE_InstanceID", "ui4"),
                new ArgumentDefinition("Unit", ArgumentDirection.In, "A_ARG_TYPE_SeekMode", "string"),
                new ArgumentDefinition("Target", ArgumentDirection.In, "A_ARG_TYPE_SeekTarget", "string"),
            },
            Array.Empty<ArgumentDefinition>());

        // Dictionary order is intentionally reversed to prove the SCPD order is honoured,
        // not the dictionary's iteration order.
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Target"] = "00:01:30",
            ["Unit"] = "REL_TIME",
            ["InstanceID"] = "0",
        };

        var xml = SoapEnvelopeBuilder.BuildXml(AvtServiceType, action, inputs);

        var instanceIdx = xml.IndexOf("<InstanceID>", StringComparison.Ordinal);
        var unitIdx = xml.IndexOf("<Unit>", StringComparison.Ordinal);
        var targetIdx = xml.IndexOf("<Target>", StringComparison.Ordinal);

        instanceIdx.Should().BeGreaterThan(0);
        unitIdx.Should().BeGreaterThan(instanceIdx);
        targetIdx.Should().BeGreaterThan(unitIdx);
    }

    [Fact]
    public void Xml_special_characters_in_values_are_escaped()
    {
        var action = new ActionDefinition(
            "SetAVTransportURI",
            new[]
            {
                new ArgumentDefinition("CurrentURI", ArgumentDirection.In, "AVTransportURI", "string"),
            },
            Array.Empty<ArgumentDefinition>());

        var inputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CurrentURI"] = "<bad>&\"'value",
        };

        var xml = SoapEnvelopeBuilder.BuildXml(AvtServiceType, action, inputs);

        xml.Should().Contain("&lt;bad&gt;&amp;&quot;&apos;value");
        xml.Should().NotContain("<bad>");
    }

    [Fact]
    public void Action_element_namespace_matches_service_type()
    {
        var action = new ActionDefinition(
            "Browse",
            Array.Empty<ArgumentDefinition>(),
            Array.Empty<ArgumentDefinition>());

        var xml = SoapEnvelopeBuilder.BuildXml("urn:schemas-upnp-org:service:ContentDirectory:1", action, new Dictionary<string, string>());

        xml.Should().Contain("xmlns:u=\"urn:schemas-upnp-org:service:ContentDirectory:1\"");
    }

    [Fact]
    public void Output_is_utf8_encoded()
    {
        var action = new ActionDefinition(
            "SetVolume",
            new[]
            {
                new ArgumentDefinition("DesiredVolume", ArgumentDirection.In, "Volume", "ui2"),
            },
            Array.Empty<ArgumentDefinition>());

        var inputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DesiredVolume"] = "café",
        };

        var bytes = SoapEnvelopeBuilder.Build("urn:schemas-upnp-org:service:RenderingControl:1", action, inputs);
        var decoded = Encoding.UTF8.GetString(bytes);

        decoded.Should().Contain("café");
        // 'é' is 0xC3 0xA9 in UTF-8 — make sure the bytes are present.
        bytes.Should().ContainInOrder(new byte[] { 0xC3, 0xA9 });
    }

    [Fact]
    public void Missing_input_value_is_treated_as_empty()
    {
        var action = new ActionDefinition(
            "GetTransportInfo",
            new[]
            {
                new ArgumentDefinition("InstanceID", ArgumentDirection.In, "A_ARG_TYPE_InstanceID", "ui4"),
            },
            Array.Empty<ArgumentDefinition>());

        var xml = SoapEnvelopeBuilder.BuildXml(AvtServiceType, action, new Dictionary<string, string>());

        xml.Should().Contain("<InstanceID></InstanceID>");
    }

    [Fact]
    public void Envelope_contains_soap_body_and_envelope_namespace()
    {
        var action = new ActionDefinition(
            "Stop",
            Array.Empty<ArgumentDefinition>(),
            Array.Empty<ArgumentDefinition>());

        var xml = SoapEnvelopeBuilder.BuildXml(AvtServiceType, action, new Dictionary<string, string>());

        xml.Should().Contain("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"");
        xml.Should().Contain("s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"");
        xml.Should().Contain("<s:Body>");
        xml.Should().Contain("</s:Body></s:Envelope>");
    }
}
