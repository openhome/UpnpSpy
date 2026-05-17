using UpnpSpy.Core.Description;
using UpnpSpy.Core.Lifecycle;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.ViewModels;

/// <summary>
/// Constructs <see cref="ServiceNodeViewModel"/> instances. Lets a parent
/// <see cref="DeviceNodeViewModel"/> stay decoupled from the SCPD fetcher and
/// keeps DI registration of the view-models declarative.
/// </summary>
public sealed class ServiceNodeFactory
{
    private readonly IScpdFetcher _scpdFetcher;
    private readonly IDispatcher _dispatcher;
    private readonly IBrowserLauncher _browserLauncher;
    private readonly AppShutdownTokenSource _shutdown;

    public ServiceNodeFactory(
        IScpdFetcher scpdFetcher,
        IDispatcher dispatcher,
        IBrowserLauncher browserLauncher,
        AppShutdownTokenSource shutdown)
    {
        _scpdFetcher = scpdFetcher ?? throw new ArgumentNullException(nameof(scpdFetcher));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _browserLauncher = browserLauncher ?? throw new ArgumentNullException(nameof(browserLauncher));
        _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
    }

    public ServiceNodeViewModel Create(Service service) =>
        new(service, _scpdFetcher, _dispatcher, _browserLauncher, _shutdown.Token);
}
