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

        // FR-054: seeded snapshot is sorted alphabetically by friendly name.
        sut.Devices.Should().HaveCount(2);
        sut.Devices.Select(d => d.Label).Should().Equal("Display", "Speaker");
    }

    [Fact]
    public void Devices_added_out_of_order_end_up_alphabetically_sorted()
    {
        // FR-054: a discovery burst can return devices in any order; the tree
        // surfaces them in case-insensitive friendly-name order regardless.
        var registry = new DeviceRegistry();
        var sut = new DeviceTreeViewModel(registry, new SynchronousDispatcher());

        registry.TryAddOrUpdate(Make("uuid-z", "Zebra", FetchState.Loaded));
        registry.TryAddOrUpdate(Make("uuid-a", "apple", FetchState.Loaded));
        registry.TryAddOrUpdate(Make("uuid-m", "Mango", FetchState.Loaded));

        sut.Devices.Select(d => d.Label).Should().Equal("apple", "Mango", "Zebra");
    }

    [Fact]
    public void Rename_moves_device_to_new_sorted_position()
    {
        // FR-054 + edge case: when a device re-announces with a new friendly name,
        // the tree row migrates to its new alphabetical slot. The node identity
        // is preserved (Move, not Remove+Insert) so any selection / expansion
        // state attached to the row survives the relocation.
        var registry = new DeviceRegistry();
        var sut = new DeviceTreeViewModel(registry, new SynchronousDispatcher());
        registry.TryAddOrUpdate(Make("uuid-a", "Apple", FetchState.Loaded));
        registry.TryAddOrUpdate(Make("uuid-m", "Mango", FetchState.Loaded));
        registry.TryAddOrUpdate(Make("uuid-z", "Zebra", FetchState.Loaded));
        var mangoNode = sut.Devices[1];

        registry.TryAddOrUpdate(Make("uuid-m", "Yak", FetchState.Loaded));

        sut.Devices.Select(d => d.Label).Should().Equal("Apple", "Yak", "Zebra");
        ReferenceEquals(sut.Devices[1], mangoNode).Should().BeTrue("rename preserves node identity");
    }

    [Fact]
    public void Devices_sharing_a_label_are_ordered_by_uuid()
    {
        // FR-054 tiebreak: if two devices report identical friendly names, UUID
        // ordering gives them a stable, deterministic position so the user
        // doesn't see them swap rows between sessions or rescans.
        var registry = new DeviceRegistry();
        var sut = new DeviceTreeViewModel(registry, new SynchronousDispatcher());

        registry.TryAddOrUpdate(Make("uuid-b", "Living Room", FetchState.Loaded));
        registry.TryAddOrUpdate(Make("uuid-a", "Living Room", FetchState.Loaded));

        sut.Devices.Select(d => d.Device.Uuid).Should().Equal("uuid-a", "uuid-b");
    }

    [Fact]
    public void Promotion_to_Loaded_inserts_at_sorted_position()
    {
        // FR-047 promotion + FR-054 ordering: a device whose eager fetch lands
        // late should slot into the existing alphabetical order, not append.
        var registry = new DeviceRegistry();
        var sut = new DeviceTreeViewModel(registry, new SynchronousDispatcher());
        registry.TryAddOrUpdate(Make("uuid-a", "Apple", FetchState.Loaded));
        registry.TryAddOrUpdate(Make("uuid-z", "Zebra", FetchState.Loaded));

        var late = Make("uuid-m", friendly: null, FetchState.NotFetched);
        registry.TryAddOrUpdate(late);
        late.FriendlyName = "Mango";
        late.DescriptionFetchState = FetchState.Loaded;
        registry.NotifyUpdated(late.Uuid);

        sut.Devices.Select(d => d.Label).Should().Equal("Apple", "Mango", "Zebra");
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
