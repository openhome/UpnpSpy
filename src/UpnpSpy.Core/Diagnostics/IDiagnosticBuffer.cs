using UpnpSpy.Core.Models;

namespace UpnpSpy.Core.Diagnostics;

/// <summary>
/// In-memory bounded ring buffer of diagnostic entries plus a live subscription API.
/// Both ends share the same backing structure.
/// </summary>
public interface IDiagnosticBuffer : IDiagnosticSink
{
    int Capacity { get; }

    /// <summary>Snapshot of all currently-buffered entries, oldest first.</summary>
    IReadOnlyList<DiagnosticEntry> Snapshot();

    /// <summary>
    /// Subscribes to entries recorded AFTER the call returns. Not retroactive — callers
    /// wanting a primed view must invoke <see cref="Snapshot"/> first.
    /// </summary>
    IDisposable Subscribe(IObserver<DiagnosticEntry> observer);
}
