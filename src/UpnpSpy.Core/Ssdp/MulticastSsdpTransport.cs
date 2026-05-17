using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.Ssdp;

/// <summary>
/// Single-adapter multicast SSDP transport (FR-004, FR-048): one
/// <see cref="UdpClient"/> bound on the IPv4 address currently published by
/// <see cref="INetworkAdapterSelector"/>. <see cref="RestartAsync"/> tears
/// the socket down and rebinds on the new selection so the adapter switch
/// orchestration in <c>ShellViewModel</c> can swap NICs at runtime (FR-050).
/// </summary>
public sealed class MulticastSsdpTransport : ISsdpTransport
{
    private static readonly IPAddress SsdpMulticastGroup = IPAddress.Parse("239.255.255.250");
    private static readonly IPEndPoint SsdpMulticastEndpoint = new(SsdpMulticastGroup, 1900);
    private const int ChannelCapacity = 4096;

    private readonly INetworkAdapterSelector _selector;
    private readonly IClock _clock;
    private readonly ILogger<MulticastSsdpTransport> _logger;
    private readonly Channel<ReceivedSsdpDatagram> _channel;
    private readonly object _gate = new();
    private BoundSocket? _socket;
    private bool _disposed;

    public MulticastSsdpTransport(
        INetworkAdapterSelector selector,
        IClock clock,
        ILogger<MulticastSsdpTransport> logger)
    {
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _channel = Channel.CreateBounded<ReceivedSsdpDatagram>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
    }

    public IAsyncEnumerable<ReceivedSsdpDatagram> ReceivedMessages =>
        _channel.Reader.ReadAllAsync();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_socket is not null) return Task.CompletedTask;
            _socket = TryBindOnSelected();
        }
        return Task.CompletedTask;
    }

    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        BoundSocket? old;
        lock (_gate)
        {
            old = _socket;
            _socket = null;
        }

        if (old is not null)
        {
            try { old.ReceiveCts.Cancel(); } catch (ObjectDisposedException) { }
            try { old.Client.Close(); } catch { }
            if (old.ReceiveTask is not null)
            {
                try { await old.ReceiveTask.ConfigureAwait(false); } catch { }
            }
            try { old.ReceiveCts.Dispose(); } catch { }
        }

        BoundSocket? next;
        lock (_gate)
        {
            if (_disposed) return;
            next = TryBindOnSelected();
            _socket = next;
        }
    }

    private BoundSocket? TryBindOnSelected()
    {
        var nic = _selector.Selected;
        if (nic is null)
        {
            _logger.LogWarning("No eligible adapter selected; SSDP transport is idle");
            return null;
        }

        try
        {
            var client = new UdpClient(AddressFamily.InterNetwork);
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.Client.Bind(new IPEndPoint(nic.Ipv4Address, 0));
            client.JoinMulticastGroup(SsdpMulticastGroup, nic.Ipv4Address);
            client.MulticastLoopback = false;

            var cts = new CancellationTokenSource();
            var bound = new BoundSocket(client, nic.Name, nic.Ipv4Address, cts);
            bound.ReceiveTask = Task.Run(() => ReceiveLoopAsync(bound, cts.Token));
            _logger.LogInformation("SSDP socket bound on {Interface} ({Address})", nic.Name, nic.Ipv4Address);
            return bound;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to bind SSDP socket on {Interface}", nic.Name);
            return null;
        }
    }

    public async Task SendMSearchAsync(string searchTarget, TimeSpan maxWait, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(searchTarget))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(searchTarget));
        if (maxWait < TimeSpan.FromSeconds(1) || maxWait > TimeSpan.FromSeconds(5))
            throw new ArgumentOutOfRangeException(nameof(maxWait), "MX must be 1..5 seconds (UDA 1.0 §1.2.1).");

        BoundSocket? snapshot;
        lock (_gate) snapshot = _socket;
        if (snapshot is null) return;

        var payload = BuildMSearchPayload(searchTarget, (int)maxWait.TotalSeconds);
        try
        {
            await snapshot.Client.SendAsync(payload, payload.Length, SsdpMulticastEndpoint).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "M-SEARCH send failed on {Interface}", snapshot.InterfaceName);
        }
    }

    private static byte[] BuildMSearchPayload(string st, int mxSeconds)
    {
        var sb = new StringBuilder(256);
        sb.Append("M-SEARCH * HTTP/1.1\r\n");
        sb.Append("HOST: 239.255.255.250:1900\r\n");
        sb.Append("MAN: \"ssdp:discover\"\r\n");
        sb.Append("MX: ").Append(mxSeconds).Append("\r\n");
        sb.Append("ST: ").Append(st).Append("\r\n");
        sb.Append("USER-AGENT: UpnpSpy/1.0 Windows\r\n");
        sb.Append("\r\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private async Task ReceiveLoopAsync(BoundSocket bound, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UdpReceiveResult received;
                try { received = await bound.Client.ReceiveAsync(cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                catch (ObjectDisposedException) { return; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Receive failed on {Interface}", bound.InterfaceName);
                    continue;
                }

                var datagram = new ReceivedSsdpDatagram(
                    ReceivedUtc: _clock.UtcNow,
                    InterfaceName: bound.InterfaceName,
                    RemoteEndpoint: received.RemoteEndPoint,
                    Payload: received.Buffer);

                if (!_channel.Writer.TryWrite(datagram))
                {
                    _logger.LogWarning("SSDP datagram dropped (channel overflow) on {Interface}", bound.InterfaceName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Receive loop crashed on {Interface}", bound.InterfaceName);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        BoundSocket? snapshot;
        lock (_gate)
        {
            snapshot = _socket;
            _socket = null;
        }

        if (snapshot is not null)
        {
            try { snapshot.ReceiveCts.Cancel(); } catch (ObjectDisposedException) { }
            try { snapshot.Client.Close(); } catch { }
            if (snapshot.ReceiveTask is not null)
            {
                try { await snapshot.ReceiveTask.ConfigureAwait(false); } catch { }
            }
            try { snapshot.ReceiveCts.Dispose(); } catch { }
        }

        _channel.Writer.TryComplete();
    }

    private sealed class BoundSocket
    {
        public BoundSocket(UdpClient client, string interfaceName, IPAddress localAddress, CancellationTokenSource receiveCts)
        {
            Client = client;
            InterfaceName = interfaceName;
            LocalAddress = localAddress;
            ReceiveCts = receiveCts;
        }

        public UdpClient Client { get; }
        public string InterfaceName { get; }
        public IPAddress LocalAddress { get; }
        public CancellationTokenSource ReceiveCts { get; }
        public Task? ReceiveTask { get; set; }
    }
}
