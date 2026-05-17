using System.Net;
using UpnpSpy.Core.Models;

namespace UpnpSpy.Core.Eventing;

/// <summary>
/// Hosts the local HTTP endpoint that subscribed devices NOTIFY back to
/// (UDA 1.0 §4.3). v1 production implementation
/// (<c>TcpListenerEventCallbackHost</c>) binds <see cref="System.Net.Sockets.TcpListener"/>
/// to a specific local IPv4 address (FR-049) — no Windows URL ACL grant is
/// required. Tests substitute <c>FakeEventCallbackHost</c>.
/// </summary>
public interface IEventCallbackHost : IAsyncDisposable
{
    /// <summary>
    /// Binds the listener on <paramref name="localAddress"/>:&lt;dynamic port&gt;.
    /// Must complete before the first <see cref="Register"/> call. Idempotent
    /// no-op if already started on the same address; throw if called twice
    /// with different addresses without an intervening <see cref="StopAsync"/>.
    /// </summary>
    Task StartAsync(IPAddress localAddress, CancellationToken cancellationToken);

    /// <summary>
    /// Stops the listener and drops every active registration. Idempotent
    /// no-op if not started. Used by the adapter-switch path (FR-050) to
    /// rebind on a different local IP.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Mints a fresh callback slot. Returns the absolute URL the device
    /// should call back on, constructed from the address the host is bound
    /// to and the port it is listening on.
    /// </summary>
    EventCallbackRegistration Register();

    /// <summary>
    /// Stream of every parsed NOTIFY for a given registration. Bounded
    /// channel per subscription; on overflow oldest events are dropped and a
    /// Warning diagnostic is recorded.
    /// </summary>
    IAsyncEnumerable<EventNotification> EventsFor(EventCallbackRegistration registration, CancellationToken cancellationToken);

    /// <summary>
    /// Tears down a registration after UNSUBSCRIBE / popup close.
    /// </summary>
    ValueTask UnregisterAsync(EventCallbackRegistration registration);
}
