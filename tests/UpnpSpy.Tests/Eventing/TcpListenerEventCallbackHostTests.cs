using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using UpnpSpy.Core.Eventing;
using UpnpSpy.Core.Models;
using UpnpSpy.Tests.TestHelpers;
using Xunit;

namespace UpnpSpy.Tests.Eventing;

/// <summary>
/// Loopback round-trip tests for the TcpListener-based callback host.
/// Binds to 127.0.0.1 on a dynamic port, then sends raw HTTP/1.1 NOTIFY
/// requests via TcpClient — exercises the full accept-loop, parser,
/// dispatch, and response path.
/// </summary>
public sealed class TcpListenerEventCallbackHostTests
{
    [Fact]
    public async Task Round_trip_NOTIFY_delivers_parsed_event_and_returns_200()
    {
        var sink = new RecordingDiagnosticSink();
        var clock = new FakeClock();
        await using var host = new TcpListenerEventCallbackHost(sink, clock);
        await host.StartAsync(IPAddress.Loopback, CancellationToken.None);

        var reg = host.Register();
        var token = reg.CallbackUrl.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)[1];

        var body = "<e:propertyset xmlns:e=\"urn:schemas-upnp-org:event-1-0\">" +
                   "<e:property><Volume>5</Volume></e:property></e:propertyset>";
        var status = await SendNotifyAsync(host.Port, token, body, sid: "uuid:sid-1", seq: 0);

        status.Should().Be(200);

        // Drain one event from the channel.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        EventNotification? captured = null;
        await foreach (var ev in host.EventsFor(reg, cts.Token))
        {
            captured = ev;
            break;
        }
        captured.Should().NotBeNull();
        captured!.Properties.Should().ContainKey("Volume").WhoseValue.Should().Be("5");
        captured.SequenceNumber.Should().Be(0u);
    }

    [Fact]
    public async Task Wrong_method_returns_400()
    {
        await using var host = new TcpListenerEventCallbackHost(new RecordingDiagnosticSink(), new FakeClock());
        await host.StartAsync(IPAddress.Loopback, CancellationToken.None);
        var reg = host.Register();
        var token = reg.CallbackUrl.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)[1];

        var status = await SendRawAsync(host.Port, $"GET /upnpspy/{token}/ HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n");
        status.Should().Be(400);
    }

    [Fact]
    public async Task Unknown_token_returns_412()
    {
        await using var host = new TcpListenerEventCallbackHost(new RecordingDiagnosticSink(), new FakeClock());
        await host.StartAsync(IPAddress.Loopback, CancellationToken.None);

        // Token has the right format but no registration.
        var bogusToken = Guid.NewGuid().ToString("N");
        var body = "<e:propertyset/>";
        var status = await SendNotifyAsync(host.Port, bogusToken, body, sid: "uuid:s", seq: 0);

        status.Should().Be(412);
    }

    [Fact]
    public async Task UnregisterAsync_then_NOTIFY_returns_412()
    {
        await using var host = new TcpListenerEventCallbackHost(new RecordingDiagnosticSink(), new FakeClock());
        await host.StartAsync(IPAddress.Loopback, CancellationToken.None);

        var reg = host.Register();
        var token = reg.CallbackUrl.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)[1];
        await host.UnregisterAsync(reg);

        var status = await SendNotifyAsync(host.Port, token, "<e:propertyset/>", sid: "uuid:s", seq: 0);
        status.Should().Be(412);
    }

    [Fact]
    public async Task StopAsync_releases_the_port()
    {
        var host = new TcpListenerEventCallbackHost(new RecordingDiagnosticSink(), new FakeClock());
        await host.StartAsync(IPAddress.Loopback, CancellationToken.None);
        var port = host.Port;
        port.Should().BeGreaterThan(0);

        await host.StopAsync(CancellationToken.None);

        // After StopAsync the port should be free — re-bind on the same port.
        var probe = new TcpListener(IPAddress.Loopback, port);
        probe.Start();
        probe.Stop();
    }

    [Fact]
    public async Task Missing_NT_header_records_warning_diagnostic_and_returns_400()
    {
        var sink = new RecordingDiagnosticSink();
        await using var host = new TcpListenerEventCallbackHost(sink, new FakeClock());
        await host.StartAsync(IPAddress.Loopback, CancellationToken.None);
        var reg = host.Register();
        var token = reg.CallbackUrl.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)[1];

        var body = "<e:propertyset/>";
        var wire =
            $"NOTIFY /upnpspy/{token}/ HTTP/1.1\r\n" +
            $"Host: 127.0.0.1:{host.Port}\r\n" +
            // NT missing
            $"NTS: upnp:propchange\r\n" +
            $"SID: uuid:sid\r\n" +
            $"SEQ: 0\r\n" +
            $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n" +
            $"\r\n{body}";

        var status = await SendRawAsync(host.Port, wire);
        status.Should().Be(400);
        sink.Entries.Should().Contain(e => e.Category == "Eventing.Callback");
    }

    private static async Task<int> SendNotifyAsync(int port, string token, string body, string sid, uint seq)
    {
        var wire =
            $"NOTIFY /upnpspy/{token}/ HTTP/1.1\r\n" +
            $"Host: 127.0.0.1:{port}\r\n" +
            $"NT: upnp:event\r\n" +
            $"NTS: upnp:propchange\r\n" +
            $"SID: {sid}\r\n" +
            $"SEQ: {seq}\r\n" +
            $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n" +
            $"Content-Type: text/xml; charset=\"utf-8\"\r\n" +
            $"\r\n{body}";
        return await SendRawAsync(port, wire);
    }

    private static async Task<int> SendRawAsync(int port, string wire)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var stream = client.GetStream();
        var bytes = Encoding.UTF8.GetBytes(wire);
        await stream.WriteAsync(bytes);

        // Read response status line.
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var statusLine = await reader.ReadLineAsync() ?? "";
        var parts = statusLine.Split(' ', 3);
        return parts.Length >= 2 && int.TryParse(parts[1], out var code) ? code : -1;
    }
}
