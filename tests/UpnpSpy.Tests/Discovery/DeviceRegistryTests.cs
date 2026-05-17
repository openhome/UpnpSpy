using FluentAssertions;
using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Models;
using Xunit;

namespace UpnpSpy.Tests.Discovery;

public sealed class DeviceRegistryTests
{
    [Fact]
    public void First_alive_adds_device_and_raises_added_event()
    {
        var sut = new DeviceRegistry();
        var events = new List<DeviceRegistryEvent>();
        sut.DeviceAdded += e => events.Add(e);

        sut.TryAddOrUpdate(Make("uuid-a", friendly: "Speaker"));

        events.Should().HaveCount(1);
        events[0].Should().BeOfType<DeviceAddedEvent>();
        ((DeviceAddedEvent)events[0]).Device.Uuid.Should().Be("uuid-a");
        sut.Contains("uuid-a").Should().BeTrue();
    }

    [Fact]
    public void Second_alive_for_known_uuid_does_not_duplicate_and_returns_canonical_instance()
    {
        var sut = new DeviceRegistry();
        var first = sut.TryAddOrUpdate(Make("uuid-a", friendly: "Speaker"));
        var second = sut.TryAddOrUpdate(Make("uuid-a", friendly: "Speaker"));

        sut.Snapshot().Should().HaveCount(1);
        ReferenceEquals(first, second).Should().BeTrue();
    }

    [Fact]
    public void Same_uuid_with_unchanged_friendly_name_does_not_raise_updated()
    {
        var sut = new DeviceRegistry();
        sut.TryAddOrUpdate(Make("uuid-a", friendly: "Speaker"));

        var updates = new List<DeviceUpdatedEvent>();
        sut.DeviceUpdated += updates.Add;

        sut.TryAddOrUpdate(Make("uuid-a", friendly: "Speaker"));

        updates.Should().BeEmpty();
    }

    [Fact]
    public void Same_uuid_with_new_friendly_name_raises_updated_and_mutates_in_place()
    {
        var sut = new DeviceRegistry();
        var first = sut.TryAddOrUpdate(Make("uuid-a", friendly: "Speaker"));

        var updates = new List<DeviceUpdatedEvent>();
        sut.DeviceUpdated += updates.Add;

        sut.TryAddOrUpdate(Make("uuid-a", friendly: "Speaker 2"));

        updates.Should().HaveCount(1);
        first.FriendlyName.Should().Be("Speaker 2");
    }

    [Fact]
    public void Byebye_for_unknown_uuid_is_a_silent_no_op()
    {
        var sut = new DeviceRegistry();
        var raised = new List<DeviceRemovedEvent>();
        sut.DeviceRemoved += raised.Add;

        sut.Remove("uuid-x").Should().BeFalse();
        raised.Should().BeEmpty();
    }

    [Fact]
    public void Byebye_removes_known_device_and_raises_removed()
    {
        var sut = new DeviceRegistry();
        sut.TryAddOrUpdate(Make("uuid-a", friendly: "Speaker"));
        var raised = new List<DeviceRemovedEvent>();
        sut.DeviceRemoved += raised.Add;

        sut.Remove("uuid-a").Should().BeTrue();

        sut.Contains("uuid-a").Should().BeFalse();
        raised.Should().ContainSingle().Which.Uuid.Should().Be("uuid-a");
    }

    [Fact]
    public void Rapid_alive_byebye_alive_ends_with_device_present_and_events_in_order()
    {
        var sut = new DeviceRegistry();
        var log = new List<DeviceRegistryEvent>();
        sut.DeviceAdded += e => log.Add(e);
        sut.DeviceRemoved += e => log.Add(e);
        sut.DeviceUpdated += e => log.Add(e);

        sut.TryAddOrUpdate(Make("uuid-a", friendly: "Speaker"));
        sut.Remove("uuid-a");
        sut.TryAddOrUpdate(Make("uuid-a", friendly: "Speaker"));

        sut.Contains("uuid-a").Should().BeTrue();
        sut.Snapshot().Should().HaveCount(1);
        log.Select(e => e.GetType()).Should().Equal(
            typeof(DeviceAddedEvent),
            typeof(DeviceRemovedEvent),
            typeof(DeviceAddedEvent));
    }

    [Fact]
    public void NotifyUpdated_raises_DeviceUpdated_with_the_canonical_device()
    {
        var sut = new DeviceRegistry();
        var canonical = sut.TryAddOrUpdate(Make("uuid-a", friendly: "Speaker"));

        var updates = new List<DeviceUpdatedEvent>();
        sut.DeviceUpdated += updates.Add;

        sut.NotifyUpdated("uuid-a");

        updates.Should().ContainSingle();
        ReferenceEquals(updates[0].Device, canonical).Should().BeTrue();
    }

    [Fact]
    public void NotifyUpdated_for_unknown_uuid_is_a_silent_no_op()
    {
        var sut = new DeviceRegistry();
        var updates = new List<DeviceUpdatedEvent>();
        sut.DeviceUpdated += updates.Add;

        sut.NotifyUpdated("uuid-x");

        updates.Should().BeEmpty();
    }

    [Fact]
    public void NotifyUpdated_with_blank_uuid_throws()
    {
        var sut = new DeviceRegistry();
        var act = () => sut.NotifyUpdated("   ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Concurrent_writes_remain_consistent()
    {
        var sut = new DeviceRegistry();
        Parallel.For(0, 1000, i =>
        {
            sut.TryAddOrUpdate(Make($"uuid-{i % 10}", friendly: $"d{i % 10}"));
        });

        sut.Snapshot().Should().HaveCount(10);
    }

    private static Device Make(string uuid, string? friendly = null) => new()
    {
        Uuid = uuid,
        FriendlyName = friendly,
        LocationUrl = new Uri($"http://192.0.2.{uuid.GetHashCode() & 0xff}/desc.xml"),
        LastSeenUtc = DateTimeOffset.UtcNow,
    };
}
