using Microsoft.UI.Xaml;
using UpnpSpy.App.Platform;
using UpnpSpy.App.Views;
using UpnpSpy.Core.Eventing;
using UpnpSpy.Core.Lifecycle;
using UpnpSpy.Core.ViewModels;

namespace UpnpSpy.App;

public sealed partial class MainWindow : Window
{
    private readonly ShellViewModel _shellVm;
    private readonly AppShutdownTokenSource _shutdownSource;
    private readonly IEventCallbackHost _callbackHost;
    private bool _firstActivation = true;

    public MainWindow(
        ShellViewModel shellVm,
        AppShutdownTokenSource shutdownSource,
        InvocationPopupFactory invocationFactory,
        SubscriptionPopupFactory subscriptionFactory,
        DevicePropertiesPopupFactory propertiesFactory,
        IEventCallbackHost callbackHost,
        Func<DiagnosticsViewerViewModel> diagnosticsVmFactory,
        MainWindowHandleProvider handleProvider)
    {
        _shellVm = shellVm ?? throw new ArgumentNullException(nameof(shellVm));
        _shutdownSource = shutdownSource ?? throw new ArgumentNullException(nameof(shutdownSource));
        _callbackHost = callbackHost ?? throw new ArgumentNullException(nameof(callbackHost));
        ArgumentNullException.ThrowIfNull(invocationFactory);
        ArgumentNullException.ThrowIfNull(subscriptionFactory);
        ArgumentNullException.ThrowIfNull(propertiesFactory);
        ArgumentNullException.ThrowIfNull(diagnosticsVmFactory);
        ArgumentNullException.ThrowIfNull(handleProvider);

        InitializeComponent();
        // FR-046: publish the main window's HWND so secondary windows can be
        // owner-owned. Must run after InitializeComponent so the HWND exists.
        handleProvider.Initialize(WinRT.Interop.WindowNative.GetWindowHandle(this));
        Root.Children.Add(new ShellView(shellVm, invocationFactory, subscriptionFactory, propertiesFactory, diagnosticsVmFactory, handleProvider));

        Activated += OnActivated;
        Closed += OnClosed;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (!_firstActivation || args.WindowActivationState == WindowActivationState.Deactivated)
            return;

        _firstActivation = false;
        // FR-049: callback-host binding now lives inside ShellViewModel.InitializeAsync
        // (it uses the selected adapter's IP, not a wildcard).
        _ = _shellVm.InitializeAsync(_shutdownSource.Token);
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        _shutdownSource.Cancel();
        try { await _callbackHost.DisposeAsync(); } catch { }
        (App.Services as IDisposable)?.Dispose();
    }
}
