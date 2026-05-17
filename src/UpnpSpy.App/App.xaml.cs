using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using UpnpSpy.App.Platform;
using UpnpSpy.Core.Composition;
using UpnpSpy.Core.Diagnostics;
using UpnpSpy.Core.Platform;
using UpnpSpy.Core.ViewModels;

namespace UpnpSpy.App;

public partial class App : Application
{
    private Window? _window;

    public static IServiceProvider Services { get; private set; } = default!;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = BuildServiceProvider();
        _window = Services.GetRequiredService<MainWindow>();
        _window.Activate();
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();

        var services = new ServiceCollection();
        services.AddUpnpSpyCore(configuration);

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IFileSystem, SystemFileSystem>();
        services.AddSingleton<INetworkInterfaceEnumerator, NetworkInterfaceEnumerator>();
        services.AddSingleton<IBrowserLauncher, DefaultBrowserLauncher>();
        services.AddSingleton<IDispatcher>(_ => new WinUiDispatcher(DispatcherQueue.GetForCurrentThread()));

        // FR-049: IEventCallbackHost is registered by AddUpnpSpyCore now
        // (TcpListener-based, BCL-only — no URL ACL required).

        services.AddLogging();

        // Diagnostics window opens a fresh VM per click; register a delegate
        // factory because MS.Extensions.DependencyInjection does not provide
        // Func<T> resolution out of the box.
        services.AddSingleton<Func<DiagnosticsViewerViewModel>>(sp =>
            () => sp.GetRequiredService<DiagnosticsViewerViewModel>());

        services.AddSingleton<MainWindowHandleProvider>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
