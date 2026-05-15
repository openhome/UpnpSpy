using System.Globalization;
using System.Text;

namespace UpnpSpy.Core.Eventing;

/// <summary>
/// Minimal HTTP/1.1 request reader for the GENA NOTIFY traffic profile (FR-049):
/// request line + headers + <c>Content-Length</c>-bounded body. UPnP devices
/// in the wild emit small, well-framed bodies; this reader rejects anything
/// outside that envelope and returns <c>null</c> rather than throwing.
/// </summary>
public static class HttpRequestReader
{
    public sealed record ReaderLimits(int MaxHeaderBytes, int MaxBodyBytes, TimeSpan ReadTimeout)
    {
        public static readonly ReaderLimits Default =
            new(MaxHeaderBytes: 8 * 1024, MaxBodyBytes: 64 * 1024, ReadTimeout: TimeSpan.FromSeconds(5));
    }

    public sealed record ParsedHttpRequest(
        string Method,
        string RequestTarget,
        string HttpVersion,
        IReadOnlyDictionary<string, string> Headers,
        string Body);

    /// <summary>
    /// Reads one HTTP/1.1 request off <paramref name="stream"/>. Returns
    /// <c>null</c> if the request is malformed, exceeds the configured limits,
    /// or the connection closes before the request completes. Never throws on
    /// untrusted input; <see cref="OperationCanceledException"/> from the
    /// supplied <paramref name="cancellationToken"/> is propagated.
    /// </summary>
    public static async Task<ParsedHttpRequest?> ReadAsync(
        Stream stream,
        ReaderLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(limits);

        using var timeoutCts = new CancellationTokenSource(limits.ReadTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var headerBytes = await ReadUntilCrLfCrLfAsync(stream, limits.MaxHeaderBytes, linked.Token).ConfigureAwait(false);
            if (headerBytes is null) return null;

            var headerText = Encoding.ASCII.GetString(headerBytes);
            var lines = SplitHeaderLines(headerText);
            if (lines.Count == 0) return null;

            if (!TryParseRequestLine(lines[0], out var method, out var target, out var httpVersion))
                return null;

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.Length == 0) continue;
                var colon = line.IndexOf(':');
                if (colon <= 0) return null;
                var name = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();
                if (name.Length == 0) return null;
                headers[name] = value;
            }

            var body = string.Empty;
            if (headers.TryGetValue("Content-Length", out var clValue)
                && int.TryParse(clValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var contentLength))
            {
                if (contentLength < 0 || contentLength > limits.MaxBodyBytes) return null;
                if (contentLength > 0)
                {
                    var bodyBytes = await ReadExactlyAsync(stream, contentLength, linked.Token).ConfigureAwait(false);
                    if (bodyBytes is null) return null;
                    body = Encoding.UTF8.GetString(bodyBytes);
                }
            }

            return new ParsedHttpRequest(method, target, httpVersion, headers, body);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (IOException) { return null; }
    }

    private static async Task<byte[]?> ReadUntilCrLfCrLfAsync(Stream stream, int cap, CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Min(4096, cap)];
        var accumulated = new MemoryStream(capacity: 1024);
        var single = new byte[1];
        var state = 0; // 0=ground, 1=saw CR, 2=saw CRLF, 3=saw CRLFCR
        while (true)
        {
            if (cancellationToken.IsCancellationRequested) return null;
            var read = await stream.ReadAsync(single.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0) return null;

            accumulated.WriteByte(single[0]);
            if (accumulated.Length > cap) return null;

            state = NextHeaderTerminatorState(state, single[0]);
            if (state == 4) // CRLFCRLF observed
            {
                // Strip the trailing CRLFCRLF from the returned buffer; the
                // parser splits on CRLF and an empty trailing line would
                // otherwise need extra handling.
                var len = (int)accumulated.Length - 4;
                if (len < 0) return null;
                var bytes = accumulated.GetBuffer();
                var result = new byte[len];
                Array.Copy(bytes, 0, result, 0, len);
                return result;
            }
        }
    }

    private static int NextHeaderTerminatorState(int state, byte b) => (state, b) switch
    {
        (0, (byte)'\r') => 1,
        (1, (byte)'\n') => 2,
        (2, (byte)'\r') => 3,
        (3, (byte)'\n') => 4,
        _ => 0,
    };

    private static List<string> SplitHeaderLines(string headerText)
    {
        // Header block has been stripped of its trailing CRLFCRLF. Lines are
        // separated by CRLF; we tolerate bare LF for robustness.
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < headerText.Length; i++)
        {
            if (headerText[i] == '\n')
            {
                var end = i;
                if (end > start && headerText[end - 1] == '\r') end--;
                lines.Add(headerText[start..end]);
                start = i + 1;
            }
        }
        if (start < headerText.Length)
            lines.Add(headerText[start..]);
        return lines;
    }

    private static bool TryParseRequestLine(string line, out string method, out string target, out string version)
    {
        method = target = version = string.Empty;
        var firstSpace = line.IndexOf(' ');
        if (firstSpace <= 0) return false;
        var secondSpace = line.IndexOf(' ', firstSpace + 1);
        if (secondSpace <= firstSpace + 1) return false;
        if (secondSpace == line.Length - 1) return false;

        method = line[..firstSpace];
        target = line[(firstSpace + 1)..secondSpace];
        version = line[(secondSpace + 1)..];
        return method.Length > 0 && target.Length > 0 && version.StartsWith("HTTP/", StringComparison.Ordinal);
    }

    private static async Task<byte[]?> ReadExactlyAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            if (cancellationToken.IsCancellationRequested) return null;
            var n = await stream.ReadAsync(buffer.AsMemory(read, count - read), cancellationToken).ConfigureAwait(false);
            if (n == 0) return null;
            read += n;
        }
        return buffer;
    }
}
