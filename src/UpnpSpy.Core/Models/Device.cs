using UpnpSpy.Core.Description;

namespace UpnpSpy.Core.Models;

public sealed class Device
{
    public required string Uuid { get; init; }
    public string? FriendlyName { get; set; }
    public required Uri LocationUrl { get; set; }

    public FetchState DescriptionFetchState { get; set; } = FetchState.NotFetched;
    public string? DescriptionFetchError { get; set; }
    public IReadOnlyList<Service> Services { get; set; } = Array.Empty<Service>();

    // Description-derived metadata (FR-051, FR-052; populated by the eager
    // dispatcher on success and used by the tree row's detail line and the
    // Properties window).
    public string? DeviceType { get; set; }
    public string? Manufacturer { get; set; }
    public Uri? ManufacturerUrl { get; set; }
    public string? ModelName { get; set; }
    public string? ModelDescription { get; set; }
    public string? ModelNumber { get; set; }
    public Uri? ModelUrl { get; set; }
    public string? SerialNumber { get; set; }
    public string? Upc { get; set; }
    public Uri? PresentationUrl { get; set; }
    public IReadOnlyList<EmbeddedDeviceSummary> EmbeddedDevices { get; set; } =
        Array.Empty<EmbeddedDeviceSummary>();

    // SSDP-side metadata (FR-052; refreshed by DiscoveryService on each alive).
    public string? ServerHeader { get; set; }
    public int? CacheControlMaxAge { get; set; }
    public int? BootId { get; set; }
    public int? ConfigId { get; set; }

    // Lifecycle bookkeeping.
    public DateTimeOffset FirstSeenUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
    public long AliveCount { get; set; }
    public IReadOnlySet<string> ObservedOnInterfaces { get; set; } = new HashSet<string>(StringComparer.Ordinal);

    public string Label =>
        !string.IsNullOrWhiteSpace(FriendlyName) ? FriendlyName! : "uuid:" + Uuid;

    /// <summary>
    /// FR-051: muted secondary line shown beneath <see cref="Label"/> so
    /// co-resident root devices with the same friendly name are visually
    /// distinct. Format: <c>"&lt;deviceType-tail&gt; · &lt;host&gt;:&lt;port&gt;"</c>.
    /// Falls back gracefully when either component is missing.
    /// </summary>
    public string DetailLabel
    {
        get
        {
            var typeTail = DeviceTypeTail();
            var endpoint = HostPort();
            return (typeTail, endpoint) switch
            {
                ({ Length: > 0 } t, { Length: > 0 } e) => $"{t} · {e}",
                ({ Length: > 0 } t, _) => t,
                (_, { Length: > 0 } e) => e,
                _ => string.Empty,
            };
        }
    }

    private string DeviceTypeTail()
    {
        if (string.IsNullOrWhiteSpace(DeviceType)) return string.Empty;
        const string Marker = ":device:";
        var idx = DeviceType!.IndexOf(Marker, StringComparison.Ordinal);
        if (idx < 0) return DeviceType.Trim();
        var tail = DeviceType[(idx + Marker.Length)..];
        // Strip the version suffix (":1", ":2"…) — purely visual noise.
        var colon = tail.IndexOf(':');
        return colon >= 0 ? tail[..colon] : tail;
    }

    private string HostPort()
    {
        if (LocationUrl is null) return string.Empty;
        var port = LocationUrl.IsDefaultPort ? string.Empty : ":" + LocationUrl.Port;
        return string.IsNullOrEmpty(LocationUrl.Host) ? string.Empty : LocationUrl.Host + port;
    }
}
