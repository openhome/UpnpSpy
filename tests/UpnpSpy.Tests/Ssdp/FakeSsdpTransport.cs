using System.Net;
using System.Threading.Channels;
using UpnpSpy.Core.Ssdp;

namespace UpnpSpy.Tests.Ssdp;

/// <summary>Test double for ISsdpTransport. Tests push datagrams into PushDatagram(...).</summary>
public sealed class FakeSsdpTransport : ISsdpTransport
{
    private readonly Channel<ReceivedSsdpDatagram> _channel =
        Channel.CreateUnbounded<ReceivedSsdpDatagram>(new UnboundedChannelOptions { SingleReader = true });

    public List<SentMSearch> SentMSearches { get; } = new();
    public bool Started { get; private set; }
    public bool Disposed { get; private set; }

    public IAsyncEnumerable<ReceivedSsdpDatagram> ReceivedMessages => _channel.Reader.ReadAllAsync();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Started = true;
        return Task.CompletedTask;
    }

    public int RestartCount { get; private set; }

    public Task RestartAsync(CancellationToken cancellationToken)
    {
        RestartCount++;
        return Task.CompletedTask;
    }

    public Task SendMSearchAsync(string searchTarget, TimeSpan maxWait, CancellationToken cancellationToken)
    {
        SentMSearches.Add(new SentMSearch(searchTarget, maxWait));
        return Task.CompletedTask;
    }

    public void PushDatagram(ReceivedSsdpDatagram datagram) => _channel.Writer.TryWrite(datagram);

    public void CompleteReceiveStream() => _channel.Writer.TryComplete();

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        _channel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    public static ReceivedSsdpDatagram MakeDatagram(byte[] payload, string interfaceName = "fake-nic", string remoteIp = "192.0.2.10")
    {
        return new ReceivedSsdpDatagram(
            ReceivedUtc: DateTimeOffset.UtcNow,
            InterfaceName: interfaceName,
            RemoteEndpoint: new IPEndPoint(IPAddress.Parse(remoteIp), 1900),
            Payload: payload);
    }
}

public sealed record SentMSearch(string SearchTarget, TimeSpan MaxWait);
