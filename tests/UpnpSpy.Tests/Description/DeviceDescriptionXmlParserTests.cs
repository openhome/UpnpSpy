using System.Text;
using FluentAssertions;
using UpnpSpy.Core.Description;
using UpnpSpy.Tests.TestHelpers;
using Xunit;

namespace UpnpSpy.Tests.Description;

public sealed class DeviceDescriptionXmlParserTests
{
    private static readonly Uri BaseUri = new("http://192.168.1.10:8080/desc.xml");

    [Fact]
    public void Parses_well_formed_description()
    {
        var (sut, _) = CreateSut();
        var description = sut.Parse(Bytes(@"
<root xmlns='urn:schemas-upnp-org:device-1-0'>
  <device>
    <UDN>uuid:11111111-2222-3333-4444-555555555555</UDN>
    <friendlyName>Living Room Speaker</friendlyName>
    <serviceList>
      <service>
        <serviceId>urn:upnp-org:serviceId:AVTransport</serviceId>
        <serviceType>urn:schemas-upnp-org:service:AVTransport:1</serviceType>
        <SCPDURL>/AVTransport.xml</SCPDURL>
        <controlURL>/AVTransport/ctrl</controlURL>
        <eventSubURL>/AVTransport/event</eventSubURL>
      </service>
    </serviceList>
  </device>
</root>"), BaseUri);

        description.Should().NotBeNull();
        description!.Uuid.Should().Be("11111111-2222-3333-4444-555555555555");
        description.FriendlyName.Should().Be("Living Room Speaker");
        description.Services.Should().HaveCount(1);
        var svc = description.Services[0];
        svc.ContainingDeviceUdn.Should().Be("uuid:11111111-2222-3333-4444-555555555555");
        svc.ContainingDeviceFriendlyName.Should().Be("Living Room Speaker");
        svc.ScpdUrl.Should().Be(new Uri("http://192.168.1.10:8080/AVTransport.xml"));
        svc.ControlUrl.Should().Be(new Uri("http://192.168.1.10:8080/AVTransport/ctrl"));
        svc.EventSubUrl.Should().Be(new Uri("http://192.168.1.10:8080/AVTransport/event"));
    }

    [Fact]
    public void Missing_friendly_name_yields_null_friendly_name()
    {
        var (sut, _) = CreateSut();
        var description = sut.Parse(Bytes(@"
<root>
  <device>
    <UDN>uuid:abc</UDN>
    <serviceList/>
  </device>
</root>"), BaseUri);

        description.Should().NotBeNull();
        description!.FriendlyName.Should().BeNull();
    }

    [Fact]
    public void Whitespace_friendly_name_yields_null_friendly_name()
    {
        var (sut, _) = CreateSut();
        var description = sut.Parse(Bytes(@"
<root>
  <device>
    <UDN>uuid:abc</UDN>
    <friendlyName>   </friendlyName>
    <serviceList/>
  </device>
</root>"), BaseUri);

        description.Should().NotBeNull();
        description!.FriendlyName.Should().BeNull();
    }

    [Fact]
    public void Embedded_child_service_uses_child_udn_and_friendly_name()
    {
        var (sut, _) = CreateSut();
        var description = sut.Parse(Bytes(@"
<root>
  <device>
    <UDN>uuid:root-uuid</UDN>
    <friendlyName>Root</friendlyName>
    <deviceList>
      <device>
        <UDN>uuid:child-a</UDN>
        <friendlyName>Zone A</friendlyName>
        <serviceList>
          <service>
            <serviceId>urn:upnp-org:serviceId:AVT</serviceId>
            <serviceType>urn:schemas-upnp-org:service:AVTransport:1</serviceType>
            <SCPDURL>/a/scpd.xml</SCPDURL>
            <controlURL>/a/ctrl</controlURL>
            <eventSubURL>/a/event</eventSubURL>
          </service>
        </serviceList>
      </device>
    </deviceList>
  </device>
</root>"), BaseUri);

        description.Should().NotBeNull();
        description!.Services.Should().HaveCount(1);
        var svc = description.Services[0];
        svc.ContainingDeviceUdn.Should().Be("uuid:child-a");
        svc.ContainingDeviceFriendlyName.Should().Be("Zone A");
    }

    [Fact]
    public void Two_levels_deep_device_list_walked_recursively()
    {
        var (sut, _) = CreateSut();
        var description = sut.Parse(Bytes(@"
<root>
  <device>
    <UDN>uuid:root</UDN>
    <friendlyName>R</friendlyName>
    <serviceList>
      <service>
        <serviceId>id-root</serviceId><serviceType>urn:s:service:Root:1</serviceType>
        <SCPDURL>/r.xml</SCPDURL><controlURL>/r/c</controlURL><eventSubURL>/r/e</eventSubURL>
      </service>
    </serviceList>
    <deviceList>
      <device>
        <UDN>uuid:level1</UDN><friendlyName>L1</friendlyName>
        <serviceList>
          <service>
            <serviceId>id-1</serviceId><serviceType>urn:s:service:L1:1</serviceType>
            <SCPDURL>/1.xml</SCPDURL><controlURL>/1/c</controlURL><eventSubURL>/1/e</eventSubURL>
          </service>
        </serviceList>
        <deviceList>
          <device>
            <UDN>uuid:level2</UDN><friendlyName>L2</friendlyName>
            <serviceList>
              <service>
                <serviceId>id-2</serviceId><serviceType>urn:s:service:L2:1</serviceType>
                <SCPDURL>/2.xml</SCPDURL><controlURL>/2/c</controlURL><eventSubURL>/2/e</eventSubURL>
              </service>
            </serviceList>
          </device>
        </deviceList>
      </device>
    </deviceList>
  </device>
</root>"), BaseUri);

        description.Should().NotBeNull();
        description!.Services.Select(s => s.ContainingDeviceUdn)
            .Should().BeEquivalentTo(new[] { "uuid:root", "uuid:level1", "uuid:level2" });
    }

    [Fact]
    public void Two_embedded_children_sharing_serviceId_both_appear()
    {
        var (sut, _) = CreateSut();
        var description = sut.Parse(Bytes(@"
<root>
  <device>
    <UDN>uuid:root</UDN><friendlyName>R</friendlyName>
    <deviceList>
      <device>
        <UDN>uuid:child-a</UDN><friendlyName>A</friendlyName>
        <serviceList>
          <service>
            <serviceId>shared-id</serviceId><serviceType>urn:s:service:AVT:1</serviceType>
            <SCPDURL>/a.xml</SCPDURL><controlURL>/a/c</controlURL><eventSubURL>/a/e</eventSubURL>
          </service>
        </serviceList>
      </device>
      <device>
        <UDN>uuid:child-b</UDN><friendlyName>B</friendlyName>
        <serviceList>
          <service>
            <serviceId>shared-id</serviceId><serviceType>urn:s:service:AVT:1</serviceType>
            <SCPDURL>/b.xml</SCPDURL><controlURL>/b/c</controlURL><eventSubURL>/b/e</eventSubURL>
          </service>
        </serviceList>
      </device>
    </deviceList>
  </device>
</root>"), BaseUri);

        description.Should().NotBeNull();
        description!.Services.Should().HaveCount(2);
        description.Services.Select(s => s.ContainingDeviceUdn)
            .Should().BeEquivalentTo(new[] { "uuid:child-a", "uuid:child-b" });
    }

    [Fact]
    public void Duplicate_service_within_same_device_drops_second_and_emits_warning()
    {
        var (sut, diagnostics) = CreateSut();
        var description = sut.Parse(Bytes(@"
<root>
  <device>
    <UDN>uuid:root</UDN><friendlyName>R</friendlyName>
    <serviceList>
      <service>
        <serviceId>dup-id</serviceId><serviceType>urn:s:service:AVT:1</serviceType>
        <SCPDURL>/a.xml</SCPDURL><controlURL>/a/c</controlURL><eventSubURL>/a/e</eventSubURL>
      </service>
      <service>
        <serviceId>dup-id</serviceId><serviceType>urn:s:service:AVT:1</serviceType>
        <SCPDURL>/b.xml</SCPDURL><controlURL>/b/c</controlURL><eventSubURL>/b/e</eventSubURL>
      </service>
    </serviceList>
  </device>
</root>"), BaseUri);

        description.Should().NotBeNull();
        description!.Services.Should().HaveCount(1);
        description.Services[0].ScpdUrl.AbsolutePath.Should().Be("/a.xml");

        diagnostics.Entries.Should().ContainSingle();
        diagnostics.Entries[0].Category.Should().Be("Description.Parse");
        diagnostics.Entries[0].Context.Should().ContainKey("service.id").WhoseValue.Should().Be("dup-id");
    }

    [Fact]
    public void Malformed_xml_returns_null()
    {
        var (sut, _) = CreateSut();
        var description = sut.Parse(Bytes("<root><device><UDN>uuid:abc</UDN"), BaseUri);
        description.Should().BeNull();
    }

    [Fact]
    public void Dtd_reference_is_rejected()
    {
        var (sut, _) = CreateSut();
        var description = sut.Parse(Bytes(@"<?xml version='1.0'?>
<!DOCTYPE root [<!ELEMENT root ANY>]>
<root><device><UDN>uuid:abc</UDN></device></root>"), BaseUri);
        description.Should().BeNull();
    }

    [Fact]
    public void Missing_root_device_returns_null()
    {
        var (sut, _) = CreateSut();
        var description = sut.Parse(Bytes("<root></root>"), BaseUri);
        description.Should().BeNull();
    }

    [Fact]
    public void Parses_full_metadata_fields_from_root_device()
    {
        var xml = """
            <?xml version="1.0"?>
            <root xmlns="urn:schemas-upnp-org:device-1-0">
              <device>
                <UDN>uuid:abc</UDN>
                <friendlyName>Sky Router</friendlyName>
                <deviceType>urn:schemas-upnp-org:device:InternetGatewayDevice:1</deviceType>
                <manufacturer>Sky</manufacturer>
                <manufacturerURL>https://www.sky.com/</manufacturerURL>
                <modelName>F@ST 2504</modelName>
                <modelDescription>ADSL router</modelDescription>
                <modelNumber>2504</modelNumber>
                <modelURL>https://www.sky.com/products/2504</modelURL>
                <serialNumber>SN12345</serialNumber>
                <UPC>123456789012</UPC>
                <presentationURL>/admin</presentationURL>
              </device>
            </root>
            """;
        var (sut, _) = CreateSut();
        var d = sut.Parse(Bytes(xml), BaseUri);

        d.Should().NotBeNull();
        d!.DeviceType.Should().Be("urn:schemas-upnp-org:device:InternetGatewayDevice:1");
        d.Manufacturer.Should().Be("Sky");
        d.ManufacturerUrl.Should().Be(new Uri("https://www.sky.com/"));
        d.ModelName.Should().Be("F@ST 2504");
        d.ModelDescription.Should().Be("ADSL router");
        d.ModelNumber.Should().Be("2504");
        d.ModelUrl.Should().Be(new Uri("https://www.sky.com/products/2504"));
        d.SerialNumber.Should().Be("SN12345");
        d.Upc.Should().Be("123456789012");
        // presentationURL is resolved relative to BaseUri.
        d.PresentationUrl!.AbsoluteUri.Should().Be("http://192.168.1.10:8080/admin");
    }

    [Fact]
    public void Missing_optional_metadata_fields_yield_nulls()
    {
        var xml = """
            <?xml version="1.0"?>
            <root xmlns="urn:schemas-upnp-org:device-1-0">
              <device>
                <UDN>uuid:abc</UDN>
                <deviceType>urn:schemas-upnp-org:device:MediaRenderer:1</deviceType>
              </device>
            </root>
            """;
        var (sut, _) = CreateSut();
        var d = sut.Parse(Bytes(xml), BaseUri);

        d.Should().NotBeNull();
        d!.DeviceType.Should().Be("urn:schemas-upnp-org:device:MediaRenderer:1");
        d.Manufacturer.Should().BeNull();
        d.ManufacturerUrl.Should().BeNull();
        d.ModelName.Should().BeNull();
        d.PresentationUrl.Should().BeNull();
        d.SerialNumber.Should().BeNull();
        d.Upc.Should().BeNull();
    }

    [Fact]
    public void Embedded_devices_are_collected_recursively_into_EmbeddedDevices()
    {
        var xml = """
            <?xml version="1.0"?>
            <root xmlns="urn:schemas-upnp-org:device-1-0">
              <device>
                <UDN>uuid:root</UDN>
                <deviceType>urn:schemas-upnp-org:device:InternetGatewayDevice:1</deviceType>
                <friendlyName>Router</friendlyName>
                <deviceList>
                  <device>
                    <UDN>uuid:wan</UDN>
                    <deviceType>urn:schemas-upnp-org:device:WANDevice:1</deviceType>
                    <friendlyName>WAN</friendlyName>
                    <deviceList>
                      <device>
                        <UDN>uuid:wanconn</UDN>
                        <deviceType>urn:schemas-upnp-org:device:WANConnectionDevice:1</deviceType>
                        <friendlyName>WAN Connection</friendlyName>
                      </device>
                    </deviceList>
                  </device>
                </deviceList>
              </device>
            </root>
            """;
        var (sut, _) = CreateSut();
        var d = sut.Parse(Bytes(xml), BaseUri);

        d.Should().NotBeNull();
        d!.EmbeddedDevices.Should().HaveCount(1);
        d.EmbeddedDevices[0].FriendlyName.Should().Be("WAN");
        d.EmbeddedDevices[0].EmbeddedDevices.Should().HaveCount(1);
        d.EmbeddedDevices[0].EmbeddedDevices[0].FriendlyName.Should().Be("WAN Connection");
    }

    private static (DeviceDescriptionXmlParser Sut, RecordingDiagnosticSink Diagnostics) CreateSut()
    {
        var sink = new RecordingDiagnosticSink();
        var clock = new FakeClock();
        return (new DeviceDescriptionXmlParser(sink, clock), sink);
    }

    private static MemoryStream Bytes(string xml) =>
        new(Encoding.UTF8.GetBytes(xml.Trim()));
}
