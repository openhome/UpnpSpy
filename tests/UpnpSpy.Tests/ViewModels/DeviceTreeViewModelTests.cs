using FluentAssertions;
using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.ViewModels;
using UpnpSpy.Tests.TestHelpers;
using Xunit;

namespace UpnpSpy.Tests.ViewModels;

public sealed class DeviceTreeViewModelTests
{
    [Fact]
    public void DeviceAdded_event_for_already_loaded_device_appends_a_node()
    {
        // The production dispatcher transitions NotFetched → Fetching → Loaded
        // before calling NotifyUpdated; this test exercises the rare path where
        // a device enters the registry already in the Loaded state.
        var registry = new DeviceRegistry();
        var sut = new DeviceTreeViewModel(registry, new SynchronousDispatcher());

        registry.TryAddOrUpdate(Make("uuid-a", "Speaker", FetchState.Loaded));

        sut.Devices.Should().HaveCount(1);
        sut.Devices[0].Device.Uuid.Should().Be("uuid-a");
        sut.Devices[0].Label.Should().Be("Speaker");
    }

    [Fact]
    public void Same_uuid_added_twice_does_not_duplicate()
    {
        var registry = new DeviceRegistry();
        var sut = new DeviceTreeViewModel(registry, new SynchronousDispatcher());

        registry.TryAddOrUpdate(Make("uuid-a", "Speaker", FetchState.Loaded));
        registry.TryAddOrUpdate(Make("uuid-a", "Speaker", FetchState.Loaded));

        sut.Devices.Should().HaveCount(1);
    }

    [Fact]
    public void DeviceRemoved_event_removes_matching_node()
    {
        var registry = new DeviceRegistry();
        var sut = new DeviceTreeViewModel(registry, new SynchronousDispatcher());
        registry.TryAddOrUpdate(Make("uuid-a", "Speaker", FetchState.Loaded));

        registry.Remove("uuid-a");

        sut.Devices.Should().BeEmpty();
    }

    [Fact]
    public void DeviceUpdated_event_refreshes_label_in_place()
    {
        var registry = new DeviceRegistry();
        var sut = new DeviceTreeViewModel(registry, new SynchronousDispatcher());
        registry.TryAddOrUpdate(Make("uuid-a", "Speaker", FetchState.Loaded));
        var nodeBefore = sut.Devices[0];

        registry.TryAddOrUpdate(Make("uuid-a", "Speaker 2", FetchState.Loaded));

        sut.Devices.Should().HaveCount(1);
        ReferenceEquals(sut.Devices[0], nodeBefore).Should().BeTrue("update mutates in place");
        sut.Devices[0].Label.Should().Be("Speaker 2");
    }

    [Fact]
    public void Byebye_for_an_observed_device_removes_without_throwing()
    {
        var registry = new DeviceRegistry();
        var sut = new DeviceTreeViewModel(registry, new SynchronousDispatcher());
        registry.TryAddOrUpdate(Make("uuid-a", "Speaker", FetchState.Loaded));
        registry.TryAddOrUpdate(Make("uuid-a", "Speaker 2", FetchState.Loaded));
        var node = sut.Devices[0];
        _ = node.Label;

        var remove = () => registry.Remove("uuid-a");

        remove.Should().NotThrow();
        sut.Devices.Should().BeEmpty();
    }

    [Fact]
    public void Rapid_alive_byebye_alive_leaves_one_node()
    {
        var registry = new DeviceRegistry();
        var sut = new DeviceTreeViewModel(registry, new SynchronousDispatcher());

        registry.TryAddOrUpdate(Make("uuid-a", "Speaker", FetchState.Loaded));
        registry.Remove("uuid-a");
        registry.TryAddOrUpdate(Make("uuid-a", "Speaker", FetchState.Loaded));

        sut.Devices.Should().HaveCount(1);
        sut.Devices[0].Device.Uuid.Should().Be("uuid-a");
    }

    [Fact]
    public void Loaded_devices_already_in_registry_at_construction_are_seeded()
    {
        var registry = new DeviceRegistry();
        registry.TryAddOrUpdate(Make("uuid-a", "Speaker", FetchState.Loaded));
        registry.TryAddOrUpdate(Make("uuid-b", "Display", FetchState.Loaded));

        var sut = new DeviceTreeViewModel(registry, new SynchronousDispatcher());

        sut.Devices.Should().HaveCount(2);
    }

    [Fact]
    public void Loaded_device_with_no_friendly_name_falls_back_to_uuid_label()
    {
        // FR-010 (post-FR-047): the uuid: fallback applies to a successfully-
        // fetched description that simply lacks a <friendlyName> element. A
        // failed fetch hides the device entirely (FR-047).
        var registry = new DeviceRegistry();
        var sut = new DeviceTreeViewModel(registry, new SynchronousDispatcher());

        registry.TryAddOrUpdate(Make("uuid-anonymous", friendly: null, FetchState.Loaded));

        sut.Devices.Should().HaveCount(1);
        sut.Devices[0].Label.Should().Be("uuid:uuid-anonymous");
    }

    [Fact]
    public void Device_in_NotFetched_state_does_not_appear_in_tree()
    {
        var registry = new DeviceRegistry();
        var sut = new DeviceTreeViewModel(registry, new SynchronousDispatcher());

        registry.TryAddOrUpdate(Make("uuid-pending", "Speaker", FetchState.NotFetched));

        sut.Devices.Should().BeEmpty();
    }

    [Fact]
    public void Device_in_Fetching_state_does_not_appear_in_tree()
    {
        var registry = new DeviceRegistry();
        var sut = new DeviceTreeViewModel(registry, new SynchronousDispatcher());

        registry.TryAddOrUpdate(Make("uuid-fetching", "Speaker", FetchState.Fetching));

        sut.Devices.Should().BeEmpty();
    }

    [Fact]
    public void Device_in_Failed_state_does_not_appear_in_tree()
    {
        var registry = new DeviceRegistry();
        var sut = new DeviceTreeViewModel(registry, new SynchronousDispatcher());

        var failed = Make("uuid-broken", friendly: null, FetchState.Failed);
        failed.DescriptionFetchError = "HTTP 500";
        registry.TryAddOrUpdate(failed);

        sut.Devices.Should().BeEmpty();
    }

    [Fact]
    public void Failed_device_remains_in_registry_but_not_in_tree()
    {
        // Visibility (tree) and membership (registry) are decoupled. The
        // dispatcher and byebye handler still address the device by UUID
        // while the user sees nothing in the tree; the failure is recorded
        // separately in the diagnostic ring (FR-039), tested elsewhere.
        var registry = new DeviceRegistry();
        var sut = new DeviceTreeViewModel(registry, new SynchronousDispatcher());

        var failed = Make("uuid-broken", friendly: null, FetchState.Failed);
        failed.DescriptionFetchError = "transport error";
        registry.TryAddOrUpdate(failed);

        registry.Contains("uuid-broken").Should().BeTrue();
        sut.Devices.Should().BeEmpty();
    }

    [Fact]
    public void Device_promoted_to_Loaded_appears_in_tree_via_DeviceUpdated()
    {
        // Production path: SSDP add → DeviceAdded (state=NotFetched, no tree
        // entry). Dispatcher fetches description, mutates state to Loaded,
        // calls NotifyUpdated. The tree picks the device up at that moment.
        var registry = new DeviceRegistry();
        var sut = new DeviceTreeViewModel(registry, new SynchronousDispatcher());

        var device = Make("uuid-promote", friendly: null, FetchState.NotFetched);
        registry.TryAddOrUpdate(device);
        sut.Devices.Should().BeEmpty();

        // Simulate dispatcher writing description-derived fields then
        // notifying the registry.
        device.FriendlyName = "Discovered Speaker";
        device.DescriptionFetchState = FetchState.Loaded;
        registry.NotifyUpdated(device.Uuid);

        sut.Devices.Should().HaveCount(1);
        sut.Devices[0].Device.Uuid.Should().Be("uuid-promote");
        sut.Devices[0].Label.Should().Be("Discovered Speaker");
    }

    [Fact]
    public void Device_promoted_to_Failed_does_not_appear_in_tree()
    {
        var registry = new DeviceRegistry();
        var sut = new DeviceTreeViewModel(registry, new SynchronousDispatcher());

        var device = Make("uuid-doomed", friendly: null, FetchState.NotFetched);
        registry.TryAddOrUpdate(device);
        device.DescriptionFetchState = FetchState.Failed;
        device.DescriptionFetchError = "HTTP 404";
        registry.NotifyUpdated(device.Uuid);

        sut.Devices.Should().BeEmpty();
    }

    private static Device Make(string uuid, string? friendly, FetchState state = FetchState.NotFetched) => new()
    {
        Uuid = uuid,
        FriendlyName = friendly,
        LocationUrl = new Uri("http://192.0.2.1/desc.xml"),
        LastSeenUtc = DateTimeOffset.UtcNow,
        DescriptionFetchState = state,
    };
}
