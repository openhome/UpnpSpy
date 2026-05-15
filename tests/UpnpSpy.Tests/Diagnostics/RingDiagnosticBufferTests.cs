using FluentAssertions;
using UpnpSpy.Core.Diagnostics;
using UpnpSpy.Core.Models;
using Xunit;

namespace UpnpSpy.Tests.Diagnostics;

public sealed class RingDiagnosticBufferTests
{
    [Fact]
    public void Snapshot_returns_chronological_entries_up_to_capacity()
    {
        var sut = new RingDiagnosticBuffer(capacity: 3);
        sut.Record(Make("a"));
        sut.Record(Make("b"));
        sut.Record(Make("c"));

        sut.Snapshot().Select(e => e.Message).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Records_past_capacity_evict_oldest_first()
    {
        var sut = new RingDiagnosticBuffer(capacity: 3);
        sut.Record(Make("a"));
        sut.Record(Make("b"));
        sut.Record(Make("c"));
        sut.Record(Make("d"));

        sut.Snapshot().Select(e => e.Message).Should().Equal("b", "c", "d");
    }

    [Fact]
    public void Subscribe_sees_only_entries_after_subscription()
    {
        var sut = new RingDiagnosticBuffer(capacity: 10);
        sut.Record(Make("pre"));

        var seen = new List<string>();
        using (sut.Subscribe(new ListObserver(seen)))
        {
            sut.Record(Make("after"));
        }
        sut.Record(Make("post-unsub"));

        seen.Should().Equal("after");
    }

    [Fact]
    public void Dispose_of_subscription_stops_further_OnNext()
    {
        var sut = new RingDiagnosticBuffer(capacity: 10);
        var seen = new List<string>();
        var sub = sut.Subscribe(new ListObserver(seen));

        sut.Record(Make("a"));
        sub.Dispose();
        sut.Record(Make("b"));

        seen.Should().Equal("a");
    }

    [Fact]
    public void Concurrent_writes_remain_bounded()
    {
        var sut = new RingDiagnosticBuffer(capacity: 1_000);
        Parallel.For(0, 10_000, i => sut.Record(Make($"e{i}")));
        sut.Snapshot().Count.Should().Be(1_000);
    }

    [Fact]
    public void Snapshot_returns_an_immutable_view_of_the_moment_it_was_called()
    {
        var sut = new RingDiagnosticBuffer(capacity: 10);
        sut.Record(Make("a"));
        var snap = sut.Snapshot();
        sut.Record(Make("b"));

        snap.Should().HaveCount(1);
        snap[0].Message.Should().Be("a");
    }

    private static DiagnosticEntry Make(string message) => new(
        Timestamp: DateTimeOffset.UtcNow,
        Severity: DiagnosticSeverity.Information,
        Category: "Test",
        Message: message,
        Context: new Dictionary<string, string>(),
        Exception: null);

    private sealed class ListObserver : IObserver<DiagnosticEntry>
    {
        private readonly List<string> _sink;
        public ListObserver(List<string> sink) { _sink = sink; }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(DiagnosticEntry value) { lock (_sink) _sink.Add(value.Message); }
    }
}
