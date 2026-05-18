using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.Ssdp;

/// <summary>
/// Single-adapter multicast SSDP transport (FR-004, FR-048). Uses two sockets
/// on the selected NIC:
///
/// 1. A listener bound to <c>0.0.0.0:1900</c> with <c>SO_REUSEADDR</c> and
///    joined to the SSDP multicast group on the NIC — receives the multicast
///    NOTIFY ssdp:alive / ssdp:byebye stream (UDA 1.0 §1.1). Co-existing with
///    Windows' SSDPSRV is fine here because each joined socket gets its own
///    copy of every multicast datagram.
/// 2. A search socket bound to <c>&lt;nic-ip&gt;:0</c> (ephemeral port) — sends
///    M-SEARCH and receives the unicast 200-OK replies (UDA 1.0 §1.2). The
///    ephemeral port is essential on Windows: SSDPSRV holds port 1900, and
///    with <c>SO_REUSEADDR</c> sharing, unicast replies addressed to that port
///    are delivered to only one socket (typically SSDPSRV), so an M-SEARCH
///    socket bound to 1900 silently loses its responses.
///
/// <see cref="RestartAsync"/> tears both sockets down and rebinds on the
/// newly-selected NIC so the adapter-switch orchestration in
/// <c>ShellViewModel</c> can swap NICs at runtime (FR-050).
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
    private BoundSockets? _sockets;
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
            if (_sockets is not null) return Task.CompletedTask;
            _sockets = TryBindOnSelected();
        }
        return Task.CompletedTask;
    }

    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        BoundSockets? old;
        lock (_gate)
        {
            old = _sockets;
            _sockets = null;
        }

        if (old is not null) await TearDownAsync(old).ConfigureAwait(false);

        lock (_gate)
        {
            if (_disposed) return;
            _sockets = TryBindOnSelected();
        }
    }

    private BoundSockets? TryBindOnSelected()
    {
        var nic = _selector.Selected;
        if (nic is null)
        {
            _logger.LogWarning("No eligible adapter selected; SSDP transport is idle");
            return null;
        }

        BoundReceiver? listener = null;
        BoundReceiver? searcher = null;
        try
        {
            listener = BindListener(nic);
            searcher = BindSearcher(nic);
            _logger.LogInformation(
                "SSDP sockets bound on {Interface} ({Address}); search port {SearchPort}",
                nic.Name,
                nic.Ipv4Address,
                ((IPEndPoint)searcher.Client.Client.LocalEndPoint!).Port);
            return new BoundSockets(listener, searcher);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to bind SSDP sockets on {Interface}", nic.Name);
            TearDownSync(listener);
            TearDownSync(searcher);
            return null;
        }
    }

    private BoundReceiver BindListener(EligibleInterface nic)
    {
        var client = new UdpClient(AddressFamily.InterNetwork);
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        // Bind 0.0.0.0:1900 so the kernel delivers multicast NOTIFY ssdp:alive /
        // ssdp:byebye to us. SSDPSRV may also hold this port; SO_REUSEADDR + the
        // multicast join below gives us our own copy of every multicast datagram.
        // We deliberately do NOT send M-SEARCH from this socket — see BindSearcher.
        client.Client.Bind(new IPEndPoint(IPAddress.Any, 1900));
        client.JoinMulticastGroup(SsdpMulticastGroup, nic.Ipv4Address);
        client.MulticastLoopback = false;
        return StartReceiver(client, nic.Name);
    }

    private BoundReceiver BindSearcher(EligibleInterface nic)
    {
        var client = new UdpClient(AddressFamily.InterNetwork);
        // Bind <nic-ip>:0 so M-SEARCH replies (unicast back to source IP:port)
        // come to a port that no other process holds — avoids the Windows
        // SSDPSRV port-1900 collision that swallows replies on a shared socket.
        // Binding to the NIC IP also anchors the outgoing multicast send to
        // this interface.
        client.Client.Bind(new IPEndPoint(nic.Ipv4Address, 0));
        // Belt-and-braces: explicitly pin the multicast egress interface in
        // case the kernel would otherwise consult the routing table for the
        // 239.255.255.250 destination.
        client.Client.SetSocketOption(
            SocketOptionLevel.IP,
            SocketOptionName.MulticastInterface,
            nic.Ipv4Address.GetAddressBytes());
        client.MulticastLoopback = false;
        return StartReceiver(client, nic.Name);
    }

    private BoundReceiver StartReceiver(UdpClient client, string interfaceName)
    {
        var cts = new CancellationTokenSource();
        var receiver = new BoundReceiver(client, interfaceName, cts);
        receiver.ReceiveTask = Task.Run(() => ReceiveLoopAsync(receiver, cts.Token));
        return receiver;
    }

    public async Task SendMSearchAsync(string searchTarget, TimeSpan maxWait, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(searchTarget))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(searchTarget));
        if (maxWait < TimeSpan.FromSeconds(1) || maxWait > TimeSpan.FromSeconds(5))
            throw new ArgumentOutOfRangeException(nameof(maxWait), "MX must be 1..5 seconds (UDA 1.0 §1.2.1).");

        BoundSockets? snapshot;
        lock (_gate) snapshot = _sockets;
        if (snapshot is null) return;

        var payload = BuildMSearchPayload(searchTarget, (int)maxWait.TotalSeconds);
        try
        {
            await snapshot.Searcher.Client
                .SendAsync(payload, payload.Length, SsdpMulticastEndpoint)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "M-SEARCH send failed on {Interface}", snapshot.Searcher.InterfaceName);
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

    private async Task ReceiveLoopAsync(BoundReceiver bound, CancellationToken cancellationToken)
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

        BoundSockets? snapshot;
        lock (_gate)
        {
            snapshot = _sockets;
            _sockets = null;
        }

        if (snapshot is not null) await TearDownAsync(snapshot).ConfigureAwait(false);

        _channel.Writer.TryComplete();
    }

    private static async Task TearDownAsync(BoundSockets sockets)
    {
        await TearDownAsync(sockets.Listener).ConfigureAwait(false);
        await TearDownAsync(sockets.Searcher).ConfigureAwait(false);
    }

    private static async Task TearDownAsync(BoundReceiver receiver)
    {
        try { receiver.ReceiveCts.Cancel(); } catch (ObjectDisposedException) { }
        try { receiver.Client.Close(); } catch { }
        if (receiver.ReceiveTask is not null)
        {
            try { await receiver.ReceiveTask.ConfigureAwait(false); } catch { }
        }
        try { receiver.ReceiveCts.Dispose(); } catch { }
    }

    private static void TearDownSync(BoundReceiver? receiver)
    {
        if (receiver is null) return;
        try { receiver.ReceiveCts.Cancel(); } catch (ObjectDisposedException) { }
        try { receiver.Client.Close(); } catch { }
        try { receiver.ReceiveCts.Dispose(); } catch { }
    }

    private sealed class BoundSockets
    {
        public BoundSockets(BoundReceiver listener, BoundReceiver searcher)
        {
            Listener = listener;
            Searcher = searcher;
        }

        public BoundReceiver Listener { get; }
        public BoundReceiver Searcher { get; }
    }

    private sealed class BoundReceiver
    {
        public BoundReceiver(UdpClient client, string interfaceName, CancellationTokenSource receiveCts)
        {
            Client = client;
            InterfaceName = interfaceName;
            ReceiveCts = receiveCts;
        }

        public UdpClient Client { get; }
        public string InterfaceName { get; }
        public CancellationTokenSource ReceiveCts { get; }
        public Task? ReceiveTask { get; set; }
    }
}
