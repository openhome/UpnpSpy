namespace UpnpSpy.Core.Models;

public sealed class DiscoverySession
{
    public required DateTimeOffset StartedUtc { get; init; }
    public required DateTimeOffset Deadline { get; init; }
    public required bool IsStartupSession { get; init; }
    public required IReadOnlySet<string> KnownAtStart { get; init; }

    public HashSet<string> HeardThisSession { get; } = new(StringComparer.Ordinal);
    public DiscoverySessionState State { get; set; } = DiscoverySessionState.Running;
}
