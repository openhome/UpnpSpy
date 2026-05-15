namespace UpnpSpy.App.Platform;

/// <summary>
/// FR-046: DI-managed singleton that publishes the main window's HWND so
/// secondary windows (invocation popup, subscription popup, Diagnostics
/// viewer) can be made owner-owned via <see cref="OwnedWindowHelper"/>. The
/// handle is set exactly once, during <see cref="MainWindow"/>'s
/// construction, before any secondary window can be opened by the user.
/// </summary>
public sealed class MainWindowHandleProvider
{
    public IntPtr Handle { get; private set; }

    public void Initialize(IntPtr handle)
    {
        if (Handle != IntPtr.Zero)
            throw new InvalidOperationException("MainWindowHandleProvider already initialized.");
        Handle = handle;
    }
}
