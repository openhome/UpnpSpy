using UpnpSpy.Core.Models;

namespace UpnpSpy.Core.Diagnostics;

/// <summary>
/// Records a diagnostic entry. Implementations MUST NOT block the caller — entries
/// are queued and drained on the implementation's own schedule.
/// </summary>
public interface IDiagnosticSink
{
    void Record(DiagnosticEntry entry);
}
