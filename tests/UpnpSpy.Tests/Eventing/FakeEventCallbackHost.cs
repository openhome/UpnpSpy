using System.Net;
using System.Threading.Channels;
using UpnpSpy.Core.Eventing;
using UpnpSpy.Core.Models;

namespace UpnpSpy.Tests.Eventing;

/// <summary>
/// In-memory <see cref="IEventCallbackHost"/> with predictable per-call tokens
/// and a manual push API so tests can synthesise NOTIFY arrivals.
/// </summary>
internal sealed class FakeEventCallbackHost : IEventCallbackHost
{
    private readonly Dictionary<Guid, Channel<EventNotification>> _channels = new();
    private readonly object _gate = new();
    private IPAddress _boundAddress = IPAddress.Loopback;

    public List<EventCallbackRegistration> Registrations { get; } = new();
    public List<EventCallbackRegistration> Unregistered { get; } = new();
    public bool Started { get; private set; }
    public bool Stopped { get; private set; }

    public Task StartAsync(IPAddress localAddress, CancellationToken cancellationToken)
    {
        _boundAddress = localAddress ?? IPAddress.Loopback;
        Started = true;
        Stopped = false;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Stopped = true;
        lock (_gate)
        {
            foreach (var c in _channels.Values) c.Writer.TryComplete();
            _channels.Clear();
        }
        return Task.CompletedTask;
    }

    public EventCallbackRegistration Register()
    {
        var token = Guid.NewGuid();
        var url = new Uri($"http://{_boundAddress}:0/upnpspy/{token:N}/");
        var registration = new EventCallbackRegistration(token, url);
        var channel = Channel.CreateUnbounded<EventNotification>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        lock (_gate) _channels[token] = channel;
        Registrations.Add(registration);
        return registration;
    }

    public async IAsyncEnumerable<EventNotification> EventsFor(
        EventCallbackRegistration registration,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Channel<EventNotification>? channel;
        lock (_gate) _channels.TryGetValue(registration.Token, out channel);
        if (channel is null) yield break;

        while (await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (channel.Reader.TryRead(out var ev))
                yield return ev;
        }
    }

    public ValueTask UnregisterAsync(EventCallbackRegistration registration)
    {
        Channel<EventNotification>? channel;
        lock (_gate)
        {
            _channels.TryGetValue(registration.Token, out channel);
            _channels.Remove(registration.Token);
        }
        channel?.Writer.TryComplete();
        Unregistered.Add(registration);
        return ValueTask.CompletedTask;
    }

    public void Push(EventCallbackRegistration registration, EventNotification notification)
    {
        Channel<EventNotification>? channel;
        lock (_gate) _channels.TryGetValue(registration.Token, out channel);
        channel?.Writer.TryWrite(notification);
    }

    public ValueTask DisposeAsync()
    {
        Channel<EventNotification>[] all;
        lock (_gate)
        {
            all = _channels.Values.ToArray();
            _channels.Clear();
        }
        foreach (var c in all) c.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
