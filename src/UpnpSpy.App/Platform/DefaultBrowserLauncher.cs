using UpnpSpy.Core.Diagnostics;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Platform;
using Windows.System;

namespace UpnpSpy.App.Platform;

/// <summary>
/// Production <see cref="IBrowserLauncher"/> implementation. Delegates to
/// <see cref="Launcher.LaunchUriAsync(Uri)"/> — the documented WinUI 3 path that
/// respects the user's default-browser setting without spawning a transient
/// shell window (contracts/IBrowserLauncher.md, research §13).
/// </summary>
public sealed class DefaultBrowserLauncher : IBrowserLauncher
{
    private readonly IClock _clock;
    private readonly IDiagnosticSink _diagnostics;

    public DefaultBrowserLauncher(IClock clock, IDiagnosticSink diagnostics)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public async Task<bool> OpenAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        try
        {
            return await Launcher.LaunchUriAsync(url).AsTask(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _diagnostics.Record(new DiagnosticEntry(
                Timestamp: _clock.UtcNow,
                Severity: DiagnosticSeverity.Warning,
                Category: "App.Lifecycle",
                Message: "Failed to launch default browser.",
                Context: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["url"] = url.ToString(),
                    ["exception.type"] = ex.GetType().FullName ?? ex.GetType().Name,
                },
                Exception: ex.ToString()));
            return false;
        }
    }
}
