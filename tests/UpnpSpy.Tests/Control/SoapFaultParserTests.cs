using FluentAssertions;
using UpnpSpy.Core.Control;
using Xunit;

namespace UpnpSpy.Tests.Control;

public sealed class SoapFaultParserTests
{
    [Fact]
    public void Well_formed_fault_with_upnp_error_returns_code_and_description()
    {
        const string body = """
        <?xml version="1.0"?>
        <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"
            s:encodingStyle="http://schemas.xmlsoap.org/soap/encoding/">
          <s:Body>
            <s:Fault>
              <faultcode>s:Client</faultcode>
              <faultstring>UPnPError</faultstring>
              <detail>
                <UPnPError xmlns="urn:schemas-upnp-org:control-1-0">
                  <errorCode>711</errorCode>
                  <errorDescription>Illegal seek target</errorDescription>
                </UPnPError>
              </detail>
            </s:Fault>
          </s:Body>
        </s:Envelope>
        """;

        var fault = SoapFaultParser.Parse(body, "fallback");

        fault.ErrorCode.Should().Be(711);
        fault.ErrorDescription.Should().Be("Illegal seek target");
    }

    [Fact]
    public void Fault_without_upnp_error_falls_back_to_provided_description()
    {
        const string body = """
        <?xml version="1.0"?>
        <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
          <s:Body>
            <s:Fault>
              <faultcode>s:Server</faultcode>
              <faultstring>Server died</faultstring>
            </s:Fault>
          </s:Body>
        </s:Envelope>
        """;

        var fault = SoapFaultParser.Parse(body, "Internal Server Error");

        fault.ErrorCode.Should().Be(0);
        fault.ErrorDescription.Should().Be("Internal Server Error");
    }

    [Fact]
    public void Malformed_xml_returns_fallback_and_does_not_throw()
    {
        const string body = "<not actually xml<<<";

        var fault = SoapFaultParser.Parse(body, "Bad request");

        fault.ErrorCode.Should().Be(0);
        fault.ErrorDescription.Should().Be("Bad request");
    }

    [Fact]
    public void Empty_body_returns_fallback()
    {
        var fault = SoapFaultParser.Parse(string.Empty, "Empty");

        fault.ErrorCode.Should().Be(0);
        fault.ErrorDescription.Should().Be("Empty");
    }

    [Fact]
    public void Non_numeric_error_code_falls_back_to_zero()
    {
        const string body = """
        <UPnPError xmlns="urn:schemas-upnp-org:control-1-0">
          <errorCode>not-a-number</errorCode>
          <errorDescription>Vendor weirdness</errorDescription>
        </UPnPError>
        """;

        var fault = SoapFaultParser.Parse(body, "fallback");

        fault.ErrorCode.Should().Be(0);
        fault.ErrorDescription.Should().Be("Vendor weirdness");
    }

    [Fact]
    public void Upnp_error_without_description_uses_fallback_description()
    {
        const string body = """
        <UPnPError xmlns="urn:schemas-upnp-org:control-1-0">
          <errorCode>402</errorCode>
        </UPnPError>
        """;

        var fault = SoapFaultParser.Parse(body, "Invalid Args");

        fault.ErrorCode.Should().Be(402);
        fault.ErrorDescription.Should().Be("Invalid Args");
    }
}
