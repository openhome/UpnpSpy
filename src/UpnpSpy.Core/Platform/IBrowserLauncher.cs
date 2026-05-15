namespace UpnpSpy.Core.Platform;

/// <summary>
/// One-method abstraction over "open this URL in the user's default browser".
/// Exists so the right-click → Fetch XML / Fetch service XML flow can be
/// unit-tested without launching a real browser (contracts/IBrowserLauncher.md).
/// </summary>
public interface IBrowserLauncher
{
    /// <summary>
    /// Opens <paramref name="url"/> in the user's default browser. Returns
    /// <c>true</c> if the system reports the URI was launched, <c>false</c> if
    /// it could not be (no association, COM failure, etc.). Does not throw for
    /// ordinary "no browser" failure.
    /// </summary>
    Task<bool> OpenAsync(Uri url, CancellationToken cancellationToken);
}
