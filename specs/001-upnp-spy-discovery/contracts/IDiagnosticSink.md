# Contract: `IDiagnosticSink` and `IDiagnosticBuffer`

**Namespace**: `UpnpSpy.Core.Diagnostics`
**Lifetime**: Singleton (both)
**Spec FR**: FR-039, FR-040, FR-041, FR-042

Two cooperating contracts that together satisfy the dual-destination diagnostic logging requirement: every diagnostic event goes both to a bounded rolling file on disk **and** into a bounded in-memory ring backing the `View > Diagnostics` viewer.

## C# signatures

```csharp
public interface IDiagnosticSink
{
    // Records one entry. Never blocks the caller: entries are queued and drained on
    // a dedicated background task. Implementations of IDiagnosticSink may be chained
    // (rolling file + ring buffer are both implementations).
    void Record(DiagnosticEntry entry);
}

public interface IDiagnosticBuffer : IDiagnosticSink
{
    // Snapshot of all currently-buffered entries, oldest first.
    IReadOnlyList<DiagnosticEntry> Snapshot();

    // Live subscription: observer.OnNext is invoked on the buffer's dispatcher
    // for every entry recorded after Subscribe is called. The IDisposable
    // detaches the observer; the observer never sees the snapshot — callers
    // wanting a primed view should call Snapshot() before Subscribe().
    IDisposable Subscribe(IObserver<DiagnosticEntry> observer);

    // Cap on the ring buffer (default: 5000).
    int Capacity { get; }
}
```

`DiagnosticEntry` is the record defined in data-model §12.

## Behavioural requirements

### `IDiagnosticSink` (general)

- `Record` is **non-blocking**: it enqueues into an in-memory channel and returns. The drain task processes one entry at a time. Even if a disk write takes seconds, the caller is unaffected (FR-042).
- An `ILoggerProvider` (`DiagnosticLoggerProvider`) bridges `Microsoft.Extensions.Logging.ILogger` calls into `Record(DiagnosticEntry)` so domain code can use ordinary `_logger.LogWarning(...)` and the constitutional logging surface (Principle III) stays unified.

### `RollingFileDiagnosticSink`

- Writes one JSON line per entry to `%LOCALAPPDATA%\UpnpSpy\logs\upnpspy.log`.
- When the current file exceeds 2 MB, rotates: `upnpspy.log` → `upnpspy.1.log`, `upnpspy.1.log` → `upnpspy.2.log`, …, `upnpspy.7.log` is overwritten (max 8 files, ≤16 MB total).
- **Fail-open** (FR-042): if `%LOCALAPPDATA%\UpnpSpy\logs\` cannot be created or the file cannot be opened, the sink swallows the error (Trace-level internal entry to debugger output only), and the app raises a single user-visible toast on startup. The in-memory buffer keeps working.

### `RingDiagnosticBuffer` (the singleton implementation of `IDiagnosticBuffer`)

- Fixed-capacity ring of 5,000 entries; oldest is evicted when a 5,001st arrives (FR-041).
- Thread-safe.
- `Subscribe` is **not retroactive** — it sees only entries added after the subscription. The `DiagnosticsViewerViewModel` calls `Snapshot()` for its initial state and `Subscribe()` for live updates; the two are merged into the viewer's `BoundedObservableCollection<DiagnosticEntry>` (FR-041 "remain responsive while new entries arrive").

## DI composition

```text
services.AddSingleton<RingDiagnosticBuffer>();
services.AddSingleton<IDiagnosticBuffer>(sp => sp.GetRequiredService<RingDiagnosticBuffer>());
services.AddSingleton<RollingFileDiagnosticSink>();

// IDiagnosticSink resolves to a composite that fans out to both
services.AddSingleton<IDiagnosticSink>(sp => new CompositeDiagnosticSink(
    sp.GetRequiredService<RingDiagnosticBuffer>(),
    sp.GetRequiredService<RollingFileDiagnosticSink>()));

services.AddSingleton<ILoggerProvider, DiagnosticLoggerProvider>();
```

## Test seam

- `RingDiagnosticBuffer` is itself trivially usable in tests as both sink and buffer.
- `RollingFileDiagnosticSink` accepts an injected `IFileSystem` abstraction (small, in-repo) so rotation logic can be tested against an in-memory file system.

## Citations

- Spec FR-039 (what to record), FR-040 (rolling file), FR-041 (in-memory ring + viewer), FR-042 (non-blocking, fail-open, no sensitive data).
