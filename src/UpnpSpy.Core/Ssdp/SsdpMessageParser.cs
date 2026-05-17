using System.Text;

namespace UpnpSpy.Core.Ssdp;

/// <summary>
/// Parses NOTIFY and M-SEARCH response datagrams per UDA 1.0 §1.1 / §1.2 / §1.3.
/// Returns null on any malformed input — caller logs a Warning and drops the datagram.
/// </summary>
public sealed class SsdpMessageParser
{
    public ParsedSsdpMessage? Parse(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty) return null;

        string text;
        try { text = Encoding.UTF8.GetString(payload.Span); }
        catch { return null; }

        var separators = new[] { "\r\n", "\n" };
        var lines = text.Split(separators, StringSplitOptions.None);
        if (lines.Length == 0) return null;

        var firstLine = lines[0].Trim();
        if (firstLine.Length == 0) return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (name.Length == 0) continue;
            headers[name] = value;
        }

        if (!headers.TryGetValue("USN", out var usn) || string.IsNullOrWhiteSpace(usn))
            return null;
        var uuid = ExtractUuid(usn);
        if (uuid is null) return null;

        if (firstLine.StartsWith("NOTIFY ", StringComparison.OrdinalIgnoreCase))
            return ParseNotify(uuid, usn, headers);

        if (firstLine.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
            return ParseSearchResponse(firstLine, uuid, usn, headers);

        return null;
    }

    public static string? ExtractUuid(string usn)
    {
        if (string.IsNullOrWhiteSpace(usn)) return null;
        if (!usn.StartsWith("uuid:", StringComparison.OrdinalIgnoreCase))
            return null;

        var rest = usn[5..];
        var sep = rest.IndexOf("::", StringComparison.Ordinal);
        var uuid = sep >= 0 ? rest[..sep] : rest;
        uuid = uuid.Trim();
        return uuid.Length == 0 ? null : uuid;
    }

    private static SsdpNotifyMessage ParseNotify(string uuid, string usn, IDictionary<string, string> headers)
    {
        headers.TryGetValue("NT", out var nt);
        headers.TryGetValue("NTS", out var nts);
        headers.TryGetValue("LOCATION", out var loc);
        headers.TryGetValue("SERVER", out var server);
        headers.TryGetValue("CACHE-CONTROL", out var cc);
        headers.TryGetValue("BOOTID.UPNP.ORG", out var bootIdRaw);
        headers.TryGetValue("CONFIGID.UPNP.ORG", out var configIdRaw);

        Uri? location = null;
        if (!string.IsNullOrWhiteSpace(loc))
            Uri.TryCreate(loc, UriKind.Absolute, out location);

        return new SsdpNotifyMessage(
            Uuid: uuid,
            Usn: usn,
            Nts: nts ?? string.Empty,
            Nt: nt ?? string.Empty,
            Location: location,
            Server: server,
            CacheControlMaxAge: ParseMaxAge(cc),
            BootId: TryParseInt(bootIdRaw),
            ConfigId: TryParseInt(configIdRaw));
    }

    private static SsdpSearchResponse? ParseSearchResponse(string firstLine, string uuid, string usn, IDictionary<string, string> headers)
    {
        // First line shape: "HTTP/1.1 200 OK"
        var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        if (!int.TryParse(parts[1], out var status) || status != 200) return null;

        headers.TryGetValue("ST", out var st);
        headers.TryGetValue("LOCATION", out var loc);
        headers.TryGetValue("SERVER", out var server);
        headers.TryGetValue("CACHE-CONTROL", out var cc);

        if (string.IsNullOrWhiteSpace(loc) || !Uri.TryCreate(loc, UriKind.Absolute, out var location))
            return null;

        return new SsdpSearchResponse(
            Uuid: uuid,
            Usn: usn,
            St: st ?? string.Empty,
            Location: location,
            Server: server,
            CacheControlMaxAge: ParseMaxAge(cc));
    }

    private static int? ParseMaxAge(string? cacheControl)
    {
        if (string.IsNullOrWhiteSpace(cacheControl)) return null;
        foreach (var raw in cacheControl.Split(','))
        {
            var token = raw.Trim();
            if (!token.StartsWith("max-age", StringComparison.OrdinalIgnoreCase)) continue;
            var eq = token.IndexOf('=');
            if (eq < 0) continue;
            if (int.TryParse(token[(eq + 1)..].Trim(), out var seconds))
                return seconds;
        }
        return null;
    }

    private static int? TryParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return int.TryParse(value, out var i) ? i : null;
    }
}
