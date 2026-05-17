using FluentAssertions;
using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Lifecycle;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Platform;
using UpnpSpy.Core.ViewModels;
using UpnpSpy.Tests.Description;
using UpnpSpy.Tests.TestHelpers;
using Xunit;

namespace UpnpSpy.Tests.ViewModels;

/// <summary>
/// Verifies the state-machine introduced by FR-043: the VM no longer initiates
/// an HTTP fetch — that work has moved to <see cref="EagerDescriptionDispatcher"/>
/// — and <see cref="DeviceNodeViewModel.ExpandAsync"/> simply renders the
/// terminal state of <see cref="Device.DescriptionFetchState"/>, waiting on a
/// <see cref="DeviceRegistry.DeviceUpdated"/> event if the fetch is still in flight.
/// </summary>
public sealed class DeviceNodeViewModelTests
{
    private static readonly Uri RootLocation = new("http://192.168.1.10:8080/desc.xml");

    [Fact]
    public void Newly_constructed_node_contains_Loading_placeholder_child()
    {
        // FR-044: pre-seeded placeholder so WinUI TreeView renders the chevron
        // before the user clicks, without waiting for ExpandAsync to populate Children.
        var device = MakeDevice("root-uuid", "Speaker");
        device.DescriptionFetchState = FetchState.Fetching;

        var registry = new DeviceRegistry();
        registry.TryAddOrUpdate(device);

        var sut = CreateNode(device, registry);

        sut.Children.Should().ContainSingle()
            .Which.Should().Be(DeviceNodeViewModel.LoadingPlaceholder);
    }

    [Fact]
    public async Task Expansion_with_state_Loaded_hydrates_children_from_Device_Services()
    {
        var device = MakeDevice("root-uuid", "Speaker");
        device.Services = new[] { MakeRootService("root-uuid", "AVT") };
        device.DescriptionFetchState = FetchState.Loaded;

        var registry = new DeviceRegistry();
        registry.TryAddOrUpdate(device);

        var sut = CreateNode(device, registry);

        await sut.ExpandAsync();

        sut.Children.Should().HaveCount(1);
        sut.Children[0].Should().BeOfType<ServiceNodeViewModel>();
    }

    [Fact]
    public async Task Expansion_with_state_Failed_renders_inline_placeholder_with_error_text()
    {
        var device = MakeDevice("root-uuid", null);
        device.DescriptionFetchState = FetchState.Failed;
        device.DescriptionFetchError = "HTTP 404 Not Found";

        var registry = new DeviceRegistry();
        registry.TryAddOrUpdate(device);

        var sut = CreateNode(device, registry);
        await sut.ExpandAsync();

        sut.Children.Should().HaveCount(1);
        sut.Children[0].Should().BeOfType<string>()
            .Which.Should().Contain("Services unavailable").And.Contain("404");
    }

    [Fact]
    public async Task Expansion_with_state_Fetching_shows_Loading_placeholder_then_resolves_to_services()
    {
        var device = MakeDevice("root-uuid", null);
        device.DescriptionFetchState = FetchState.Fetching;

        var registry = new DeviceRegistry();
        registry.TryAddOrUpdate(device);

        var sut = CreateNode(device, registry);

        var expand = sut.ExpandAsync();

        sut.Children.Should().ContainSingle()
            .Which.Should().Be(DeviceNodeViewModel.LoadingPlaceholder);

        // Simulate the dispatcher completing the fetch.
        device.FriendlyName = "Discovered Name";
        device.Services = new[] { MakeRootService("root-uuid", "AVT") };
        device.DescriptionFetchState = FetchState.Loaded;
        registry.NotifyUpdated(device.Uuid);

        await expand;

        sut.Children.Should().HaveCount(1);
        sut.Children[0].Should().BeOfType<ServiceNodeViewModel>();
    }

    [Fact]
    public async Task Expansion_with_state_Fetching_resolves_to_inline_error_on_failure()
    {
        var device = MakeDevice("root-uuid", null);
        device.DescriptionFetchState = FetchState.Fetching;

        var registry = new DeviceRegistry();
        registry.TryAddOrUpdate(device);

        var sut = CreateNode(device, registry);

        var expand = sut.ExpandAsync();

        sut.Children.Should().ContainSingle()
            .Which.Should().Be(DeviceNodeViewModel.LoadingPlaceholder);

        device.DescriptionFetchState = FetchState.Failed;
        device.DescriptionFetchError = "network unreachable";
        registry.NotifyUpdated(device.Uuid);

        await expand;

        sut.Children.Should().ContainSingle()
            .Which.Should().BeOfType<string>()
            .Which.Should().Contain("network unreachable");
    }

    [Fact]
    public async Task Subsequent_expansions_are_idempotent_and_do_not_duplicate_children()
    {
        var device = MakeDevice("root-uuid", "Speaker");
        device.Services = new[] { MakeRootService("root-uuid", "AVT") };
        device.DescriptionFetchState = FetchState.Loaded;

        var registry = new DeviceRegistry();
        registry.TryAddOrUpdate(device);

        var sut = CreateNode(device, registry);

        await sut.ExpandAsync();
        await sut.ExpandAsync();
        await sut.ExpandAsync();

        sut.Children.Should().HaveCount(1);
    }

    [Fact]
    public async Task Embedded_child_services_render_with_friendly_name_prefix_in_label()
    {
        var device = MakeDevice("root-uuid", "Root");
        device.Services = new[]
        {
            MakeEmbeddedService("uuid:child-a", "Zone A", "shared-id"),
            MakeEmbeddedService("uuid:child-b", "Zone B", "shared-id"),
        };
        device.DescriptionFetchState = FetchState.Loaded;

        var registry = new DeviceRegistry();
        registry.TryAddOrUpdate(device);

        var sut = CreateNode(device, registry);
        await sut.ExpandAsync();

        var labels = sut.Children.OfType<ServiceNodeViewModel>().Select(c => c.Label).ToArray();
        labels.Should().HaveCount(2);
        labels.Should().Contain(l => l.StartsWith("Zone A", StringComparison.Ordinal));
        labels.Should().Contain(l => l.StartsWith("Zone B", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Shutdown_cancellation_unblocks_in_flight_expansion()
    {
        var device = MakeDevice("root-uuid", null);
        device.DescriptionFetchState = FetchState.Fetching;

        var registry = new DeviceRegistry();
        registry.TryAddOrUpdate(device);

        var shutdown = new AppShutdownTokenSource();
        var sut = CreateNode(device, registry, shutdown);

        var expand = sut.ExpandAsync();
        shutdown.Cancel();

        await FluentActions.Awaiting(() => expand).Should().ThrowAsync<OperationCanceledException>();
        shutdown.Dispose();
    }

    [Fact]
    public async Task FetchXmlCommand_opens_device_location_in_browser()
    {
        var device = MakeDevice("root-uuid", "Speaker");
        var registry = new DeviceRegistry();
        registry.TryAddOrUpdate(device);

        var browser = new FakeBrowserLauncher();
        var sut = CreateNode(device, registry, browser: browser);

        await sut.FetchXmlCommand!.ExecuteAsync(null);

        browser.Calls.Should().ContainSingle().Which.Should().Be(RootLocation);
    }

    [Fact]
    public async Task FetchXmlCommand_does_not_throw_when_launcher_returns_false()
    {
        var device = MakeDevice("root-uuid", "Speaker");
        var registry = new DeviceRegistry();
        registry.TryAddOrUpdate(device);

        var browser = new FakeBrowserLauncher { NextResult = false };
        var sut = CreateNode(device, registry, browser: browser);

        await FluentActions.Awaiting(() => sut.FetchXmlCommand!.ExecuteAsync(null))
            .Should().NotThrowAsync();
        browser.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task FetchXmlCommand_works_even_after_description_fetch_failed()
    {
        var device = MakeDevice("root-uuid", null);
        device.DescriptionFetchState = FetchState.Failed;
        device.DescriptionFetchError = "HTTP 500 Internal Server Error";

        var registry = new DeviceRegistry();
        registry.TryAddOrUpdate(device);

        var browser = new FakeBrowserLauncher();
        var sut = CreateNode(device, registry, browser: browser);

        await sut.ExpandAsync();
        await sut.FetchXmlCommand!.ExecuteAsync(null);

        sut.Children.Should().ContainSingle().Which.Should().BeOfType<string>();
        browser.Calls.Should().ContainSingle().Which.Should().Be(RootLocation);
    }

    private static DeviceNodeViewModel CreateNode(
        Device device,
        DeviceRegistry registry,
        AppShutdownTokenSource? shutdown = null,
        IBrowserLauncher? browser = null)
    {
        var s = shutdown ?? new AppShutdownTokenSource();
        var b = browser ?? new FakeBrowserLauncher();
        var dispatcher = new SynchronousDispatcher();
        var serviceFactory = new ServiceNodeFactory(new FakeScpdFetcher(), dispatcher, b, s);
        return new DeviceNodeViewModel(device, registry, serviceFactory, dispatcher, b, s.Token);
    }

    private static Device MakeDevice(string uuid, string? friendly) => new()
    {
        Uuid = uuid,
        FriendlyName = friendly,
        LocationUrl = RootLocation,
        LastSeenUtc = DateTimeOffset.UtcNow,
    };

    private static Service MakeRootService(string rootUuid, string serviceIdSuffix) => new()
    {
        OwningDeviceUuid = rootUuid,
        ContainingDeviceUdn = "uuid:" + rootUuid,
        ContainingDeviceFriendlyName = null,
        ServiceId = "urn:upnp-org:serviceId:" + serviceIdSuffix,
        ServiceType = "urn:schemas-upnp-org:service:" + serviceIdSuffix + ":1",
        ScpdUrl = new Uri("http://192.168.1.10:8080/" + serviceIdSuffix + "/scpd.xml"),
        ControlUrl = new Uri("http://192.168.1.10:8080/" + serviceIdSuffix + "/ctrl"),
        EventSubUrl = new Uri("http://192.168.1.10:8080/" + serviceIdSuffix + "/evt"),
    };

    private static Service MakeEmbeddedService(string udn, string friendly, string serviceIdSuffix) => new()
    {
        OwningDeviceUuid = "root-uuid",
        ContainingDeviceUdn = udn,
        ContainingDeviceFriendlyName = friendly,
        ServiceId = "urn:upnp-org:serviceId:" + serviceIdSuffix,
        ServiceType = "urn:schemas-upnp-org:service:AVTransport:1",
        ScpdUrl = new Uri("http://192.168.1.10:8080/" + udn + "/scpd.xml"),
        ControlUrl = new Uri("http://192.168.1.10:8080/" + udn + "/ctrl"),
        EventSubUrl = new Uri("http://192.168.1.10:8080/" + udn + "/evt"),
    };
}
