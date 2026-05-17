using UpnpSpy.Core.Description;

namespace UpnpSpy.Tests.Description;

internal sealed class FakeScpdFetcher : IScpdFetcher
{
    private readonly Dictionary<Uri, ScpdFetchResult> _results = new();
    private readonly Dictionary<Uri, int> _calls = new();

    public ScpdFetchResult Default { get; set; } =
        new ScpdFetchResult.TransportError("no canned result", null);

    public void SetResult(Uri url, ScpdFetchResult result) => _results[url] = result;

    public int CallsFor(Uri url) => _calls.TryGetValue(url, out var n) ? n : 0;

    public Task<ScpdFetchResult> FetchAsync(Uri scpdUrl, CancellationToken cancellationToken)
    {
        _calls[scpdUrl] = CallsFor(scpdUrl) + 1;
        var result = _results.TryGetValue(scpdUrl, out var r) ? r : Default;
        return Task.FromResult(result);
    }
}
