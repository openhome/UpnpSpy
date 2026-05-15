using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UpnpSpy.Core.Control;
using UpnpSpy.Core.Description;
using UpnpSpy.Core.Diagnostics;
using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Eventing;
using UpnpSpy.Core.Lifecycle;
using UpnpSpy.Core.Net;
using UpnpSpy.Core.Platform;
using UpnpSpy.Core.Ssdp;
using UpnpSpy.Core.ViewModels;

namespace UpnpSpy.Core.Composition;

public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the platform-agnostic UpnpSpy services. The App-side composition
    /// root must additionally register the Windows adapters
    /// (<c>IClock</c>, <c>IDispatcher</c>, <c>INetworkInterfaceEnumerator</c>, <c>IFileSystem</c>)
    /// and call <c>services.AddLogging(...)</c> so the ILogger factory is present.
    /// </summary>
    /// <param name="services">DI collection.</param>
    /// <param name="configuration">
    /// Provides <c>Diagnostics:LogDirectory</c> if set; falls back to
    /// <c>%LOCALAPPDATA%\UpnpSpy\logs</c>.
    /// </param>
    public static IServiceCollection AddUpnpSpyCore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<AppShutdownTokenSource>();
        services.AddSingleton<HttpClientFactory>();

        services.AddSingleton<RingDiagnosticBuffer>(_ => new RingDiagnosticBuffer(capacity: 5_000));
        services.AddSingleton<IDiagnosticBuffer>(sp => sp.GetRequiredService<RingDiagnosticBuffer>());

        var logDirectory = configuration["Diagnostics:LogDirectory"] ?? DefaultLogDirectory();
        services.AddSingleton(sp => new RollingFileDiagnosticSink(
            sp.GetRequiredService<IFileSystem>(),
            logDirectory));

        services.AddSingleton<IDiagnosticSink>(sp => new CompositeDiagnosticSink(
            sp.GetRequiredService<RingDiagnosticBuffer>(),
            sp.GetRequiredService<RollingFileDiagnosticSink>()));

        services.AddSingleton<ILoggerProvider, DiagnosticLoggerProvider>();

        // Discovery spine (Phase 3 US1)
        services.AddSingleton<SsdpMessageParser>();
        services.AddSingleton<DeviceRegistry>();
        services.AddSingleton<MulticastSsdpTransport>();
        services.AddSingleton<ISsdpTransport>(sp => sp.GetRequiredService<MulticastSsdpTransport>());
        services.AddSingleton(sp => new DiscoveryService(
            sp.GetRequiredService<ISsdpTransport>(),
            sp.GetRequiredService<DeviceRegistry>(),
            sp.GetRequiredService<SsdpMessageParser>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ILogger<DiscoveryService>>(),
            sp.GetRequiredService<IDiagnosticSink>(),
            sp.GetRequiredService<SsdpLogViewModel>()));

        services.AddSingleton<RescanCoordinator>();

        // Description / SCPD fetchers (Phase 5 US3)
        services.AddSingleton<DeviceDescriptionXmlParser>();
        services.AddSingleton<ScpdXmlParser>();
        services.AddSingleton<IDeviceDescriptionFetcher, DeviceDescriptionFetcher>();
        services.AddSingleton<IScpdFetcher, ScpdFetcher>();

        // Eager device-description fetch (Phase 12, FR-043) — listens to the
        // registry and resolves friendly-name labels without user interaction.
        services.AddSingleton<EagerDescriptionDispatcher>();

        // View-model factories (Phase 5 US3)
        services.AddSingleton<ServiceNodeFactory>();
        services.AddSingleton<DeviceNodeFactory>();

        // Control client + invocation popup factory (Phase 9 US7)
        services.AddSingleton<IControlClient, ControlClient>();
        services.AddSingleton<InvocationPopupFactory>();

        // Eventing (Phase 10 US8, Phase 16 FR-049). The host is BCL-only
        // (TcpListener), so it lives in Core; no URL ACL is required.
        services.AddSingleton<ISubscriptionClient, SubscriptionClient>();
        services.AddSingleton<TcpListenerEventCallbackHost>();
        services.AddSingleton<IEventCallbackHost>(sp => sp.GetRequiredService<TcpListenerEventCallbackHost>());
        services.AddSingleton<SubscriptionPopupFactory>();

        // Device properties popup (Phase 17 FR-052)
        services.AddSingleton<DevicePropertiesPopupFactory>();

        // Network adapter selector (Phase 16 FR-048): single source of truth
        // for "which NIC are we on right now". Default-binds to the first
        // eligible adapter; user can switch via the View menu.
        services.AddSingleton<INetworkAdapterSelector, NetworkAdapterSelector>();

        // View-models (Phase 3 US1)
        services.AddSingleton(sp => new DeviceTreeViewModel(
            sp.GetRequiredService<DeviceRegistry>(),
            sp.GetRequiredService<IDispatcher>(),
            sp.GetRequiredService<DeviceNodeFactory>()));

        // SSDP log view-model (Phase 6 US4)
        services.AddSingleton<SsdpLogViewModel>();

        // Diagnostics viewer (Phase 11): one fresh VM per window open.
        services.AddTransient(sp => new DiagnosticsViewerViewModel(
            sp.GetRequiredService<IDiagnosticBuffer>(),
            sp.GetRequiredService<DeviceRegistry>(),
            sp.GetRequiredService<IDispatcher>()));

        services.AddSingleton<ShellViewModel>();

        return services;
    }

    private static string DefaultLogDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UpnpSpy",
        "logs");
}
