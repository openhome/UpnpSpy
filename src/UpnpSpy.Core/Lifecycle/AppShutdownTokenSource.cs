namespace UpnpSpy.Core.Lifecycle;

/// <summary>
/// Owns the app-wide shutdown <see cref="CancellationTokenSource"/>. Long-running
/// background tasks (SSDP receive pump, subscription renewal, etc.) link their own
/// cancellation tokens to <see cref="Token"/> so MainWindow's OnClosed handler can
/// cancel everything at once (research §16).
/// </summary>
public sealed class AppShutdownTokenSource : IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    public CancellationToken Token => _cts.Token;

    public void Cancel()
    {
        try { _cts.Cancel(); }
        catch (ObjectDisposedException) { /* idempotent on shutdown */ }
    }

    public void Dispose() => _cts.Dispose();
}
