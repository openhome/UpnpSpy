using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace UpnpSpy.App.Platform;

/// <summary>
/// FR-046: gives a secondary <see cref="Window"/> an explicit OS-level owner
/// (the main window). WinUI 3 has no managed <c>Window.Owner</c>, so without
/// this an `Activate()`'d popup is an unowned top-level window and the user
/// can send it behind the main window with a routine focus change. Setting
/// <c>GWLP_HWNDPARENT</c> via <c>SetWindowLongPtr</c> establishes the
/// owner-owned relationship: the popup stays z-above its owner, minimises
/// with it, and closes when the owner closes.
/// </summary>
public static class OwnedWindowHelper
{
    private const int GWLP_HWNDPARENT = -8;

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    public static void SetOwner(Window child, IntPtr ownerHwnd)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (ownerHwnd == IntPtr.Zero) return;

        var childHwnd = WinRT.Interop.WindowNative.GetWindowHandle(child);
        if (childHwnd == IntPtr.Zero) return;

        if (IntPtr.Size == 8)
            _ = SetWindowLongPtr64(childHwnd, GWLP_HWNDPARENT, ownerHwnd);
        else
            _ = SetWindowLong32(childHwnd, GWLP_HWNDPARENT, ownerHwnd.ToInt32());
    }
}
