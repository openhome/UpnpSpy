using System.Text;
using FluentAssertions;
using UpnpSpy.Core.Eventing;
using Xunit;

namespace UpnpSpy.Tests.Eventing;

public sealed class HttpRequestReaderTests
{
    private static readonly HttpRequestReader.ReaderLimits TestLimits =
        new(MaxHeaderBytes: 4 * 1024, MaxBodyBytes: 16 * 1024, ReadTimeout: TimeSpan.FromSeconds(2));

    [Fact]
    public async Task Reads_well_formed_NOTIFY_with_content_length_body()
    {
        const string body = "<e:propertyset xmlns:e=\"urn:schemas-upnp-org:event-1-0\"><e:property><x>1</x></e:property></e:propertyset>";
        var wire =
            $"NOTIFY /upnpspy/abc/ HTTP/1.1\r\n" +
            $"HOST: 192.168.1.50:49152\r\n" +
            $"CONTENT-TYPE: text/xml; charset=\"utf-8\"\r\n" +
            $"NT: upnp:event\r\n" +
            $"NTS: upnp:propchange\r\n" +
            $"SID: uuid:fake-sid\r\n" +
            $"SEQ: 0\r\n" +
            $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n" +
            $"\r\n{body}";

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(wire));
        var req = await HttpRequestReader.ReadAsync(stream, TestLimits, CancellationToken.None);

        req.Should().NotBeNull();
        req!.Method.Should().Be("NOTIFY");
        req.RequestTarget.Should().Be("/upnpspy/abc/");
        req.HttpVersion.Should().Be("HTTP/1.1");
        req.Headers["nt"].Should().Be("upnp:event"); // case-insensitive lookup
        req.Headers["SeQ"].Should().Be("0");
        req.Body.Should().Be(body);
    }

    [Fact]
    public async Task Empty_body_when_no_content_length()
    {
        var wire =
            "NOTIFY /upnpspy/abc/ HTTP/1.1\r\n" +
            "HOST: 192.168.1.50:49152\r\n" +
            "\r\n";

        var stream = new MemoryStream(Encoding.ASCII.GetBytes(wire));
        var req = await HttpRequestReader.ReadAsync(stream, TestLimits, CancellationToken.None);

        req.Should().NotBeNull();
        req!.Body.Should().BeEmpty();
    }

    [Fact]
    public async Task Returns_null_when_header_block_exceeds_cap()
    {
        var huge = new StringBuilder();
        huge.Append("NOTIFY /upnpspy/abc/ HTTP/1.1\r\n");
        for (var i = 0; i < 200; i++)
            huge.Append("X-Padding-").Append(i).Append(": ").Append(new string('z', 256)).Append("\r\n");
        huge.Append("\r\n");

        var stream = new MemoryStream(Encoding.ASCII.GetBytes(huge.ToString()));
        var req = await HttpRequestReader.ReadAsync(stream, TestLimits, CancellationToken.None);

        req.Should().BeNull();
    }

    [Fact]
    public async Task Returns_null_when_content_length_exceeds_cap()
    {
        var wire =
            $"NOTIFY /upnpspy/abc/ HTTP/1.1\r\n" +
            $"Content-Length: {TestLimits.MaxBodyBytes + 1}\r\n" +
            $"\r\n";
        var stream = new MemoryStream(Encoding.ASCII.GetBytes(wire));
        var req = await HttpRequestReader.ReadAsync(stream, TestLimits, CancellationToken.None);

        req.Should().BeNull();
    }

    [Fact]
    public async Task Returns_null_when_connection_closes_mid_headers()
    {
        var wire = "NOTIFY /upnpspy/abc/ HTTP/1.1\r\nHOST: 192.168.1.50:49152\r\n"; // no trailing CRLF
        var stream = new MemoryStream(Encoding.ASCII.GetBytes(wire));
        var req = await HttpRequestReader.ReadAsync(stream, TestLimits, CancellationToken.None);

        req.Should().BeNull();
    }

    [Fact]
    public async Task Returns_null_when_request_line_is_malformed()
    {
        var wire = "GARBAGE\r\n\r\n";
        var stream = new MemoryStream(Encoding.ASCII.GetBytes(wire));
        var req = await HttpRequestReader.ReadAsync(stream, TestLimits, CancellationToken.None);

        req.Should().BeNull();
    }

    [Fact]
    public async Task Returns_null_when_body_is_shorter_than_content_length()
    {
        var wire =
            "NOTIFY /upnpspy/abc/ HTTP/1.1\r\n" +
            "Content-Length: 100\r\n" +
            "\r\nshort";
        var stream = new MemoryStream(Encoding.ASCII.GetBytes(wire));
        var req = await HttpRequestReader.ReadAsync(stream, TestLimits, CancellationToken.None);

        req.Should().BeNull();
    }

    [Fact]
    public async Task Per_request_timeout_returns_null_without_throwing()
    {
        var limits = new HttpRequestReader.ReaderLimits(
            MaxHeaderBytes: 4 * 1024, MaxBodyBytes: 16 * 1024,
            ReadTimeout: TimeSpan.FromMilliseconds(50));
        // A stream that produces 1 byte then blocks forever — simulate slowloris.
        using var hang = new SlowStream();
        var req = await HttpRequestReader.ReadAsync(hang, limits, CancellationToken.None);
        req.Should().BeNull();
    }

    private sealed class SlowStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => Task.Run(() => ReadAsync(buffer, offset, count, CancellationToken.None)).Result;
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
