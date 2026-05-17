using FluentAssertions;
using UpnpSpy.Core.Description;
using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.ViewModels;
using Xunit;

namespace UpnpSpy.Tests.ViewModels;

public sealed class DevicePropertiesViewModelTests
{
    [Fact]
    public void Surfaces_every_field_from_the_Device()
    {
        var device = MakePopulatedDevice();
        var registry = new DeviceRegistry();
        registry.TryAddOrUpdate(device);

        using var sut = new DevicePropertiesViewModel(device, registry);

        sut.FriendlyName.Should().Be("Sky ADSL Router");
        sut.DeviceType.Should().Be("urn:schemas-upnp-org:device:InternetGatewayDevice:1");
        sut.Uuid.Should().Be("uuid-a");
        sut.Manufacturer.Should().Be("Sky");
        sut.ManufacturerUrl.Should().NotBeNull();
        sut.ModelName.Should().Be("F@ST 2504");
        sut.ModelNumber.Should().Be("2504");
        sut.SerialNumber.Should().Be("SN12345");
        sut.PresentationUrl.Should().NotBeNull();
        sut.LocationUrlText.Should().Contain("192.168.0.1");
        sut.Endpoint.Should().Be("192.168.0.1:49152");
        sut.ServerHeader.Should().Contain("BRCM");
        sut.CacheControlMaxAge.Should().Be("1800 s");
        sut.AliveCount.Should().Be("3");
    }

    [Fact]
    public void Null_fields_render_as_dash_placeholder()
    {
        var device = new Device
        {
            Uuid = "uuid-bare",
            LocationUrl = new Uri("http://192.0.2.5:1900/desc.xml"),
            FriendlyName = "Bare",
        };
        var registry = new DeviceRegistry();
        registry.TryAddOrUpdate(device);

        using var sut = new DevicePropertiesViewModel(device, registry);

        sut.DeviceType.Should().Be(DevicePropertiesViewModel.Placeholder);
        sut.Manufacturer.Should().Be(DevicePropertiesViewModel.Placeholder);
        sut.ManufacturerUrlText.Should().Be(DevicePropertiesViewModel.Placeholder);
        sut.ManufacturerUrl.Should().BeNull();
        sut.PresentationUrlText.Should().Be(DevicePropertiesViewModel.Placeholder);
        sut.PresentationUrl.Should().BeNull();
        sut.SerialNumber.Should().Be(DevicePropertiesViewModel.Placeholder);
        sut.BootId.Should().Be(DevicePropertiesViewModel.Placeholder);
        sut.CacheControlMaxAge.Should().Be(DevicePropertiesViewModel.Placeholder);
    }

    [Fact]
    public void DeviceRemoved_for_matching_uuid_flips_IsDeviceUnreachable()
    {
        var device = MakePopulatedDevice();
        var registry = new DeviceRegistry();
        registry.TryAddOrUpdate(device);

        using var sut = new DevicePropertiesViewModel(device, registry);
        sut.IsDeviceUnreachable.Should().BeFalse();

        registry.Remove(device.Uuid);

        sut.IsDeviceUnreachable.Should().BeTrue();
    }

    [Fact]
    public void DeviceRemoved_for_other_uuid_is_a_noop()
    {
        var device = MakePopulatedDevice();
        var registry = new DeviceRegistry();
        registry.TryAddOrUpdate(device);
        registry.TryAddOrUpdate(new Device
        {
            Uuid = "uuid-other",
            LocationUrl = new Uri("http://192.0.2.99/desc.xml"),
        });

        using var sut = new DevicePropertiesViewModel(device, registry);
        registry.Remove("uuid-other");

        sut.IsDeviceUnreachable.Should().BeFalse();
    }

    [Fact]
    public void Embedded_devices_are_exposed_directly()
    {
        var device = MakePopulatedDevice();
        device.EmbeddedDevices = new[]
        {
            new EmbeddedDeviceSummary(
                Udn: "uuid:child-1",
                DeviceType: "urn:schemas-upnp-org:device:WANDevice:1",
                FriendlyName: "WAN",
                EmbeddedDevices: Array.Empty<EmbeddedDeviceSummary>()),
        };
        var registry = new DeviceRegistry();
        registry.TryAddOrUpdate(device);

        using var sut = new DevicePropertiesViewModel(device, registry);

        sut.EmbeddedDevices.Should().HaveCount(1);
        sut.EmbeddedDevices[0].FriendlyName.Should().Be("WAN");
    }

    [Fact]
    public void Dispose_unsubscribes_from_registry_events()
    {
        var device = MakePopulatedDevice();
        var registry = new DeviceRegistry();
        registry.TryAddOrUpdate(device);

        var sut = new DevicePropertiesViewModel(device, registry);
        sut.Dispose();

        registry.Remove(device.Uuid);

        sut.IsDeviceUnreachable.Should().BeFalse("the VM unsubscribed in Dispose");
    }

    private static Device MakePopulatedDevice()
    {
        var firstSeen = new DateTimeOffset(2026, 5, 14, 12, 0, 0, TimeSpan.Zero);
        return new Device
        {
            Uuid = "uuid-a",
            FriendlyName = "Sky ADSL Router",
            LocationUrl = new Uri("http://192.168.0.1:49152/IGD.xml"),
            DeviceType = "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
            Manufacturer = "Sky",
            ManufacturerUrl = new Uri("https://sky.com/"),
            ModelName = "F@ST 2504",
            ModelNumber = "2504",
            ModelDescription = "ADSL router with UPnP IGD",
            SerialNumber = "SN12345",
            PresentationUrl = new Uri("http://192.168.0.1/"),
            ServerHeader = "Linux/2.6.18 UPnP/1.0 BRCM400/1.0",
            CacheControlMaxAge = 1800,
            BootId = 1,
            ConfigId = 1,
            FirstSeenUtc = firstSeen,
            LastSeenUtc = firstSeen.AddSeconds(10),
            AliveCount = 3,
        };
    }
}
