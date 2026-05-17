using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Lifecycle;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.ViewModels;

/// <summary>
/// Constructs <see cref="DeviceNodeViewModel"/> instances with the
/// registry / dispatcher / browser-launcher dependencies wired in. The VM no
/// longer initiates description fetches itself (see <see cref="EagerDescriptionDispatcher"/>),
/// so it depends on <see cref="DeviceRegistry"/> instead of
/// <see cref="Description.IDeviceDescriptionFetcher"/> — it subscribes to
/// <see cref="DeviceRegistry.DeviceUpdated"/> to learn when a still-in-flight
/// description fetch settles.
/// </summary>
public sealed class DeviceNodeFactory
{
    private readonly DeviceRegistry _registry;
    private readonly ServiceNodeFactory _serviceFactory;
    private readonly IDispatcher _dispatcher;
    private readonly IBrowserLauncher _browserLauncher;
    private readonly AppShutdownTokenSource _shutdown;

    public DeviceNodeFactory(
        DeviceRegistry registry,
        ServiceNodeFactory serviceFactory,
        IDispatcher dispatcher,
        IBrowserLauncher browserLauncher,
        AppShutdownTokenSource shutdown)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _browserLauncher = browserLauncher ?? throw new ArgumentNullException(nameof(browserLauncher));
        _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
    }

    public DeviceNodeViewModel Create(Device device) =>
        new(device, _registry, _serviceFactory, _dispatcher, _browserLauncher, _shutdown.Token);
}
