using UpnpSpy.Core.Description;

namespace UpnpSpy.Tests.Description;

/// <summary>
/// Test double for <see cref="IDeviceDescriptionFetcher"/> with per-URL canned
/// results and call-count tracking (verifies laziness — a node should fetch
/// once on first expansion, never on subsequent expansions).
/// </summary>
internal sealed class FakeDeviceDescriptionFetcher : IDeviceDescriptionFetcher
{
    private readonly Dictionary<Uri, DeviceDescriptionFetchResult> _results = new();
    private readonly Dictionary<Uri, int> _calls = new();
    private TaskCompletionSource? _gate;

    public DeviceDescriptionFetchResult Default { get; set; } =
        new DeviceDescriptionFetchResult.TransportError("no canned result", null);

    public void SetResult(Uri url, DeviceDescriptionFetchResult result) => _results[url] = result;

    public int CallsFor(Uri url) => _calls.TryGetValue(url, out var n) ? n : 0;

    /// <summary>Attaches a gate; <see cref="FetchAsync"/> awaits until <see cref="Release"/>.</summary>
    public void HoldNextFetch()
    {
        _gate = new TaskCompletionSource();
    }

    public void Release() => _gate?.TrySetResult();

    public async Task<DeviceDescriptionFetchResult> FetchAsync(Uri locationUrl, CancellationToken cancellationToken)
    {
        _calls[locationUrl] = CallsFor(locationUrl) + 1;
        if (_gate is { } gate)
        {
            using var registration = cancellationToken.Register(() => gate.TrySetCanceled(cancellationToken));
            await gate.Task.ConfigureAwait(false);
        }
        return _results.TryGetValue(locationUrl, out var result) ? result : Default;
    }
}
