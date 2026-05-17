using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace UpnpSpy.App.Platform;

/// <summary>
/// Applies the Fluent Mica system backdrop to a window when the host OS / SDK
/// supports it. Safe to call on every secondary window — falls back to the
/// default chrome on older OSes.
/// </summary>
internal static class WindowChrome
{
    public static void TryApplyMica(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!MicaController.IsSupported()) return;

        window.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
    }
}
