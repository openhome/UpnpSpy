using FluentAssertions;
using UpnpSpy.Core.Models;
using Xunit;

namespace UpnpSpy.Tests.Models;

public sealed class DeviceTests
{
    [Fact]
    public void DetailLabel_combines_deviceType_tail_and_endpoint()
    {
        var d = new Device
        {
            Uuid = "u1",
            LocationUrl = new Uri("http://192.168.0.1:49152/IGD.xml"),
            DeviceType = "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
        };
        d.DetailLabel.Should().Be("InternetGatewayDevice · 192.168.0.1:49152");
    }

    [Fact]
    public void DetailLabel_omits_default_port()
    {
        var d = new Device
        {
            Uuid = "u1",
            LocationUrl = new Uri("http://example.lan/desc.xml"),
            DeviceType = "urn:schemas-upnp-org:device:WANDevice:1",
        };
        d.DetailLabel.Should().Be("WANDevice · example.lan");
    }

    [Fact]
    public void DetailLabel_falls_back_to_endpoint_only_when_deviceType_is_unknown()
    {
        var d = new Device
        {
            Uuid = "u1",
            LocationUrl = new Uri("http://10.0.0.5:8080/desc.xml"),
            DeviceType = null,
        };
        d.DetailLabel.Should().Be("10.0.0.5:8080");
    }

    [Fact]
    public void DetailLabel_handles_deviceType_without_version_suffix()
    {
        var d = new Device
        {
            Uuid = "u1",
            LocationUrl = new Uri("http://1.2.3.4:1900/desc.xml"),
            DeviceType = "urn:schemas-upnp-org:device:MediaRenderer",
        };
        d.DetailLabel.Should().StartWith("MediaRenderer · ");
    }

    [Fact]
    public void DetailLabel_handles_non_standard_deviceType_by_keeping_full_value()
    {
        var d = new Device
        {
            Uuid = "u1",
            LocationUrl = new Uri("http://1.2.3.4:1900/desc.xml"),
            DeviceType = "vendor-custom-type",
        };
        d.DetailLabel.Should().StartWith("vendor-custom-type · ");
    }
}
