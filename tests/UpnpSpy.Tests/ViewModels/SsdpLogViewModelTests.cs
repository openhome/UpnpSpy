using FluentAssertions;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.ViewModels;
using UpnpSpy.Tests.TestHelpers;
using Xunit;

namespace UpnpSpy.Tests.ViewModels;

public sealed class SsdpLogViewModelTests
{
    [Fact]
    public void Alive_entry_is_appended_with_kind_alive()
    {
        var sut = new SsdpLogViewModel(new SynchronousDispatcher());

        sut.Append(MakeEntry("uuid-a", SsdpKind.Alive));

        sut.Entries.Should().HaveCount(1);
        sut.Entries[0].Kind.Should().Be(SsdpKind.Alive);
        sut.Entries[0].DeviceUuid.Should().Be("uuid-a");
    }

    [Fact]
    public void Byebye_entry_is_appended_with_kind_byebye()
    {
        var sut = new SsdpLogViewModel(new SynchronousDispatcher());

        sut.Append(MakeEntry("uuid-a", SsdpKind.Byebye));

        sut.Entries.Should().HaveCount(1);
        sut.Entries[0].Kind.Should().Be(SsdpKind.Byebye);
    }

    [Fact]
    public void Entries_are_ordered_newest_first()
    {
        // FR-055: most recently received advertisement at index 0, oldest at ^1.
        var sut = new SsdpLogViewModel(new SynchronousDispatcher());
        var first = MakeEntry("uuid-a", SsdpKind.Alive);
        var second = MakeEntry("uuid-b", SsdpKind.Alive);
        var third = MakeEntry("uuid-a", SsdpKind.Byebye);

        sut.Append(first);
        sut.Append(second);
        sut.Append(third);

        sut.Entries.Should().Equal(third, second, first);
    }

    [Fact]
    public void Capacity_is_10000_by_default()
    {
        var sut = new SsdpLogViewModel(new SynchronousDispatcher());

        sut.Entries.Capacity.Should().Be(10_000);
    }

    [Fact]
    public void Adding_beyond_capacity_evicts_oldest_from_tail()
    {
        // FR-016 + FR-055: arrivals enter at index 0, the oldest entry sits at the
        // tail, so eviction drops the tail. After a,b,c,d with capacity 3 the
        // expected newest-first order is d,c,b — `a` is evicted from the bottom.
        var sut = new SsdpLogViewModel(new SynchronousDispatcher(), capacity: 3);
        var a = MakeEntry("uuid-a", SsdpKind.Alive);
        var b = MakeEntry("uuid-b", SsdpKind.Alive);
        var c = MakeEntry("uuid-c", SsdpKind.Alive);
        var d = MakeEntry("uuid-d", SsdpKind.Alive);

        sut.Append(a);
        sut.Append(b);
        sut.Append(c);
        sut.Append(d);

        sut.Entries.Should().HaveCount(3);
        sut.Entries.Should().Equal(d, c, b);
    }

    [Fact]
    public void Large_stress_stays_bounded()
    {
        // FR-055: index 0 is the most recently appended; the tail is the oldest
        // of the 10,000 retained entries (uuid-40000 .. uuid-49999 survive).
        var sut = new SsdpLogViewModel(new SynchronousDispatcher());

        for (var i = 0; i < 50_000; i++)
            sut.Append(MakeEntry($"uuid-{i}", SsdpKind.Alive));

        sut.Entries.Should().HaveCount(10_000);
        sut.Entries[0].DeviceUuid.Should().Be("uuid-49999");
        sut.Entries[^1].DeviceUuid.Should().Be("uuid-40000");
    }

    [Fact]
    public void Append_null_throws()
    {
        var sut = new SsdpLogViewModel(new SynchronousDispatcher());

        var act = () => sut.Append(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static SsdpLogEntry MakeEntry(string uuid, SsdpKind kind) => new(
        ReceivedUtc: DateTimeOffset.UtcNow,
        Kind: kind,
        DeviceUuid: uuid,
        Nt: "upnp:rootdevice",
        SourceInterfaceName: "Ethernet");
}
