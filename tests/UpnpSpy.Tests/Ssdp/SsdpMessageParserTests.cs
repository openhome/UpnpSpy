using System.Text;
using FluentAssertions;
using UpnpSpy.Core.Ssdp;
using Xunit;

namespace UpnpSpy.Tests.Ssdp;

public sealed class SsdpMessageParserTests
{
    private readonly SsdpMessageParser _parser = new();

    [Fact]
    public void Parses_notify_alive_with_root_device_USN()
    {
        const string payload =
            "NOTIFY * HTTP/1.1\r\n" +
            "HOST: 239.255.255.250:1900\r\n" +
            "CACHE-CONTROL: max-age=1800\r\n" +
            "LOCATION: http://192.0.2.10:8080/desc.xml\r\n" +
            "NT: upnp:rootdevice\r\n" +
            "NTS: ssdp:alive\r\n" +
            "SERVER: Linux/3.x UPnP/1.0 Test/1\r\n" +
            "USN: uuid:abc-123::upnp:rootdevice\r\n" +
            "\r\n";

        var result = _parser.Parse(Bytes(payload));

        result.Should().BeOfType<SsdpNotifyMessage>();
        var notify = (SsdpNotifyMessage)result!;
        notify.Uuid.Should().Be("abc-123");
        notify.Nts.Should().Be("ssdp:alive");
        notify.Nt.Should().Be("upnp:rootdevice");
        notify.Location.Should().Be(new Uri("http://192.0.2.10:8080/desc.xml"));
        notify.Server.Should().Be("Linux/3.x UPnP/1.0 Test/1");
        notify.CacheControlMaxAge.Should().Be(1800);
    }

    [Fact]
    public void Parses_notify_byebye()
    {
        const string payload =
            "NOTIFY * HTTP/1.1\r\n" +
            "HOST: 239.255.255.250:1900\r\n" +
            "NT: upnp:rootdevice\r\n" +
            "NTS: ssdp:byebye\r\n" +
            "USN: uuid:abc-123::upnp:rootdevice\r\n" +
            "\r\n";

        var result = _parser.Parse(Bytes(payload));

        result.Should().BeOfType<SsdpNotifyMessage>();
        var notify = (SsdpNotifyMessage)result!;
        notify.Nts.Should().Be("ssdp:byebye");
        notify.Uuid.Should().Be("abc-123");
        notify.Location.Should().BeNull();
    }

    [Fact]
    public void Parses_m_search_200_OK_response()
    {
        const string payload =
            "HTTP/1.1 200 OK\r\n" +
            "CACHE-CONTROL: max-age=1800\r\n" +
            "LOCATION: http://192.0.2.10:8080/desc.xml\r\n" +
            "SERVER: Linux/3.x UPnP/1.0 Test/1\r\n" +
            "ST: upnp:rootdevice\r\n" +
            "USN: uuid:abc-123::upnp:rootdevice\r\n" +
            "EXT:\r\n" +
            "\r\n";

        var result = _parser.Parse(Bytes(payload));

        result.Should().BeOfType<SsdpSearchResponse>();
        var resp = (SsdpSearchResponse)result!;
        resp.Uuid.Should().Be("abc-123");
        resp.St.Should().Be("upnp:rootdevice");
        resp.Location.Should().Be(new Uri("http://192.0.2.10:8080/desc.xml"));
    }

    [Fact]
    public void Returns_null_when_USN_missing()
    {
        const string payload =
            "NOTIFY * HTTP/1.1\r\n" +
            "HOST: 239.255.255.250:1900\r\n" +
            "NT: upnp:rootdevice\r\n" +
            "NTS: ssdp:alive\r\n" +
            "\r\n";

        _parser.Parse(Bytes(payload)).Should().BeNull();
    }

    [Fact]
    public void Returns_null_when_payload_truncated()
    {
        _parser.Parse(Bytes("")).Should().BeNull();
        _parser.Parse(Bytes("NOTIFY")).Should().BeNull();
    }

    [Fact]
    public void Headers_are_matched_case_insensitively()
    {
        const string payload =
            "NOTIFY * HTTP/1.1\r\n" +
            "host: 239.255.255.250:1900\r\n" +
            "Location: http://192.0.2.10/x\r\n" +
            "NT: upnp:rootdevice\r\n" +
            "nts: ssdp:alive\r\n" +
            "USN: UUID:Abc-123::upnp:rootdevice\r\n" +
            "\r\n";

        var result = _parser.Parse(Bytes(payload));
        result.Should().BeOfType<SsdpNotifyMessage>();
        var notify = (SsdpNotifyMessage)result!;
        // Note: ExtractUuid preserves the original casing of the UUID body but strips the "uuid:" prefix.
        notify.Uuid.Should().Be("Abc-123");
        notify.Nts.Should().Be("ssdp:alive");
    }

    [Fact]
    public void Extracts_bare_uuid_when_USN_has_no_double_colon_suffix()
    {
        SsdpMessageParser.ExtractUuid("uuid:device-xyz").Should().Be("device-xyz");
    }

    [Fact]
    public void Extracts_bare_uuid_when_USN_has_service_suffix()
    {
        SsdpMessageParser.ExtractUuid("uuid:device-xyz::urn:schemas-upnp-org:service:AVTransport:1")
            .Should().Be("device-xyz");
    }

    [Fact]
    public void ExtractUuid_returns_null_for_non_uuid_USN()
    {
        SsdpMessageParser.ExtractUuid("urn:foo:bar").Should().BeNull();
        SsdpMessageParser.ExtractUuid("").Should().BeNull();
    }

    [Fact]
    public void Returns_null_when_http_response_lacks_LOCATION()
    {
        const string payload =
            "HTTP/1.1 200 OK\r\n" +
            "ST: upnp:rootdevice\r\n" +
            "USN: uuid:abc::upnp:rootdevice\r\n" +
            "\r\n";

        _parser.Parse(Bytes(payload)).Should().BeNull();
    }

    private static ReadOnlyMemory<byte> Bytes(string s) => Encoding.UTF8.GetBytes(s);
}
