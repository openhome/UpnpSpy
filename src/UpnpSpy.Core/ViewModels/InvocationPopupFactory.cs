using UpnpSpy.Core.Control;
using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Lifecycle;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.ViewModels;

/// <summary>
/// Builds one <see cref="InvocationPopupViewModel"/> per popup. Centralises the
/// dependency wiring so the WinUI view can stay ignorant of how the VM is
/// constructed.
/// </summary>
public sealed class InvocationPopupFactory
{
    private readonly IControlClient _controlClient;
    private readonly DeviceRegistry _registry;
    private readonly IDispatcher _dispatcher;
    private readonly IClock _clock;
    private readonly AppShutdownTokenSource _shutdown;

    public InvocationPopupFactory(
        IControlClient controlClient,
        DeviceRegistry registry,
        IDispatcher dispatcher,
        IClock clock,
        AppShutdownTokenSource shutdown)
    {
        _controlClient = controlClient ?? throw new ArgumentNullException(nameof(controlClient));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
    }

    public InvocationPopupViewModel Create(Service service, ActionDefinition action) =>
        new(service, action, _controlClient, _registry, _dispatcher, _clock, _shutdown.Token);
}
