using System.Net;
using FluentAssertions;
using UpnpSpy.Core.Platform;
using Xunit;

namespace UpnpSpy.Tests.Platform;

public sealed class NetworkAdapterSelectorTests
{
    [Fact]
    public void Default_selection_is_the_first_eligible_adapter()
    {
        var enumerator = new FakeEnumerator(
            new EligibleInterface("eth0", IPAddress.Parse("192.168.1.10")),
            new EligibleInterface("wlan0", IPAddress.Parse("10.0.0.5")));

        var sut = new NetworkAdapterSelector(enumerator);

        sut.Available.Should().HaveCount(2);
        sut.Selected!.Name.Should().Be("eth0");
    }

    [Fact]
    public void Empty_enumerator_yields_null_selection_with_no_event()
    {
        var enumerator = new FakeEnumerator();
        var events = new List<AdapterSelectionChanged>();
        var sut = new NetworkAdapterSelector(enumerator);
        sut.Changed += events.Add;

        sut.Selected.Should().BeNull();
        sut.Available.Should().BeEmpty();
        events.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectAsync_raises_Changed_with_previous_and_current()
    {
        var eth = new EligibleInterface("eth0", IPAddress.Parse("192.168.1.10"));
        var wlan = new EligibleInterface("wlan0", IPAddress.Parse("10.0.0.5"));
        var enumerator = new FakeEnumerator(eth, wlan);
        var sut = new NetworkAdapterSelector(enumerator);
        AdapterSelectionChanged? captured = null;
        sut.Changed += e => captured = e;

        await sut.SelectAsync(wlan, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Previous!.Name.Should().Be("eth0");
        captured.Current!.Name.Should().Be("wlan0");
        sut.Selected!.Name.Should().Be("wlan0");
    }

    [Fact]
    public async Task Reselecting_same_adapter_is_a_noop()
    {
        var eth = new EligibleInterface("eth0", IPAddress.Parse("192.168.1.10"));
        var enumerator = new FakeEnumerator(eth);
        var sut = new NetworkAdapterSelector(enumerator);
        var events = new List<AdapterSelectionChanged>();
        sut.Changed += events.Add;

        await sut.SelectAsync(eth, CancellationToken.None);

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectAsync_on_unknown_adapter_throws()
    {
        var eth = new EligibleInterface("eth0", IPAddress.Parse("192.168.1.10"));
        var enumerator = new FakeEnumerator(eth);
        var sut = new NetworkAdapterSelector(enumerator);
        var unknown = new EligibleInterface("nope0", IPAddress.Parse("172.16.0.1"));

        await FluentActions.Awaiting(() => sut.SelectAsync(unknown, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    private sealed class FakeEnumerator : INetworkInterfaceEnumerator
    {
        private readonly EligibleInterface[] _items;
        public FakeEnumerator(params EligibleInterface[] items) => _items = items;
        public IReadOnlyList<EligibleInterface> EnumerateEligible() => _items;
    }
}
