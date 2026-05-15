using Microsoft.Extensions.Logging;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.Diagnostics;

/// <summary>
/// Bridges <see cref="ILogger"/> calls into <see cref="IDiagnosticSink"/> so domain
/// code uses ordinary <c>_logger.LogWarning(...)</c> while every entry lands in the
/// rolling file and in-memory ring.
/// </summary>
public sealed class DiagnosticLoggerProvider : ILoggerProvider
{
    private readonly IDiagnosticSink _sink;
    private readonly IClock _clock;

    public DiagnosticLoggerProvider(IDiagnosticSink sink, IClock clock)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public ILogger CreateLogger(string categoryName) => new DiagnosticLogger(_sink, _clock, categoryName);

    public void Dispose() { }

    private sealed class DiagnosticLogger : ILogger
    {
        private readonly IDiagnosticSink _sink;
        private readonly IClock _clock;
        private readonly string _category;

        public DiagnosticLogger(IDiagnosticSink sink, IClock clock, string category)
        {
            _sink = sink;
            _clock = clock;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            ArgumentNullException.ThrowIfNull(formatter);

            var severity = MapSeverity(logLevel);
            var message = formatter(state, exception);
            var context = ExtractContext(state);

            _sink.Record(new DiagnosticEntry(
                Timestamp: _clock.UtcNow,
                Severity: severity,
                Category: _category,
                Message: message,
                Context: context,
                Exception: exception?.ToString()));
        }

        private static DiagnosticSeverity MapSeverity(LogLevel level) => level switch
        {
            LogLevel.Trace => DiagnosticSeverity.Trace,
            LogLevel.Debug => DiagnosticSeverity.Trace,
            LogLevel.Information => DiagnosticSeverity.Information,
            LogLevel.Warning => DiagnosticSeverity.Warning,
            LogLevel.Error or LogLevel.Critical => DiagnosticSeverity.Error,
            _ => DiagnosticSeverity.Information,
        };

        private static IReadOnlyDictionary<string, string> ExtractContext<TState>(TState state)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> kvps)
            {
                var dict = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var kvp in kvps)
                {
                    if (kvp.Key == "{OriginalFormat}") continue;
                    dict[kvp.Key] = kvp.Value?.ToString() ?? string.Empty;
                }
                return dict;
            }
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}
