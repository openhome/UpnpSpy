# Internal Contracts — UpnpSpy

**Feature**: 001-upnp-spy-discovery
**Date**: 2026-05-12

UpnpSpy is a single-user desktop tool; it does not expose a public API or a server endpoint. The "contracts" documented here are the **internal abstraction boundaries** between view-models / domain services and every out-of-process collaborator. They exist so that:

1. View-models and domain services in `UpnpSpy.Core` can be unit-tested without touching real sockets, the file system, the clock, the user's browser, or the WinUI dispatcher — satisfying **Constitution Principle IV**.
2. The Windows-specific surface area is confined to `UpnpSpy.App/Platform/` (and a handful of types inside `UpnpSpy.Core` whose Windows-only behaviour is local and replaceable, like `MulticastSsdpTransport` and `HttpListenerEventCallbackHost`).
3. Future changes (e.g., swapping `HttpListener` for Kestrel, swapping `Windows.System.Launcher` for `Process.Start`, or adding cross-platform support) happen behind a stable seam.

Each file in this directory documents one contract: the C# signature, the inputs/outputs, the failure modes the implementation must surface (vs. swallow), and the spec FR-### it serves.

| Contract | Implementations (production) | Implementations (test) | Spec coverage |
|---|---|---|---|
| [`ISsdpTransport`](./ISsdpTransport.md) | `MulticastSsdpTransport` | `FakeSsdpTransport` | FR-004, FR-006, FR-014, FR-015 |
| [`IDeviceDescriptionFetcher`](./IDeviceDescriptionFetcher.md) | `DeviceDescriptionFetcher` (HttpClient) | `FakeDeviceDescriptionFetcher` | FR-011, FR-013 |
| [`IControlClient`](./IControlClient.md) | `ControlClient` (HttpClient + SOAP) | `FakeControlClient` | FR-025–FR-031 |
| [`ISubscriptionClient`](./ISubscriptionClient.md) | `SubscriptionClient` (HttpClient + GENA) | `FakeSubscriptionClient` | FR-032–FR-038 |
| [`IEventCallbackHost`](./IEventCallbackHost.md) | `HttpListenerEventCallbackHost` | `FakeEventCallbackHost` | FR-033 |
| [`IBrowserLauncher`](./IBrowserLauncher.md) | `DefaultBrowserLauncher` (Windows.System.Launcher) | `FakeBrowserLauncher` | FR-019, FR-020 |
| [`IDiagnosticSink`](./IDiagnosticSink.md) | `RollingFileDiagnosticSink` + `RingDiagnosticBuffer` | in-memory test buffer | FR-039–FR-042 |
| [`IClock`](./IClock.md) | `SystemClock` | `FakeClock` | FR-038 (timeouts), SC-003/009/010/011 |

All contracts:

- Are **async-first**: every operation that involves I/O accepts a `CancellationToken` (Constitution Principle III).
- Are **exception-poor**: predictable failures (network unreachable, HTTP error, parse error) surface as result objects, not exceptions. Exceptions propagate only for truly unexpected programmer errors.
- Are registered as **singletons** in DI unless explicitly noted as transient.

Two non-I/O abstractions are also in scope but documented inline (small, mechanical):

- `IDispatcher`: marshals view-model updates to the WinUI `DispatcherQueue`. Faked synchronously in tests.
- `INetworkInterfaceEnumerator`: returns the up, non-loopback IPv4 interfaces. Faked with a fixed list in tests.
