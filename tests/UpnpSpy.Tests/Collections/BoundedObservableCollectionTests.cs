using System.Collections.Specialized;
using FluentAssertions;
using UpnpSpy.Core.Collections;
using Xunit;

namespace UpnpSpy.Tests.Collections;

public sealed class BoundedObservableCollectionTests
{
    [Fact]
    public void Constructor_rejects_non_positive_capacity()
    {
        FluentActions.Invoking(() => new BoundedObservableCollection<int>(0))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new BoundedObservableCollection<int>(-1))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Add_below_capacity_appends()
    {
        var sut = new BoundedObservableCollection<int>(3);

        sut.Add(1);
        sut.Add(2);

        sut.Should().Equal(1, 2);
        sut.Capacity.Should().Be(3);
    }

    [Fact]
    public void Add_at_capacity_evicts_oldest()
    {
        var sut = new BoundedObservableCollection<int>(3) { 1, 2, 3 };

        sut.Add(4);

        sut.Should().Equal(2, 3, 4);
        sut.Count.Should().Be(3);
    }

    [Fact]
    public void Add_at_capacity_raises_remove_then_add_notifications()
    {
        var sut = new BoundedObservableCollection<string>(2) { "a", "b" };
        var actions = new List<NotifyCollectionChangedAction>();
        sut.CollectionChanged += (_, e) => actions.Add(e.Action);

        sut.Add("c");

        actions.Should().Equal(NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Add);
        sut.Should().Equal("b", "c");
    }

    [Fact]
    public void Stress_50k_inserts_into_capacity_1000_stays_bounded_and_retains_last()
    {
        var sut = new BoundedObservableCollection<int>(1_000);

        for (var i = 0; i < 50_000; i++)
            sut.Add(i);

        sut.Count.Should().Be(1_000);
        sut[0].Should().Be(49_000);
        sut[^1].Should().Be(49_999);
    }

    [Fact]
    public void EvictTail_at_capacity_drops_the_tail_keeping_newer_head_inserts()
    {
        // FR-033 mode: subscription-popup event list inserts newest at index 0.
        // When at capacity the oldest item (now at the tail) must be evicted.
        var sut = new BoundedObservableCollection<int>(3, BoundedEvictionMode.EvictTail);
        sut.Insert(0, 1);
        sut.Insert(0, 2);
        sut.Insert(0, 3);

        sut.Insert(0, 4);

        sut.Should().Equal(4, 3, 2);
        sut.Count.Should().Be(3);
    }

    [Fact]
    public void EvictTail_raises_remove_then_add_notifications_on_overflow()
    {
        var sut = new BoundedObservableCollection<string>(2, BoundedEvictionMode.EvictTail);
        sut.Insert(0, "a");
        sut.Insert(0, "b");

        var actions = new List<NotifyCollectionChangedAction>();
        sut.CollectionChanged += (_, e) => actions.Add(e.Action);

        sut.Insert(0, "c");

        actions.Should().Equal(NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Add);
        sut.Should().Equal("c", "b");
    }

    [Fact]
    public void EvictTail_stress_50k_front_inserts_into_capacity_1000_keeps_newest_at_index_0()
    {
        var sut = new BoundedObservableCollection<int>(1_000, BoundedEvictionMode.EvictTail);

        for (var i = 0; i < 50_000; i++)
            sut.Insert(0, i);

        sut.Count.Should().Be(1_000);
        sut[0].Should().Be(49_999, "newest insert lives at the head");
        sut[^1].Should().Be(49_000, "oldest retained item lives at the tail");
    }
}
