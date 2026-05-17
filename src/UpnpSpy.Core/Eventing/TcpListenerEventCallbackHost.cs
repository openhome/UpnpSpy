using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using UpnpSpy.Core.Diagnostics;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.Eventing;

/// <summary>
/// FR-049: BCL-only GENA NOTIFY receiver. Binds <see cref="TcpListener"/> to
/// a specific local IPv4 address (chosen by the user-selected adapter,
/// FR-048), accepts TCP connections, parses HTTP/1.1 NOTIFY requests via
/// <see cref="HttpRequestReader"/>, and dispatches the parsed events into
/// per-registration channels. Because <c>TcpListener</c> uses raw BSD
/// sockets and bypasses Windows HTTP.SYS, no URL ACL is required and the
/// app runs as a normal non-Administrator user.
/// </summary>
public sealed class TcpListenerEventCallbackHost : IEventCallbackHost
{
    private const int ChannelCapacity = 1024;
    private const int DefaultStartPort = 49152;
    private const int MaxBindAttempts = 32;

    private readonly IDiagnosticSink _diagnostics;
    private readonly IClock _clock;
    private readonly Dictionary<Guid, Channel<EventNotification>> _channels = new();
    private readonly object _gate = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _accept;
    private IPAddress? _boundAddress;
    private int _port;
    private bool _disposed;

    public TcpListenerEventCallbackHost(IDiagnosticSink diagnostics, IClock clock)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public int Port => _port;
    public IPAddress? BoundAddress => _boundAddress;

    public Task StartAsync(IPAddress localAddress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(localAddress);
        lock (_gate)
        {
            if (_listener is not null)
            {
                if (!_boundAddress!.Equals(localAddress))
                    throw new InvalidOperationException(
                        $"Callback host already bound to {_boundAddress}; call StopAsync before rebinding.");
                return Task.CompletedTask;
            }

            for (var attempt = 0; attempt < MaxBindAttempts; attempt++)
            {
                var port = DefaultStartPort + attempt;
                var listener = new TcpListener(localAddress, port);
                try
                {
                    listener.Start();
                    _listener = listener;
                    _port = port;
                    _boundAddress = localAddress;
                    break;
                }
                catch (SocketException)
                {
                    try { listener.Stop(); } catch { }
                }
            }

            if (_listener is null)
            {
                Emit(DiagnosticSeverity.Error, "Eventing.Callback",
                    $"Failed to bind TcpListener on {localAddress}: no free port in range.", null, null);
                throw new InvalidOperationException(
                    $"Could not bind TcpListener on {localAddress} after {MaxBindAttempts} attempts.");
            }

            _cts = new CancellationTokenSource();
            // Capture locals so the accept-loop closure isn't racing the
            // StopAsync path which nulls these fields.
            var listenerLocal = _listener;
            var ctsLocal = _cts;
            _accept = Task.Run(() => AcceptLoopAsync(listenerLocal, ctsLocal.Token));
        }
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        TcpListener? listener;
        CancellationTokenSource? cts;
        Task? accept;
        Channel<EventNotification>[] channels;

        lock (_gate)
        {
            listener = _listener;
            cts = _cts;
            accept = _accept;
            _listener = null;
            _cts = null;
            _accept = null;
            _boundAddress = null;
            _port = 0;

            channels = _channels.Values.ToArray();
            _channels.Clear();
        }

        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        try { listener?.Stop(); } catch { }

        if (accept is not null)
        {
            try { await accept.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        foreach (var c in channels) c.Writer.TryComplete();
        cts?.Dispose();
    }

    public EventCallbackRegistration Register()
    {
        IPAddress? addr;
        int port;
        lock (_gate)
        {
            if (_listener is null || _boundAddress is null)
                throw new InvalidOperationException("Callback host not started.");
            addr = _boundAddress;
            port = _port;
        }

        var token = Guid.NewGuid();
        var channel = Channel.CreateBounded<EventNotification>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        lock (_gate) _channels[token] = channel;

        var url = new UriBuilder("http", addr.ToString(), port, $"/upnpspy/{token:N}/").Uri;
        return new EventCallbackRegistration(token, url);
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
        return ValueTask.CompletedTask;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { return; }

            _ = Task.Run(() => HandleConnectionAsync(client, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var request = await HttpRequestReader.ReadAsync(
                    stream, HttpRequestReader.ReaderLimits.Default, cancellationToken).ConfigureAwait(false);

                if (request is null)
                {
                    await WriteResponseAsync(stream, 400, "Bad Request", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!string.Equals(request.Method, "NOTIFY", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteResponseAsync(stream, 400, "Bad Request", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!TryExtractToken(request.RequestTarget, out var token))
                {
                    await WriteResponseAsync(stream, 400, "Bad Request", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!HeaderEquals(request.Headers, "NT", "upnp:event")
                    || !HeaderEquals(request.Headers, "NTS", "upnp:propchange"))
                {
                    Emit(DiagnosticSeverity.Warning, "Eventing.Callback",
                        "NOTIFY missing/invalid NT or NTS header.",
                        CallbackContext(token, request.RequestTarget), null);
                    await WriteResponseAsync(stream, 400, "Bad Request", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!request.Headers.TryGetValue("SID", out var sid) || string.IsNullOrEmpty(sid))
                {
                    Emit(DiagnosticSeverity.Warning, "Eventing.Callback",
                        "NOTIFY missing SID header.",
                        CallbackContext(token, request.RequestTarget), null);
                    await WriteResponseAsync(stream, 400, "Bad Request", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!request.Headers.TryGetValue("SEQ", out var seqValue)
                    || !uint.TryParse(seqValue, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var seq))
                {
                    Emit(DiagnosticSeverity.Warning, "Eventing.Callback",
                        "NOTIFY missing/invalid SEQ header.",
                        CallbackContext(token, request.RequestTarget), null);
                    await WriteResponseAsync(stream, 400, "Bad Request", cancellationToken).ConfigureAwait(false);
                    return;
                }

                var ev = GenaNotifyParser.Parse(request.Body, seq, _clock.UtcNow);
                if (ev.RawXml is not null && ev.Properties.Count == 0)
                {
                    Emit(DiagnosticSeverity.Warning, "Eventing.Callback",
                        "NOTIFY body malformed; surfacing raw payload to subscriber.",
                        CallbackContext(token, request.RequestTarget), null);
                }

                Channel<EventNotification>? channel;
                lock (_gate) _channels.TryGetValue(token, out channel);
                if (channel is null)
                {
                    await WriteResponseAsync(stream, 412, "Precondition Failed", cancellationToken).ConfigureAwait(false);
                    return;
                }

                await channel.Writer.WriteAsync(ev, cancellationToken).ConfigureAwait(false);
                await WriteResponseAsync(stream, 200, "OK", cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Emit(DiagnosticSeverity.Warning, "Eventing.Callback",
                "Unhandled exception in NOTIFY handler.", null, ex);
        }
    }

    private static bool TryExtractToken(string requestTarget, out Guid token)
    {
        token = default;
        var segments = requestTarget.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return false;
        if (!string.Equals(segments[0], "upnpspy", StringComparison.Ordinal)) return false;
        return Guid.TryParseExact(segments[1], "N", out token);
    }

    private static bool HeaderEquals(IReadOnlyDictionary<string, string> headers, string name, string expected) =>
        headers.TryGetValue(name, out var value)
        && string.Equals(value.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static async Task WriteResponseAsync(NetworkStream stream, int status, string reason, CancellationToken cancellationToken)
    {
        var line = $"HTTP/1.1 {status} {reason}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(line);
        try
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private static Dictionary<string, string> CallbackContext(Guid token, string? requestTarget)
    {
        var ctx = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["callback.token"] = token == Guid.Empty ? "(unknown)" : token.ToString("N"),
        };
        if (!string.IsNullOrEmpty(requestTarget)) ctx["request.target"] = requestTarget;
        return ctx;
    }

    private void Emit(DiagnosticSeverity severity, string category, string message,
        IReadOnlyDictionary<string, string>? context, Exception? exception) =>
        _diagnostics.Record(new DiagnosticEntry(
            Timestamp: _clock.UtcNow,
            Severity: severity,
            Category: category,
            Message: message,
            Context: context ?? new Dictionary<string, string>(StringComparer.Ordinal),
            Exception: exception?.ToString()));

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
