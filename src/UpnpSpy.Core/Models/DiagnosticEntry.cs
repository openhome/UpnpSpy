namespace UpnpSpy.Core.Models;

public sealed record DiagnosticEntry(
    DateTimeOffset Timestamp,
    DiagnosticSeverity Severity,
    string Category,
    string Message,
    IReadOnlyDictionary<string, string> Context,
    string? Exception);
