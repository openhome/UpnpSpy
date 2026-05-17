using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using UpnpSpy.Core.Description;
using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Lifecycle;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Ssdp;
using UpnpSpy.Core.ViewModels;
using UpnpSpy.Tests.Description;
using UpnpSpy.Tests.Ssdp;
using UpnpSpy.Tests.TestHelpers;
using Xunit;

namespace UpnpSpy.Tests.Discovery;

public sealed class DiscoveryServiceTests
{
    [Fact]
    public async Task StartAsync_invokes_transport_StartAsync()
    {
        var (sut, transport, _) = Build();
        await using var _holder = sut;

        await sut.StartAsync(CancellationToken.None);
        transport.Started.Should().BeTrue();
    }

    [Fact]
    public async Task RunStartupDiscoveryAsync_sends_M_SEARCH_with_upnp_rootdevice_and_MX_3s()
    {
        var (sut, transport, _) = Build();
        await using var _holder = sut;
        await sut.StartAsync(CancellationToken.None);

        // Cancel the 4s grace immediately — we only care about the SendMSearch params.
        var fastCts = new CancellationTokenSource();
        fastCts.CancelAfter(TimeSpan.FromMilliseconds(50));
        await sut.RunStartupDiscoveryAsync(fastCts.Token);

        transport.SentMSearches.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new SentMSearch("upnp:rootdevice", TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task Incoming_alive_NOTIFY_is_added_to_registry()
    {
        var (sut, transport, registry) = Build();
        await using var _holder = sut;
        await sut.StartAsync(CancellationToken.None);

        var added = new TaskCompletionSource<DeviceAddedEvent>();
        registry.DeviceAdded += e => added.TrySetResult(e);

        transport.PushDatagram(FakeSsdpTransport.MakeDatagram(AliveNotify("uuid-z", "http://192.0.2.7/desc.xml")));

        var evt = await added.Task.WaitAsync(TimeSpan.FromSeconds(2));
        evt.Device.Uuid.Should().Be("uuid-z");
        evt.Device.LocationUrl.Should().Be(new Uri("http://192.0.2.7/desc.xml"));
    }

    [Fact]
    public async Task Duplicate_alive_does_not_create_a_second_registry_entry()
    {
        var (sut, transport, registry) = Build();
        await using var _holder = sut;
        await sut.StartAsync(CancellationToken.None);

        var addCount = 0;
        registry.DeviceAdded += _ => Interlocked.Increment(ref addCount);

        var payload = AliveNotify("uuid-q", "http://192.0.2.9/desc.xml");
        transport.PushDatagram(FakeSsdpTransport.MakeDatagram(payload));
        transport.PushDatagram(FakeSsdpTransport.MakeDatagram(payload));

        // Wait for both datagrams to flow through the pump.
        await WaitFor(() => registry.Snapshot().ContainsKey("uuid-q"), timeout: TimeSpan.FromSeconds(2));
        await Task.Delay(100); // give second to arrive

        registry.Snapshot().Should().HaveCount(1);
        addCount.Should().Be(1);
    }

    [Fact]
    public async Task Incoming_byebye_removes_device_from_registry()
    {
        var (sut, transport, registry) = Build();
        await using var _holder = sut;
        await sut.StartAsync(CancellationToken.None);

        transport.PushDatagram(FakeSsdpTransport.MakeDatagram(AliveNotify("uuid-b", "http://192.0.2.8/desc.xml")));
        await WaitFor(() => registry.Snapshot().ContainsKey("uuid-b"), TimeSpan.FromSeconds(2));

        var removed = new TaskCompletionSource<DeviceRemovedEvent>();
        registry.DeviceRemoved += e => removed.TrySetResult(e);

        transport.PushDatagram(FakeSsdpTransport.MakeDatagram(ByebyeNotify("uuid-b")));

        var evt = await removed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        evt.Uuid.Should().Be("uuid-b");
        registry.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task Alive_NOTIFY_to_dispatcher_resolves_FriendlyName_without_DiscoveryService_calling_fetcher()
    {
        // T145 / FR-043 integration: confirms that the description fetch happens
        // through EagerDescriptionDispatcher (which subscribes to the registry),
        // not through DiscoveryService directly. DiscoveryService is constructed
        // without any IDeviceDescriptionFetcher reference.
        var (sut, transport, registry) = Build();
        await using var _holder = sut;
        await sut.StartAsync(CancellationToken.None);

        var fetcher = new FakeDeviceDescriptionFetcher();
        var location = new Uri("http://192.0.2.7/desc.xml");
        fetcher.SetResult(location, new DeviceDescriptionFetchResult.Success(
            DeviceDescription.Minimum("uuid-z", "Discovered")));

        var shutdown = new AppShutdownTokenSource();
        using var dispatcher = new EagerDescriptionDispatcher(
            registry, fetcher, new FakeClock(), shutdown,
            NullLogger<EagerDescriptionDispatcher>.Instance);
        dispatcher.Start();

        transport.PushDatagram(FakeSsdpTransport.MakeDatagram(AliveNotify("uuid-z", location.AbsoluteUri)));

        await WaitFor(() => registry.Snapshot().TryGetValue("uuid-z", out var d)
            && d.DescriptionFetchState == FetchState.Loaded, TimeSpan.FromSeconds(2));

        registry.Snapshot()["uuid-z"].FriendlyName.Should().Be("Discovered");
        fetcher.CallsFor(location).Should().Be(1);
        shutdown.Dispose();
    }

    [Fact]
    public async Task Alive_NOTIFY_with_non_root_NT_does_not_register_a_device()
    {
        // Spec Assumptions: only root devices materialise as tree entries. An
        // IGD chassis emits one alive per (root|embedded device|service) — only
        // the NT=upnp:rootdevice one should add to the registry.
        var (sut, transport, registry) = Build();
        await using var _holder = sut;
        await sut.StartAsync(CancellationToken.None);

        var added = 0;
        registry.DeviceAdded += _ => Interlocked.Increment(ref added);

        // Embedded device alive (NT=urn:...:device:WANDevice:1) — should NOT register.
        transport.PushDatagram(FakeSsdpTransport.MakeDatagram(AliveNotifyWithNt(
            uuid: "uuid-wan",
            location: "http://192.0.2.7/desc.xml",
            nt: "urn:schemas-upnp-org:device:WANDevice:1")));

        // Service alive — should NOT register.
        transport.PushDatagram(FakeSsdpTransport.MakeDatagram(AliveNotifyWithNt(
            uuid: "uuid-svc",
            location: "http://192.0.2.7/desc.xml",
            nt: "urn:schemas-upnp-org:service:Layer3Forwarding:1")));

        await Task.Delay(150);

        added.Should().Be(0);
        registry.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task Alive_NOTIFY_with_non_root_NT_still_appears_in_the_SSDP_log()
    {
        // FR-014: every alive advertisement appears in the right pane regardless
        // of whether it triggered a registry entry.
        var transport = new FakeSsdpTransport();
        var registry = new DeviceRegistry();
        var parser = new SsdpMessageParser();
        var clock = new FakeClock();
        var ssdpLog = new SsdpLogViewModel(new SynchronousDispatcher());

        var sut = new DiscoveryService(transport, registry, parser, clock,
            NullLogger<DiscoveryService>.Instance, diagnostics: null, ssdpLog: ssdpLog);
        await using var _holder = sut;
        await sut.StartAsync(CancellationToken.None);

        transport.PushDatagram(FakeSsdpTransport.MakeDatagram(AliveNotifyWithNt(
            uuid: "uuid-wan",
            location: "http://192.0.2.7/desc.xml",
            nt: "urn:schemas-upnp-org:device:WANDevice:1")));

        await WaitFor(() => ssdpLog.Entries.Count > 0, TimeSpan.FromSeconds(2));

        ssdpLog.Entries.Should().ContainSingle();
        ssdpLog.Entries[0].DeviceUuid.Should().Be("uuid-wan");
        ssdpLog.Entries[0].Kind.Should().Be(SsdpKind.Alive);
        registry.Snapshot().Should().BeEmpty(
            "non-root NT must not register a device even though it is logged");
    }

    [Fact]
    public async Task Byebye_with_non_root_NT_does_not_remove_a_root_with_the_same_UUID()
    {
        // Defensive: only the root's byebye (NT=upnp:rootdevice) should remove
        // a registered device. (In practice each NT byebye carries its own
        // USN-derived UUID, so an embedded byebye won't match a root UUID
        // anyway — but we should not even attempt the removal.)
        var (sut, transport, registry) = Build();
        await using var _holder = sut;
        await sut.StartAsync(CancellationToken.None);

        // Register a real root.
        transport.PushDatagram(FakeSsdpTransport.MakeDatagram(AliveNotify("uuid-root", "http://192.0.2.8/desc.xml")));
        await WaitFor(() => registry.Snapshot().ContainsKey("uuid-root"), TimeSpan.FromSeconds(2));

        // Byebye for the same UUID but a non-root NT — must be a no-op.
        transport.PushDatagram(FakeSsdpTransport.MakeDatagram(ByebyeNotifyWithNt(
            uuid: "uuid-root",
            nt: "urn:schemas-upnp-org:service:Layer3Forwarding:1")));
        await Task.Delay(150);

        registry.Snapshot().Should().ContainKey("uuid-root");
    }

    [Fact]
    public async Task Cancellation_cleanly_halts_the_pump()
    {
        var (sut, transport, _) = Build();
        await sut.StartAsync(CancellationToken.None);

        // DisposeAsync cancels the pump and awaits its termination.
        await sut.DisposeAsync();

        transport.Disposed.Should().BeTrue();
    }

    private static (DiscoveryService sut, FakeSsdpTransport transport, DeviceRegistry registry) Build()
    {
        var transport = new FakeSsdpTransport();
        var registry = new DeviceRegistry();
        var parser = new SsdpMessageParser();
        var clock = new FakeClock();
        var logger = NullLogger<DiscoveryService>.Instance;
        return (new DiscoveryService(transport, registry, parser, clock, logger), transport, registry);
    }

    private static byte[] AliveNotify(string uuid, string location) =>
        AliveNotifyWithNt(uuid, location, "upnp:rootdevice");

    private static byte[] AliveNotifyWithNt(string uuid, string location, string nt) =>
        Encoding.UTF8.GetBytes(
            "NOTIFY * HTTP/1.1\r\n" +
            "HOST: 239.255.255.250:1900\r\n" +
            "CACHE-CONTROL: max-age=1800\r\n" +
            $"LOCATION: {location}\r\n" +
            $"NT: {nt}\r\n" +
            "NTS: ssdp:alive\r\n" +
            $"USN: uuid:{uuid}::{nt}\r\n" +
            "\r\n");

    private static byte[] ByebyeNotify(string uuid) =>
        ByebyeNotifyWithNt(uuid, "upnp:rootdevice");

    private static byte[] ByebyeNotifyWithNt(string uuid, string nt) =>
        Encoding.UTF8.GetBytes(
            "NOTIFY * HTTP/1.1\r\n" +
            "HOST: 239.255.255.250:1900\r\n" +
            $"NT: {nt}\r\n" +
            "NTS: ssdp:byebye\r\n" +
            $"USN: uuid:{uuid}::{nt}\r\n" +
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
}
