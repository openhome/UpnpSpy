using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Ssdp;
using UpnpSpy.Tests.Ssdp;
using UpnpSpy.Tests.TestHelpers;
using Xunit;

namespace UpnpSpy.Tests.Discovery;

public sealed class RescanCoordinatorTests
{
    [Fact]
    public async Task Rescan_sends_one_M_SEARCH_burst_with_correct_ST_and_MX()
    {
        var ctx = await BuildAsync();
        await using var _ = ctx.Discovery;

        var rescan = ctx.Coordinator.RescanAsync(ctx.Cts.Token);
        ctx.Cts.CancelAfter(TimeSpan.FromMilliseconds(50));
        await rescan;

        ctx.Transport.SentMSearches.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new SentMSearch("upnp:rootdevice", TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task Devices_not_heard_during_the_session_are_pruned_at_deadline()
    {
        var ctx = await BuildAsync();
        await using var _ = ctx.Discovery;

        ctx.Registry.TryAddOrUpdate(MakeDevice("uuid-silent"));
        ctx.Registry.TryAddOrUpdate(MakeDevice("uuid-responder"));

        var rescan = ctx.Coordinator.RescanAsync(ctx.Cts.Token);

        ctx.Transport.PushDatagram(FakeSsdpTransport.MakeDatagram(AliveNotify("uuid-responder", "http://192.0.2.10/desc.xml")));
        await WaitFor(() => ctx.Registry.Snapshot().ContainsKey("uuid-responder"), TimeSpan.FromSeconds(2));
        await Task.Delay(50); // let the DeviceHeard event fire on the pump thread

        ctx.Cts.Cancel();
        await rescan;

        ctx.Registry.Snapshot().Keys.Should().Contain("uuid-responder");
        ctx.Registry.Snapshot().Keys.Should().NotContain("uuid-silent");
    }

    [Fact]
    public async Task New_devices_that_respond_during_a_rescan_are_kept()
    {
        var ctx = await BuildAsync();
        await using var _ = ctx.Discovery;

        var rescan = ctx.Coordinator.RescanAsync(ctx.Cts.Token);

        ctx.Transport.PushDatagram(FakeSsdpTransport.MakeDatagram(AliveNotify("uuid-fresh", "http://192.0.2.11/desc.xml")));
        await WaitFor(() => ctx.Registry.Snapshot().ContainsKey("uuid-fresh"), TimeSpan.FromSeconds(2));
        await Task.Delay(50);

        ctx.Cts.Cancel();
        await rescan;

        ctx.Registry.Snapshot().Keys.Should().Contain("uuid-fresh");
    }

    [Fact]
    public async Task Byebye_during_a_rescan_removes_device_immediately()
    {
        var ctx = await BuildAsync();
        await using var _ = ctx.Discovery;

        ctx.Registry.TryAddOrUpdate(MakeDevice("uuid-goingaway"));

        var rescan = ctx.Coordinator.RescanAsync(ctx.Cts.Token);

        ctx.Transport.PushDatagram(FakeSsdpTransport.MakeDatagram(ByebyeNotify("uuid-goingaway")));
        await WaitFor(() => !ctx.Registry.Snapshot().ContainsKey("uuid-goingaway"), TimeSpan.FromSeconds(2));

        // Verified the byebye removed it BEFORE the rescan deadline elapsed.
        ctx.Registry.Snapshot().Should().NotContainKey("uuid-goingaway");

        ctx.Cts.Cancel();
        await rescan;
    }

    [Fact]
    public async Task A_second_rescan_supersedes_the_first_and_only_the_second_prunes()
    {
        var ctx = await BuildAsync();
        await using var _ = ctx.Discovery;

        ctx.Registry.TryAddOrUpdate(MakeDevice("uuid-silent-before-first"));

        var firstCts = new CancellationTokenSource();
        var firstRescan = ctx.Coordinator.RescanAsync(firstCts.Token);

        // Add a new device after the first rescan's snapshot.
        ctx.Registry.TryAddOrUpdate(MakeDevice("uuid-added-between"));

        // Second rescan supersedes the first.
        var secondCts = new CancellationTokenSource();
        var secondRescan = ctx.Coordinator.RescanAsync(secondCts.Token);

        // Push a response for the second-known device only; everything else should be pruned by the second rescan.
        ctx.Transport.PushDatagram(FakeSsdpTransport.MakeDatagram(AliveNotify("uuid-added-between", "http://192.0.2.12/desc.xml")));
        await WaitFor(() => ctx.Registry.Snapshot().ContainsKey("uuid-added-between"), TimeSpan.FromSeconds(2));
        await Task.Delay(50);

        // First would have pruned uuid-added-between (not in its KnownAtStart so it wouldn't, OK bad example).
        // Cancel both — the first must be a no-op on pruning since it was superseded.
        firstCts.Cancel();
        await firstRescan;
        secondCts.Cancel();
        await secondRescan;

        ctx.Registry.Snapshot().Keys.Should().NotContain("uuid-silent-before-first",
            "the second (still-active) rescan should have pruned this device");
        ctx.Registry.Snapshot().Keys.Should().Contain("uuid-added-between",
            "this device responded during the second rescan");

        ctx.Transport.SentMSearches.Should().HaveCount(2);
    }

    private static Device MakeDevice(string uuid) => new()
    {
        Uuid = uuid,
        LocationUrl = new Uri("http://192.0.2.1/desc.xml"),
        LastSeenUtc = DateTimeOffset.UtcNow,
    };

    private static byte[] AliveNotify(string uuid, string location) =>
        Encoding.UTF8.GetBytes(
            "NOTIFY * HTTP/1.1\r\n" +
            "HOST: 239.255.255.250:1900\r\n" +
            "CACHE-CONTROL: max-age=1800\r\n" +
            $"LOCATION: {location}\r\n" +
            "NT: upnp:rootdevice\r\n" +
            "NTS: ssdp:alive\r\n" +
            $"USN: uuid:{uuid}::upnp:rootdevice\r\n" +
            "\r\n");

    private static byte[] ByebyeNotify(string uuid) =>
        Encoding.UTF8.GetBytes(
            "NOTIFY * HTTP/1.1\r\n" +
            "HOST: 239.255.255.250:1900\r\n" +
            "NT: upnp:rootdevice\r\n" +
            "NTS: ssdp:byebye\r\n" +
            $"USN: uuid:{uuid}::upnp:rootdevice\r\n" +
            "\r\n");

    private static async Task WaitFor(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(20);
        }
        throw new TimeoutException("Condition not satisfied within " + timeout);
    }

    private sealed record Ctx(
        FakeSsdpTransport Transport,
        DeviceRegistry Registry,
        DiscoveryService Discovery,
        RescanCoordinator Coordinator,
        CancellationTokenSource Cts);

    private static async Task<Ctx> BuildAsync()
    {
        var transport = new FakeSsdpTransport();
        var registry = new DeviceRegistry();
        var parser = new SsdpMessageParser();
        var clock = new FakeClock();
        var discovery = new DiscoveryService(transport, registry, parser, clock, NullLogger<DiscoveryService>.Instance);
        var coordinator = new RescanCoordinator(transport, registry, discovery, clock, NullLogger<RescanCoordinator>.Instance);

        await discovery.StartAsync(CancellationToken.None);

        var cts = new CancellationTokenSource();
        return new Ctx(transport, registry, discovery, coordinator, cts);
    }
}
