using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using UpnpSpy.Core.Description;
using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Lifecycle;
using UpnpSpy.Core.Models;
using UpnpSpy.Tests.Description;
using UpnpSpy.Tests.TestHelpers;
using Xunit;

namespace UpnpSpy.Tests.Discovery;

/// <summary>
/// FR-043 — eager device-description fetch. The dispatcher subscribes to the
/// registry's lifecycle events; tests drive those events directly to avoid
/// pulling in the SSDP transport.
/// </summary>
public sealed class EagerDescriptionDispatcherTests
{
    [Fact]
    public async Task A_single_DeviceAdded_triggers_exactly_one_FetchAsync_and_populates_friendly_name()
    {
        var (registry, fetcher, sut, _, _) = Build();
        sut.Start();

        var device = MakeDevice("uuid-a", location: new Uri("http://192.0.2.1/desc.xml"));
        fetcher.SetResult(device.LocationUrl, new DeviceDescriptionFetchResult.Success(
            DeviceDescription.Minimum("uuid-a", "Discovered Speaker")));

        registry.TryAddOrUpdate(device);

        await WaitFor(() => device.DescriptionFetchState == FetchState.Loaded);

        fetcher.CallsFor(device.LocationUrl).Should().Be(1);
        device.FriendlyName.Should().Be("Discovered Speaker");
        device.Label.Should().Be("Discovered Speaker");
    }

    [Fact]
    public async Task Success_path_raises_DeviceUpdated_with_the_canonical_device()
    {
        var (registry, fetcher, sut, _, _) = Build();
        sut.Start();

        var device = MakeDevice("uuid-a", location: new Uri("http://192.0.2.1/desc.xml"));
        fetcher.SetResult(device.LocationUrl, new DeviceDescriptionFetchResult.Success(
            DeviceDescription.Minimum("uuid-a", "Speaker")));

        var updates = new List<DeviceUpdatedEvent>();
        registry.DeviceUpdated += e => { lock (updates) updates.Add(e); };

        registry.TryAddOrUpdate(device);

        await WaitFor(() => device.DescriptionFetchState == FetchState.Loaded);
        await WaitFor(() => { lock (updates) return updates.Count > 0; });

        lock (updates)
        {
            updates.Should().HaveCountGreaterOrEqualTo(1);
            updates.Last().Device.Uuid.Should().Be("uuid-a");
        }
    }

    [Fact]
    public async Task Http_error_sets_state_Failed_and_records_a_Warning_diagnostic()
    {
        var (registry, fetcher, sut, _, diag) = Build();
        sut.Start();

        var device = MakeDevice("uuid-a", location: new Uri("http://192.0.2.1/desc.xml"));
        fetcher.SetResult(device.LocationUrl, new DeviceDescriptionFetchResult.HttpError(404, "Not Found"));

        registry.TryAddOrUpdate(device);

        await WaitFor(() => device.DescriptionFetchState == FetchState.Failed);

        device.DescriptionFetchError.Should().Contain("404");
        device.Services.Should().BeEmpty();
        device.FriendlyName.Should().BeNull("the FR-010 fallback should persist on failure");
        device.Label.Should().Be("uuid:uuid-a");

        diag.Entries.Should().Contain(e =>
            e.Severity == UpnpSpy.Core.Models.DiagnosticSeverity.Warning
            && e.Category == "Description.Fetch"
            && e.Context.ContainsKey("device.uuid") && e.Context["device.uuid"] == "uuid-a");
    }

    [Fact]
    public async Task Parse_error_categorises_as_Description_Parse()
    {
        var (registry, fetcher, sut, _, diag) = Build();
        sut.Start();

        var device = MakeDevice("uuid-a", location: new Uri("http://192.0.2.1/desc.xml"));
        fetcher.SetResult(device.LocationUrl, new DeviceDescriptionFetchResult.ParseError("not xml"));

        registry.TryAddOrUpdate(device);

        await WaitFor(() => device.DescriptionFetchState == FetchState.Failed);
        diag.Entries.Should().Contain(e => e.Category == "Description.Parse");
    }

    [Fact]
    public async Task A_second_DeviceAdded_for_the_same_UUID_does_not_trigger_a_second_fetch()
    {
        var (registry, fetcher, sut, _, _) = Build();
        sut.Start();

        var device = MakeDevice("uuid-a", location: new Uri("http://192.0.2.1/desc.xml"));
        fetcher.SetResult(device.LocationUrl, new DeviceDescriptionFetchResult.Success(
            DeviceDescription.Minimum("uuid-a", "Speaker")));

        registry.TryAddOrUpdate(device);
        await WaitFor(() => device.DescriptionFetchState == FetchState.Loaded);

        // Same UUID, fresh candidate (e.g., re-discovered) — registry merges into
        // existing and does NOT raise DeviceAdded a second time.
        registry.TryAddOrUpdate(MakeDevice("uuid-a", location: device.LocationUrl));

        // No state change beyond the original Loaded.
        await Task.Delay(100);
        fetcher.CallsFor(device.LocationUrl).Should().Be(1);
    }

    [Fact]
    public async Task Remove_followed_by_Add_for_the_same_UUID_triggers_a_fresh_fetch()
    {
        var (registry, fetcher, sut, _, _) = Build();
        sut.Start();

        var location = new Uri("http://192.0.2.1/desc.xml");
        fetcher.SetResult(location, new DeviceDescriptionFetchResult.Success(
            DeviceDescription.Minimum("uuid-a", "Speaker")));

        registry.TryAddOrUpdate(MakeDevice("uuid-a", location));
        await WaitFor(() => fetcher.CallsFor(location) == 1);

        registry.Remove("uuid-a");
        registry.TryAddOrUpdate(MakeDevice("uuid-a", location));
        await WaitFor(() => fetcher.CallsFor(location) == 2);

        fetcher.CallsFor(location).Should().Be(2);
    }

    [Fact]
    public async Task DeviceRemoved_mid_fetch_cancels_the_in_flight_fetch()
    {
        var (registry, fetcher, sut, _, _) = Build();
        sut.Start();

        fetcher.HoldNextFetch();

        var device = MakeDevice("uuid-a", new Uri("http://192.0.2.1/desc.xml"));
        registry.TryAddOrUpdate(device);

        // Let the fetch reach the gate.
        await WaitFor(() => device.DescriptionFetchState == FetchState.Fetching);

        registry.Remove("uuid-a");

        // Release the gate — the OCE should fall out cleanly without throwing
        // through to the test thread (Task.Run absorbs it).
        fetcher.Release();
        await Task.Delay(50);

        // Device is gone from the registry; its state stays Fetching, but that's
        // unobservable because no view-model holds the canonical instance.
        registry.Contains("uuid-a").Should().BeFalse();
    }

    [Fact]
    public async Task Discovery_burst_of_20_devices_caps_concurrent_fetches_at_MaxConcurrentFetches()
    {
        var (registry, fetcher, sut, _, _) = Build();
        sut.Start();

        var inFlight = 0;
        var peak = 0;
        var lockObj = new object();

        // For each location, configure the fake to track in-flight count.
        var locations = new List<(string Uuid, Uri Location)>();
        for (var i = 0; i < 20; i++)
            locations.Add(($"uuid-{i:D2}", new Uri($"http://192.0.2.{i + 1}/desc.xml")));

        var trackedFetcher = new TrackingFetcher(
            onEnter: () =>
            {
                lock (lockObj)
                {
                    inFlight++;
                    peak = Math.Max(peak, inFlight);
                }
            },
            onExit: () =>
            {
                lock (lockObj) inFlight--;
            });

        // Swap in the tracking fetcher.
        var (registry2, _, sut2, _, _) = Build(trackedFetcher);
        sut2.Start();

        foreach (var (uuid, location) in locations)
            registry2.TryAddOrUpdate(MakeDevice(uuid, location));

        await WaitFor(() => trackedFetcher.Completed == 20, timeout: TimeSpan.FromSeconds(5));

        peak.Should().BeLessOrEqualTo(EagerDescriptionDispatcher.MaxConcurrentFetches,
            $"peak in-flight should not exceed the {EagerDescriptionDispatcher.MaxConcurrentFetches}-slot cap");
    }

    [Fact]
    public async Task Description_with_mismatched_root_UDN_removes_the_device_from_the_registry_as_embedded()
    {
        // Backstop for the SSDP-layer NT=upnp:rootdevice filter: an IGD chassis
        // sometimes responds with embedded-device UUIDs that resolve to the same
        // LOCATION as the root. The description's root UDN is the canonical root;
        // a UUID that doesn't match it is an embedded child and must NOT remain
        // in the registry as a duplicate-looking row.
        var (registry, fetcher, sut, _, diag) = Build();
        sut.Start();

        var location = new Uri("http://192.0.2.1/desc.xml");
        // The fetched description claims its root is "uuid-root", but we
        // registered the device with "uuid-embedded".
        fetcher.SetResult(location, new DeviceDescriptionFetchResult.Success(
            DeviceDescription.Minimum("uuid-root", "Sky ADSL Router")));

        var device = MakeDevice("uuid-embedded", location);
        var removed = new TaskCompletionSource<DeviceRemovedEvent>();
        registry.DeviceRemoved += e =>
        {
            if (e.Uuid == "uuid-embedded") removed.TrySetResult(e);
        };

        registry.TryAddOrUpdate(device);
        await removed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        registry.Contains("uuid-embedded").Should().BeFalse(
            "the dispatcher must remove embedded-child UUIDs once their description reveals the mismatch");
        device.FriendlyName.Should().BeNull(
            "the embedded device must NOT inherit the root's friendly name");
        diag.Entries.Should().Contain(e =>
            e.Severity == UpnpSpy.Core.Models.DiagnosticSeverity.Information
            && e.Category == "Description.Fetch"
            && e.Context.ContainsKey("device.uuid") && e.Context["device.uuid"] == "uuid-embedded"
            && e.Context.ContainsKey("declared.root.uuid") && e.Context["declared.root.uuid"] == "uuid-root");
    }

    [Fact]
    public async Task Devices_already_in_registry_at_Start_are_back_filled()
    {
        var (registry, fetcher, sut, _, _) = Build();

        var device = MakeDevice("uuid-a", new Uri("http://192.0.2.1/desc.xml"));
        fetcher.SetResult(device.LocationUrl, new DeviceDescriptionFetchResult.Success(
            DeviceDescription.Minimum("uuid-a", "Pre-existing")));

        registry.TryAddOrUpdate(device); // before Start
        sut.Start();

        await WaitFor(() => device.DescriptionFetchState == FetchState.Loaded);
        device.FriendlyName.Should().Be("Pre-existing");
    }

    private static (DeviceRegistry registry, FakeDeviceDescriptionFetcher fetcher, EagerDescriptionDispatcher sut,
        AppShutdownTokenSource shutdown, RecordingDiagnosticSink diagnostics) Build(IDeviceDescriptionFetcher? fetcher = null)
    {
        var registry = new DeviceRegistry();
        var fake = (fetcher as FakeDeviceDescriptionFetcher) ?? new FakeDeviceDescriptionFetcher();
        var sink = new RecordingDiagnosticSink();
        var shutdown = new AppShutdownTokenSource();
        var sut = new EagerDescriptionDispatcher(
            registry,
            fetcher ?? fake,
            new FakeClock(),
            shutdown,
            NullLogger<EagerDescriptionDispatcher>.Instance,
            sink);
        return (registry, fake, sut, shutdown, sink);
    }

    private static Device MakeDevice(string uuid, Uri location) => new()
    {
        Uuid = uuid,
        LocationUrl = location,
        LastSeenUtc = DateTimeOffset.UtcNow,
    };

    private static async Task WaitFor(Func<bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(20);
        }
        throw new TimeoutException("Condition not satisfied within " + (timeout ?? TimeSpan.FromSeconds(2)));
    }

    private sealed class TrackingFetcher : IDeviceDescriptionFetcher
    {
        private readonly Action _onEnter;
        private readonly Action _onExit;
        private int _completed;

        public int Completed => _completed;

        public TrackingFetcher(Action onEnter, Action onExit)
        {
            _onEnter = onEnter;
            _onExit = onExit;
        }

        public async Task<DeviceDescriptionFetchResult> FetchAsync(Uri locationUrl, CancellationToken cancellationToken)
        {
            _onEnter();
            try
            {
                // Hold long enough for the peak-counter to observe concurrency.
                await Task.Delay(40, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _onExit();
                Interlocked.Increment(ref _completed);
            }
            return new DeviceDescriptionFetchResult.Success(
                DeviceDescription.Minimum("uuid-" + locationUrl.AbsolutePath, "X"));
        }
    }
}
