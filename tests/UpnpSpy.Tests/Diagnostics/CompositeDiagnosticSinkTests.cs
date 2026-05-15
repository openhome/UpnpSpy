using FluentAssertions;
using UpnpSpy.Core.Diagnostics;
using UpnpSpy.Core.Models;
using Xunit;

namespace UpnpSpy.Tests.Diagnostics;

public sealed class CompositeDiagnosticSinkTests
{
    [Fact]
    public void Records_to_every_inner_sink()
    {
        var a = new RecordingSink();
        var b = new RecordingSink();
        var sut = new CompositeDiagnosticSink(a, b);

        sut.Record(Entry("e1"));
        sut.Record(Entry("e2"));

        a.Recorded.Should().HaveCount(2);
        b.Recorded.Should().HaveCount(2);
    }

    [Fact]
    public void Failure_in_one_sink_does_not_prevent_the_others_from_receiving()
    {
        var throwing = new ThrowingSink();
        var working = new RecordingSink();
        var sut = new CompositeDiagnosticSink(throwing, working);

        sut.Record(Entry("e1"));

        working.Recorded.Should().HaveCount(1);
    }

    private static DiagnosticEntry Entry(string message) => new(
        DateTimeOffset.UtcNow, DiagnosticSeverity.Information, "Test", message,
        new Dictionary<string, string>(), null);

    private sealed class RecordingSink : IDiagnosticSink
    {
        public List<DiagnosticEntry> Recorded { get; } = new();
        public void Record(DiagnosticEntry entry) => Recorded.Add(entry);
    }

    private sealed class ThrowingSink : IDiagnosticSink
    {
        public void Record(DiagnosticEntry entry) => throw new InvalidOperationException("boom");
    }
}
