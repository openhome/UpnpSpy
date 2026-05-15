using UpnpSpy.Core.Diagnostics;
using UpnpSpy.Core.Models;

namespace UpnpSpy.Tests.TestHelpers;

/// <summary>Captures every diagnostic entry for assertion.</summary>
internal sealed class RecordingDiagnosticSink : IDiagnosticSink
{
    public List<DiagnosticEntry> Entries { get; } = new();
    public void Record(DiagnosticEntry entry) => Entries.Add(entry);
}
