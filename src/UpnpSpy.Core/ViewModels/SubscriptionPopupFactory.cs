using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Eventing;
using UpnpSpy.Core.Lifecycle;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.ViewModels;

/// <summary>
/// Builds one <see cref="SubscriptionPopupViewModel"/> per popup. Since
/// FR-048/FR-049 the application binds the callback host on the
/// user-selected adapter's IPv4 address (FR-048); the popup does not need
/// to pick its own callback IP — the host's bound IP wins.
/// </summary>
public sealed class SubscriptionPopupFactory
{
    private static readonly TimeSpan DefaultRequestedTimeout = TimeSpan.FromMinutes(30);

    private readonly ISubscriptionClient _subscriptionClient;
    private readonly IEventCallbackHost _callbackHost;
    private readonly DeviceRegistry _registry;
    private readonly IDispatcher _dispatcher;
    private readonly IClock _clock;
    private readonly AppShutdownTokenSource _shutdown;

    public SubscriptionPopupFactory(
        ISubscriptionClient subscriptionClient,
        IEventCallbackHost callbackHost,
        DeviceRegistry registry,
        IDispatcher dispatcher,
        IClock clock,
        AppShutdownTokenSource shutdown)
    {
        _subscriptionClient = subscriptionClient ?? throw new ArgumentNullException(nameof(subscriptionClient));
        _callbackHost = callbackHost ?? throw new ArgumentNullException(nameof(callbackHost));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
    }

    public SubscriptionPopupViewModel Create(Service service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return new SubscriptionPopupViewModel(
            service, _subscriptionClient, _callbackHost,
            _registry, _dispatcher, _clock, DefaultRequestedTimeout, _shutdown.Token);
    }
}
