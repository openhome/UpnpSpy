using UpnpSpy.Core.Models;

namespace UpnpSpy.Core.Diagnostics;

public sealed class CompositeDiagnosticSink : IDiagnosticSink
{
    private readonly IDiagnosticSink[] _inner;

    public CompositeDiagnosticSink(params IDiagnosticSink[] inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public void Record(DiagnosticEntry entry)
    {
        foreach (var sink in _inner)
        {
            try { sink.Record(entry); }
            catch { /* fan-out is fail-open per FR-042 */ }
        }
    }
}
