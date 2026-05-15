using FluentAssertions;
using UpnpSpy.Core.Diagnostics;
using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.ViewModels;
using UpnpSpy.Tests.TestHelpers;
using Xunit;

namespace UpnpSpy.Tests.ViewModels;

public sealed class DiagnosticsViewerViewModelTests
{
    [Fact]
    public void Start_populates_entries_from_snapshot_in_chronological_order()
    {
        var buffer = new RingDiagnosticBuffer(capacity: 10);
        buffer.Record(Make("a"));
        buffer.Record(Make("b"));
        buffer.Record(Make("c"));

        var sut = BuildSut(buffer);
        sut.Start();

        sut.Entries.Select(e => e.Message).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Live_records_after_start_flow_into_entries()
    {
        var buffer = new RingDiagnosticBuffer(capacity: 10);
        var sut = BuildSut(buffer);
        sut.Start();

        buffer.Record(Make("after"));

        sut.Entries.Should().HaveCount(1);
        sut.Entries[0].Message.Should().Be("after");
    }

    [Fact]
    public void Snapshot_plus_live_records_appear_in_order()
    {
        var buffer = new RingDiagnosticBuffer(capacity: 10);
        buffer.Record(Make("snap"));

        var sut = BuildSut(buffer);
        sut.Start();
        buffer.Record(Make("live"));

        sut.Entries.Select(e => e.Message).Should().Equal("snap", "live");
    }

    [Fact]
    public void Dispose_unsubscribes_no_further_updates()
    {
        var buffer = new RingDiagnosticBuffer(capacity: 10);
        var sut = BuildSut(buffer);
        sut.Start();
        buffer.Record(Make("before"));

        sut.Dispose();
        buffer.Record(Make("after"));

        sut.Entries.Should().HaveCount(1);
        sut.Entries[0].Message.Should().Be("before");
    }

    [Fact]
    public void Record_made_immediately_before_start_is_visible()
    {
        // SC-014: a Record made immediately before opening the viewer
        // must show up in the priming Snapshot.
        var buffer = new RingDiagnosticBuffer(capacity: 10);
        buffer.Record(Make("just-before"));

        var sut = BuildSut(buffer);
        sut.Start();

        sut.Entries.Should().ContainSingle().Which.Message.Should().Be("just-before");
    }

    [Fact]
    public void Start_twice_throws()
    {
        var buffer = new RingDiagnosticBuffer(capacity: 10);
        var sut = BuildSut(buffer);
        sut.Start();

        var act = () => sut.Start();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Null_args_throw()
    {
        var buffer = new RingDiagnosticBuffer(capacity: 10);
        var registry = new DeviceRegistry();
        var dispatcher = new SynchronousDispatcher();

        ((Action)(() => new DiagnosticsViewerViewModel(null!, registry, dispatcher)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => new DiagnosticsViewerViewModel(buffer, null!, dispatcher)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => new DiagnosticsViewerViewModel(buffer, registry, null!)))
            .Should().Throw<ArgumentNullException>();
    }

    // ---------------------------------------------------------------
    // FR-041: Identity / Endpoint column resolution
    // ---------------------------------------------------------------

    [Fact]
    public void Identity_resolves_to_FriendlyName_when_device_in_registry_has_one()
    {
        var registry = new DeviceRegistry();
        registry.TryAddOrUpdate(new Device
        {
            Uuid = "uuid-1",
            LocationUrl = new Uri("http://192.0.2.10/desc.xml"),
            FriendlyName = "Living Room Speaker",
        });
        var buffer = new RingDiagnosticBuffer(capacity: 10);
        buffer.Record(Make("boom", context: ("device.uuid", "uuid-1")));

        var sut = BuildSut(buffer, registry);
        sut.Start();

        sut.Entries.Should().ContainSingle()
            .Which.Identity.Should().Be("Living Room Speaker");
    }

    [Fact]
    public void Identity_falls_back_to_uuid_prefix_when_device_known_but_FriendlyName_missing()
    {
        var registry = new DeviceRegistry();
        registry.TryAddOrUpdate(new Device
        {
            Uuid = "uuid-2",
            LocationUrl = new Uri("http://192.0.2.11/desc.xml"),
            // FriendlyName left null — description hasn't been fetched yet
        });
        var buffer = new RingDiagnosticBuffer(capacity: 10);
        buffer.Record(Make("oops", context: ("device.uuid", "uuid-2")));

        var sut = BuildSut(buffer, registry);
        sut.Start();

        sut.Entries[0].Identity.Should().Be("uuid:uuid-2");
    }

    [Fact]
    public void Identity_falls_back_to_uuid_prefix_when_device_no_longer_in_registry()
    {
        // Snapshot-at-arrival: the device may have been removed before the
        // user opens the viewer; we still want a recognisable label.
        var registry = new DeviceRegistry();
        var buffer = new RingDiagnosticBuffer(capacity: 10);
        buffer.Record(Make("gone", context: ("device.uuid", "uuid-gone")));

        var sut = BuildSut(buffer, registry);
        sut.Start();

        sut.Entries[0].Identity.Should().Be("uuid:uuid-gone");
    }

    [Fact]
    public void Identity_is_placeholder_when_entry_has_no_device_uuid_context()
    {
        var buffer = new RingDiagnosticBuffer(capacity: 10);
        buffer.Record(Make("app-start"));

        var sut = BuildSut(buffer);
        sut.Start();

        sut.Entries[0].Identity.Should().Be(DiagnosticsViewerViewModel.Placeholder);
    }

    [Fact]
    public void Endpoint_extracts_host_and_port_from_url_when_port_is_non_default()
    {
        var buffer = new RingDiagnosticBuffer(capacity: 10);
        buffer.Record(Make("err", context: ("url", "http://192.0.2.10:49152/desc.xml")));

        var sut = BuildSut(buffer);
        sut.Start();

        sut.Entries[0].Endpoint.Should().Be("192.0.2.10:49152");
    }

    [Fact]
    public void Endpoint_elides_default_port()
    {
        var buffer = new RingDiagnosticBuffer(capacity: 10);
        buffer.Record(Make("err", context: ("url", "http://192.0.2.10/desc.xml")));

        var sut = BuildSut(buffer);
        sut.Start();

        sut.Entries[0].Endpoint.Should().Be("192.0.2.10");
    }

    [Fact]
    public void Endpoint_falls_back_to_remote_endpoint_when_url_is_absent()
    {
        // Ssdp.Parse warnings carry remote.endpoint but no url.
        var buffer = new RingDiagnosticBuffer(capacity: 10);
        buffer.Record(Make("bad-ssdp", context: ("remote.endpoint", "192.0.2.55:1900")));

        var sut = BuildSut(buffer);
        sut.Start();

        sut.Entries[0].Endpoint.Should().Be("192.0.2.55:1900");
    }

    [Fact]
    public void Endpoint_is_placeholder_when_neither_url_nor_remote_endpoint_is_present()
    {
        var buffer = new RingDiagnosticBuffer(capacity: 10);
        buffer.Record(Make("lifecycle"));

        var sut = BuildSut(buffer);
        sut.Start();

        sut.Entries[0].Endpoint.Should().Be(DiagnosticsViewerViewModel.Placeholder);
    }

    [Fact]
    public void Endpoint_falls_back_to_raw_url_string_when_url_is_unparseable()
    {
        var buffer = new RingDiagnosticBuffer(capacity: 10);
        buffer.Record(Make("err", context: ("url", "not a url")));

        var sut = BuildSut(buffer);
        sut.Start();

        sut.Entries[0].Endpoint.Should().Be("not a url");
    }

    private static DiagnosticsViewerViewModel BuildSut(
        RingDiagnosticBuffer buffer, DeviceRegistry? registry = null) =>
        new(buffer, registry ?? new DeviceRegistry(), new SynchronousDispatcher());

    private static DiagnosticEntry Make(string message, params (string Key, string Value)[] context) => new(
        Timestamp: DateTimeOffset.UtcNow,
        Severity: DiagnosticSeverity.Information,
        Category: "Test",
        Message: message,
        Context: context.ToDictionary(p => p.Key, p => p.Value),
        Exception: null);
}
