# Contract: `IBrowserLauncher`

**Namespace**: `UpnpSpy.Core.Platform`
**Lifetime**: Singleton
**Spec FR**: FR-019, FR-020

A one-method abstraction over "open this URL in the user's default browser." Exists so that the right-click → Fetch XML / Fetch service XML flow can be unit-tested without launching a browser.

## C# signature

```csharp
public interface IBrowserLauncher
{
    // Opens 'url' in the user's default browser. Returns true if the system reports
    // the URI was launched, false if it could not be (no association, etc.).
    // Does not throw for ordinary "no browser" failure — returns false instead.
    Task<bool> OpenAsync(Uri url, CancellationToken cancellationToken);
}
```

## Behavioural requirements

- The production implementation (`DefaultBrowserLauncher`) calls `Windows.System.Launcher.LaunchUriAsync(url)` and returns its `bool` result. No URL rewriting — the device's LOCATION / SCPDURL is passed verbatim.
- The method completes after the OS hands off to the chosen browser. It does not wait for the browser to render the page.
- On exception (e.g., COM failure from the launcher API), record `Warning` `DiagnosticEntry` (`Category=App.Lifecycle`, with `url`) and return `false`.

## Test seam

`FakeBrowserLauncher` records each call in a `List<Uri>` and returns a test-configured `bool`.

## Notes

- The launcher must not be replaced by `Process.Start("cmd", "/c start <url>")` or similar shell-out: in a packaged WinUI 3 app `LaunchUriAsync` is the documented path and respects per-user default-browser settings without spawning a transient `cmd.exe` window.
