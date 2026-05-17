using UpnpSpy.Core.Platform;

namespace UpnpSpy.Tests.TestHelpers;

/// <summary>
/// Records each call and returns a configurable result. Mirrors the seam
/// described in contracts/IBrowserLauncher.md.
/// </summary>
public sealed class FakeBrowserLauncher : IBrowserLauncher
{
    private readonly List<Uri> _calls = new();

    public bool NextResult { get; set; } = true;

    public IReadOnlyList<Uri> Calls => _calls;

    public Task<bool> OpenAsync(Uri url, CancellationToken cancellationToken)
    {
        _calls.Add(url);
        return Task.FromResult(NextResult);
    }
}
