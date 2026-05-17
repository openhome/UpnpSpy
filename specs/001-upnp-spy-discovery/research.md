# Phase 0 Research — UpnpSpy

**Feature**: 001-upnp-spy-discovery
**Date**: 2026-05-12
**Authoritative protocol source (per Constitution Principle II)**: `docs/specs/UPnP-arch-DeviceArchitecture-v1.0-20080424.pdf` ("UDA 1.0").

This document resolves every NEEDS CLARIFICATION question that surfaced while filling the Technical Context of `plan.md`. Each decision lists what was chosen, why, and what alternatives were rejected. Protocol decisions cite the relevant UDA 1.0 section. (The spec itself has already been through `/speckit-clarify`; the platform-level clarifications recorded there — Windows-only, all IPv4 interfaces, auto-renew subscriptions, 10,000-entry SSDP cap, dual rolling-file + in-memory diagnostic log — are not re-litigated here.)

---

## 1. UI framework: WinUI 3 vs WPF

- **Decision**: WinUI 3 (Windows App SDK, current GA 1.x).
- **Rationale**: Constitution Principle III names WinUI 3 as the preferred framework and requires a documented "blocking limitation" to fall back to WPF. None of UpnpSpy's UI requirements (a `TreeView` with right-click context menus and lazy expansion, a virtualizing `ListView` for the SSDP log, multiple secondary windows for invocation/subscription/diagnostics popups, default-browser launching) hit any known WinUI 3 blocker:
  - `Microsoft.UI.Xaml.Controls.TreeView` supports hierarchical data binding, expand-on-demand via the `Expanding` event, and `MenuFlyout`-based context menus.
  - Multi-window: `Microsoft.UI.Xaml.Window` instances can be created from any thread that has a DispatcherQueue; popups are real OS windows.
  - Default browser: `Windows.System.Launcher.LaunchUriAsync(Uri)` is available unchanged in WinUI 3.
- **Alternatives considered**:
  - **WPF**: more mature, simpler MSIX-less debugging, but the Constitution forbids the choice without a blocker. None exists.
  - **WinForms**: explicitly out of scope by Constitution.
  - **Avalonia / cross-platform**: contradicts Constitution's Windows-only target and IPv4-multicast-on-NetworkInterface assumption.

---

## 2. Target framework and language

- **Decision**:
  - `UpnpSpy.Core` and `UpnpSpy.Tests`: `TargetFramework=net7.0`.
  - `UpnpSpy.App` (WinUI 3 host): `TargetFramework=net7.0-windows` with `TargetPlatformMinVersion=10.0.17763.0` (Windows 10 1809).
  - Language: C# 11 (the language version that ships with .NET 7 SDK).
  - `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in `Directory.Build.props` (applies to every project).
  - `<ImplicitUsings>enable</ImplicitUsings>`.
- **Rationale**: .NET 7 is the runtime mandated by the Constitution's lower bound and is supported by the Windows App SDK 1.x line. Pinning `Core`/`Tests` to plain `net7.0` (not `net7.0-windows`) keeps them runnable on any standard CI image and prevents Windows-only types from leaking into the testable code path.
- **Alternatives considered**: A unified `net7.0-windows` target was rejected because it would couple `UpnpSpy.Core` to the Windows runtime identifier set and to Windows-only APIs, weakening Principle IV testability and preventing future cross-platform reuse of the protocol code.

---

## 3. Central package management & build hardening

- **Decision**: A repo-root `Directory.Packages.props` enables central package management (`ManagePackageVersionsCentrally=true`); each project declares `<PackageReference>` without `Version`. A repo-root `Directory.Build.props` sets nullable, warnings-as-errors, deterministic build, and `LangVersion=latest`. A repo-root `.editorconfig` enforces style.
- **Rationale**: Required by Constitution Principle III bullet 7. Centralisation guarantees the App and Core projects never reference different versions of `Microsoft.Extensions.*` or the toolkit.
- **Alternatives considered**: Per-project versions (rejected — Constitution mandates central management).

---

## 4. Dependency injection composition

- **Decision**: `Microsoft.Extensions.DependencyInjection`. `UpnpSpy.Core` exposes a single `AddUpnpSpyCore(this IServiceCollection, IConfiguration)` extension that registers every service, view-model, parser, and abstraction. `UpnpSpy.App` is the composition root: `App.xaml.cs` builds the `IServiceProvider`, calls `AddUpnpSpyCore`, then registers the Windows-only adapters (`DefaultBrowserLauncher`, `SystemClock`, `NetworkInterfaceEnumerator`, `HttpListenerEventCallbackHost`).
- **Rationale**: Constitution Principle III bullet 5 requires it. Splitting registration into a Core extension and an App-side composition root keeps Core platform-agnostic and lets tests construct a minimal service provider with fakes.
- **Alternatives considered**: Property/setter injection or static service locator — rejected (Principle IV "no static mutable state").

---

## 5. MVVM toolkit

- **Decision**: `CommunityToolkit.Mvvm` (latest 8.x). Use `[ObservableProperty]` and `[RelayCommand]` source generators on every view-model in `UpnpSpy.Core/ViewModels`. Code-behind is restricted to view-only concerns (e.g., `ListView` auto-scroll behavior) per Constitution.
- **Rationale**: Constitution Principle III bullet 4 names this library specifically.
- **Alternatives considered**: Prism (heavier, more concepts than needed); hand-rolled `INotifyPropertyChanged` (more boilerplate, no measurable advantage).

---

## 6. SSDP: M-SEARCH on every up, non-loopback IPv4 interface (FR-004)

- **Decision**: Enumerate `NetworkInterface.GetAllNetworkInterfaces()`, filter to `OperationalStatus == Up && !IsLoopback && SupportsMulticast && Supports(NetworkInterfaceComponent.IPv4)`. For each surviving interface, look up its unicast IPv4 address and create a dedicated `UdpClient` bound to `(localIPv4, 0)`. Each `UdpClient`:
  - Calls `JoinMulticastGroup(IPAddress.Parse("239.255.255.250"), localIPv4)` on its own interface.
  - Sends the M-SEARCH datagram to `239.255.255.250:1900`.
  - Loops `ReceiveAsync()` to capture both unicast M-SEARCH responses (UDA 1.0 §1.2.2) and multicast NOTIFY advertisements (UDA 1.0 §1.1, §1.2).
- **M-SEARCH payload** (UDA 1.0 §1.2.1):

  ```text
  M-SEARCH * HTTP/1.1
  HOST: 239.255.255.250:1900
  MAN: "ssdp:discover"
  MX: 3
  ST: ssdp:all
  USER-AGENT: UpnpSpy/1.0 Windows/10
  ```

  - `MX: 3` — middle of the 1–5 second range UDA 1.0 §1.2.1 mandates. Three seconds satisfies SC-001's 5 s budget with 2 s headroom for tree-render.
  - `ST: ssdp:all` — yields a response per (device, service) pair; deduplication by UUID happens upstream (FR-007). Matches the spec's "every UPnP device on the LAN" requirement; a narrower `upnp:rootdevice` would skip services advertised independently.
  - `MAN` value is double-quoted per UDA 1.0 §1.3 (quotes are mandatory; some stacks silently drop unquoted MAN).
- **Per-NIC receive loop**: each socket's loop dispatches every received datagram into a single shared `Channel<RawSsdpDatagram>` consumed by `SsdpMessageParser` → `DeviceRegistry`. Identity is still the UUID (UDA 1.0 §1.1.4 USN field), so a device reachable on two interfaces appears once in the tree.
- **Rescan** (FR-021–FR-024): the same per-NIC sockets stay live for the whole session; `RescanCoordinator` issues a fresh M-SEARCH burst on each socket, opens a `DiscoverySession` (MX-bounded window), records each unique UUID heard, and at the MX deadline removes any device that was in the registry at session-start but whose UUID was not heard during the window. Unsolicited NOTIFY processing continues throughout (FR-024).
- **Alternatives considered**:
  - Single dual-bound `UdpClient` on `IPAddress.Any` — rejected: the routing stack picks one interface for outbound M-SEARCH, missing devices on the others (root cause of "I see my router but not my NAS" bug reports). Also fails to join the multicast group on every interface.
  - A third-party UPnP library (e.g., RSSDP, OpenSource.UPnP, intel-tools-upnp) — rejected per Constitution Principle II (would require an ADR proving conformance to the same PDF we already cite).

---

## 7. SSDP: NOTIFY advertisement handling

- **Decision**: Parse `NOTIFY * HTTP/1.1` datagrams against UDA 1.0 §1.1 / §1.3:
  - `NT` carries the search target (e.g., `upnp:rootdevice`, `urn:schemas-upnp-org:service:AVTransport:1`).
  - `NTS` selects the kind: `ssdp:alive` (FR-014) or `ssdp:byebye` (FR-015). `ssdp:update` is UDA 1.1 and out of scope.
  - `USN` (Unique Service Name) carries the UUID (`uuid:<UUID>::<NT>`); the parser extracts the bare UUID. **This UUID is the device identity** (FR-007) and the value logged in the SSDP log entry (FR-014/015).
  - `LOCATION` (on alive) carries the device description URL; on byebye it is absent.
- **Edge case** (spec User Story 4 acceptance #4): if the parser cannot extract a UUID (truncated/malformed), the datagram is dropped silently for UI purposes and recorded as a `Warning` `DiagnosticEntry` (FR-039).
- **Rationale**: Direct from UDA 1.0 §1.1.2 (alive), §1.1.3 (byebye), §1.3 (header definitions). Citing the PDF rather than ad-hoc parsing keeps Principle II satisfied.

---

## 8. SSDP log capacity and FIFO eviction (FR-016)

- **Decision**: `SsdpLogViewModel` owns an `ObservableCollection<SsdpLogEntry>` wrapped in a small `BoundedObservableCollection` helper that, on each `Add`, removes index 0 when `Count == 10_000` before appending. Insertion and removal are funneled through the UI thread via `DispatcherQueue.TryEnqueue` so the WinUI `ListView` binding stays consistent.
- **Rationale**: 10,000 entries × ~80 bytes ≈ 0.8 MB worst case, well within SC-013 memory ceiling. FIFO matches spec phrasing "oldest discarded first" (FR-016 / Edge Cases / Assumptions).
- **Alternatives considered**: A virtualizing data source backed by a circular array (premature; 10k items is small enough that an `ObservableCollection` with virtualization in the view performs fine).

---

## 9. Device description fetch (FR-011, FR-013, FR-043)

- **Decision** *(revised 2026-05-14 per Clarifications Session 2026-05-14)*: A single shared `HttpClient` instance (per Constitution Principle III bullet 5) registered as singleton in DI, with `Timeout = 5 s` and a `Handler` that follows redirects. `DeviceDescriptionFetcher.FetchAsync(Uri, CancellationToken)` issues an `HTTP GET` against the `LOCATION` URL (UDA 1.0 §2.4) and pipes the response body through `DeviceDescriptionXmlParser`. **Eager, not lazy**: an `EagerDescriptionDispatcher` subscribes to `DeviceRegistry.DeviceAdded` and enqueues a fetch whenever a fresh `Device` enters the registry. The dispatcher gates concurrent fetches through a single shared `SemaphoreSlim(initialCount=8)` so a discovery burst on a 50-device LAN produces at most 8 concurrent HTTP requests rather than one per device. Each fetch is cancelled (via a per-device linked `CancellationTokenSource`) if a byebye or rescan-prune removes the device while the fetch is in flight. The successful description's `FriendlyName` is written back to `Device.FriendlyName` and its parsed services to `Device.Services`, the device is marked `DescriptionFetchState=Loaded`, and the registry raises `DeviceUpdated` so the tree label refreshes. Result is cached on the in-memory `Device` instance for the lifetime of that registry entry; subsequent alive advertisements for the same UUID do **not** trigger a re-fetch.
- **Failure handling** (FR-013): a fetch or parse failure leaves `Device.FriendlyName=null` (so `Device.Label` keeps the `"uuid:<uuid>"` FR-010 fallback), flips `DescriptionFetchState=Failed`, stores the human-readable reason in `Device.DescriptionFetchError`, and emits a `Warning` `DiagnosticEntry` (Category=`Description.Fetch` or `Description.Parse`). The device itself stays in the tree (its existence was confirmed by SSDP). The failure is **not** shown as an inline tree-child at this point — only when the user expands the failed node does `DeviceNodeViewModel` surface the existing `"⚠ Services unavailable: <reason>"` placeholder, preserving the FR-013 UX for users who haven't tried to drill in.
- **Tree expansion (FR-011)**: `DeviceNodeViewModel.ExpandAsync` now operates on cached state rather than performing an HTTP fetch. Three cases:
  - `DescriptionFetchState == Loaded`: hydrate `Children` from `Device.Services` synchronously on the UI thread (no I/O).
  - `DescriptionFetchState == Fetching`: insert a transient `"Loading…"` placeholder child and subscribe to `DeviceRegistry.DeviceUpdated` for this UUID so the placeholder is replaced when the eager fetch lands (or replaced by the FR-013 inline error on failure).
  - `DescriptionFetchState == Failed`: show the `"⚠ Services unavailable: <reason>"` inline placeholder (FR-013).
- **Rationale**: HTTP/1.1 GET against `LOCATION` is verbatim UDA 1.0 §2.4. Resolving friendly names without user interaction matches the user's mental model of a UPnP browser (the spec's User Story 1 talks about devices appearing "labelled by their human-readable friendly name"). The original "lazy on first expand" decision conflicted with that, since the label stayed at `uuid:…` until the user clicked the chevron. Bounded parallelism keeps the cost predictable: 8 × 5 s = ~5 s worst-case to clear a 8-device burst, ~13 s for 20 devices — well within the additional 2 s budget added to SC-001 for typical-size descriptions on a LAN where most fetches complete in well under a second.
- **Alternatives considered**:
  - **Stay lazy** (previous decision): rejected because the tree label is the device's primary visual identifier (FR-009) and forcing a user click to resolve it undermines User Story 1.
  - **Eager but unbounded parallelism**: rejected as a thundering-herd risk against routers/NASes that respond to multiple search targets and against the host's own TCP-connection budget.
  - **Eager device description + eager SCPD**: rejected — SCPD fetches are per service (one root device with 5 services means 5 extra fetches), and most users never open most services. Lazy SCPD keeps the on-discovery cost roughly proportional to device count, not service count.

---

## 10. SCPD fetch (FR-012, FR-013)

- **Decision**: Same shape as §9 but against the service's `SCPDURL`, parsed by `ScpdXmlParser` into ordered `ActionDefinition` records (each carrying ordered input/output `ArgumentDefinition` lists). Lazy: triggered only on first `TreeViewItem.Expanding` for the service node. Cached per `Service`. Same failure UX as §9.
- **Rationale**: UDA 1.0 §2.2 (service description) and §2.4 (retrieval). Input/output ordering matters because the SOAP envelope must list arguments in SCPD-declared order (UDA 1.0 §3.1.1).

---

## 11. Action invocation (FR-025–FR-031)

- **Decision**: `ControlClient.InvokeAsync(Service service, ActionDefinition action, IReadOnlyDictionary<string, string> inputs, CancellationToken)`:
  1. Builds a SOAP envelope per UDA 1.0 §3.1.1: `POST <service.ControlURL> HTTP/1.1`, headers `HOST`, `CONTENT-LENGTH`, `CONTENT-TYPE: text/xml; charset="utf-8"`, `USER-AGENT`, `SOAPACTION: "<serviceType>#<actionName>"` (quotes mandatory), body containing a `<s:Envelope>` with the `<u:<actionName> xmlns:u="<serviceType>">` block and one child element per input argument in SCPD-declared order.
  2. On HTTP 200 with a SOAP body: parses the response envelope (UDA 1.0 §3.1.2) into a dictionary of output-argument names → string values, returns an `InvocationResult.Success(outputs)`.
  3. On HTTP 500 with a SOAP fault (UDA 1.0 §3.1.3): parses `<UPnPError><errorCode/><errorDescription/></UPnPError>` and returns `InvocationResult.UpnpFault(httpStatus: 500, errorCode, errorDescription, rawFaultXml)`. (FR-029).
  4. On transport error (timeout, unreachable, DNS failure): returns `InvocationResult.TransportError(message, exception)`. (FR-030).
- **Empty-input action** (FR-031): step 1 produces an envelope whose action body element is self-closing.
- **No-output action** (FR-031): step 2 returns `InvocationResult.Success(empty)`; the popup shows "Succeeded (no output values)".
- **Free-form text inputs** (spec Assumptions): the popup binds each input to a `TextBox`; v1 does not validate against SCPD `allowedValueList` or `allowedValueRange` — the device's fault becomes the validation.
- **Rationale**: Direct mapping of UDA 1.0 §3.1.1–§3.1.3.

---

## 12. Eventing: subscribe / renew / unsubscribe / callback host (FR-032–FR-038)

- **Decision**:
  - **Callback host**: a single process-wide `HttpListener` instance, registered as a singleton `IEventCallbackHost`, started on `App` launch. Binds to `http://+:0/upnpspy/` (ephemeral port; `+` accepts any host header, but per-subscription URLs use a per-NIC local IP so the device reaches back over the same link it advertised on). For each subscription a unique opaque token is appended (`http://<localIp>:<port>/upnpspy/<guid>/`) and registered with a dispatcher that maps inbound NOTIFY to the right `SubscriptionState`. `HttpListener` does not require admin if a URL ACL is reserved at install time; for unpackaged developer builds, the App falls back to `127.0.0.1` plus a documented `netsh http add urlacl` step (out of scope of v1 user docs; the MSIX install handles it for shipped builds).
  - **SUBSCRIBE** (UDA 1.0 §4.1.1): `SubscriptionClient` issues `SUBSCRIBE <service.EventSubURL> HTTP/1.1` with `CALLBACK: <http://<localIp>:<port>/upnpspy/<guid>/>` (angle brackets mandatory), `NT: upnp:event`, and `TIMEOUT: Second-1800` (1800 s = 30 min, the conventional ceiling — devices may grant less). The server's `SID` and granted `TIMEOUT` are stored on `SubscriptionState`. The initial event burst (UDA 1.0 §4.1.2) and every subsequent NOTIFY (UDA 1.0 §4.3) are pushed into the popup's scrolling list.
  - **Renewal** (FR-038, UDA 1.0 §4.1.3): `SubscriptionRenewalScheduler` (one timer per active subscription) fires at `granted_timeout − 30 s`. It re-issues `SUBSCRIBE` with only the `SID` and a fresh `TIMEOUT` (no `CALLBACK`/`NT`). Continues as long as the popup is open. A 4xx/5xx response or transport failure marks the subscription as lapsed: the popup shows an inline message, the scheduler stops, and a `Warning` `DiagnosticEntry` is recorded (FR-038, FR-039). Once lapsed, closing the popup MUST NOT send UNSUBSCRIBE (FR-038).
  - **UNSUBSCRIBE** (UDA 1.0 §4.1.4, FR-034): on popup close while the subscription is still active, send `UNSUBSCRIBE <service.EventSubURL> HTTP/1.1` with `SID: <sid>`. Best-effort; transport failure logs to diagnostics and is otherwise swallowed (the popup is already closing).
- **Subscription-failed path** (FR-035): if the initial SUBSCRIBE returns a non-200 status or fails at the transport layer, no `SubscriptionState` is created, the popup shows the failure, and closing the popup does not invoke UNSUBSCRIBE.
- **Device-disappears path** (FR-037): the `DeviceRegistry`'s byebye handler raises an event the popup view-models subscribe to; the popup transitions to an "underlying device left the network" state, stops the renewal scheduler, and closes cleanly without UNSUBSCRIBE.
- **Rationale**: `HttpListener` is BCL-provided (Principle II / no third-party dependency), runs without admin once a URL ACL is in place (MSIX handles ACL provisioning), and is sufficient for an HTTP/1.1 receive-only server. The 30-s-before-timeout renewal margin tolerates clock skew and one missed cycle without dropping events. The angle-bracket `CALLBACK` syntax is a UDA 1.0 §4.1.1 requirement frequently violated by ad-hoc implementations and worth calling out.
- **Alternatives considered**:
  - **Kestrel / ASP.NET Core minimal API** — rejected: drags in a large dependency surface for a single GENA NOTIFY route, and requires more configuration to bind to multiple per-NIC IPs.
  - **Single global callback URL on `127.0.0.1`** — rejected: devices on the LAN cannot reach `127.0.0.1` on the host, so events would never arrive.

---

## 13. Default browser launch (FR-019, FR-020)

- **Decision**: `Windows.System.Launcher.LaunchUriAsync(new Uri(descriptionUrl))`. Injected through `IBrowserLauncher` so tests don't open a browser.
- **Rationale**: Documented Microsoft-recommended API for launching the user's default browser from a WinUI 3/UWP-style app; works in MSIX-packaged apps without elevation.
- **Alternatives considered**: `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })` — works but is less ergonomic in WinUI 3's packaged context and harder to fake cleanly.

---

## 14. Diagnostic logging: dual sink (FR-039–FR-042)

- **Decision**:
  - One `DiagnosticEntry` record carries `Timestamp`, `Severity` (`Trace`/`Information`/`Warning`/`Error`), `Category` (event class, e.g., `Ssdp.Parse`, `Description.Fetch`, `Control.Soap`, `Eventing.Subscribe`, `Eventing.Renew`, `Eventing.Unsubscribe`), `Message`, and an optional `Context` dictionary (device UUID, service id, action name, URL, HTTP status, error text — whatever the call site has).
  - Two sinks registered as separate `ILoggerProvider`s behind `Microsoft.Extensions.Logging`:
    1. `RollingFileDiagnosticSink` writes JSON-lines to `%LOCALAPPDATA%\UpnpSpy\logs\upnpspy.log`. Size-based rollover: at 2 MB the current file rotates to `upnpspy.1.log`, older files shift up, anything past `upnpspy.7.log` is deleted (≤8 files, ≤16 MB total).
    2. `RingDiagnosticBuffer` (a thread-safe ring of 5,000 entries) backs the `View > Diagnostics` window via `IDiagnosticBuffer.Subscribe(IObserver<DiagnosticEntry>)`; the viewer view-model subscribes on open and unsubscribes on close (FR-041).
  - **Fail-open** (FR-042): if the rolling file cannot be opened (path locked, disk full), the sink swallows the error after a single user-visible warning toast, and the in-memory buffer continues to function.
  - **Off-UI-thread** (FR-042): both sinks accept entries via a non-blocking `Channel<DiagnosticEntry>` consumed by a dedicated background task, so call sites never await disk or queue contention.
- **Rationale**: Keeps the third-party logging surface to zero (`Microsoft.Extensions.Logging` abstractions are already in our dependency set for Principle III), satisfies "bounded, rolling, dual" diagnostic requirement exactly as clarified on 2026-05-12.
- **Alternatives considered**:
  - **Serilog** — feature-complete and battle-tested, but the constitution favours BCL/M.E.* primitives and the dual-sink requirement is small enough that a custom 100-LOC provider keeps the dependency surface lean.
  - **EventLog (Windows)** — wrong audience: requires elevated install, not surfaced inside the app.

---

## 15. Packaging and signing

- **Decision**: MSIX, single architecture-neutral bundle producing per-architecture packages (x64, ARM64). `UpnpSpy.App` carries `Package.appxmanifest`; build profile produces an `.msix` per arch via `dotnet publish -r win-x64`/`-r win-arm64` with `WindowsPackageType=MSIX`. Code signing uses a developer certificate for local builds and is hooked up to a release pipeline (out of scope for v1 implementation tasks; the manifest, capabilities, and assets are in scope).
- **Rationale**: Constitution mandates MSIX; ARM64 support is explicit ("Target OS: Windows 10 22H2 and Windows 11 (x64, ARM64)").
- **Alternatives considered**: ClickOnce, plain `.exe` installer — both rejected by Constitution.

---

## 16. Cancellation discipline

- **Decision**: A single `CancellationTokenSource` owned by `App` is signalled on `App.Exiting`. Long-lived loops (`SsdpListener.RunAsync`, `HttpListenerEventCallbackHost.RunAsync`, `SubscriptionRenewalScheduler` timers) take that token. Per-operation cancellation tokens (HTTP fetches, action invocations, individual SUBSCRIBE/UNSUBSCRIBE) are created via `CancellationTokenSource.CreateLinkedTokenSource(appShutdown, perOpTimeout)`. Closing a popup cancels its scope without affecting the app token.
- **Rationale**: Constitution Principle III bullet 5: "Cancellation MUST be plumbed end-to-end."

---

## 17. UI threading model

- **Decision**: Every view-model property mutation that is observable from XAML is performed on the `DispatcherQueue` of the owning window. Background components push updates via `IDispatcher.RunOnUi(Action)` (a tiny wrapper around `DispatcherQueue.TryEnqueue`). `IDispatcher` is faked in tests (synchronous executor) so view-model tests are deterministic.
- **Rationale**: WinUI 3 marshalling rules + Constitution "UI thread MUST never be blocked"; the abstraction keeps view-models pure for testing per Principle IV.

---

## 18. M-SEARCH timing details

- **Decision**: MX = 3 seconds in every M-SEARCH the app sends (startup and rescan). The discovery session waits MX + 1 s grace (= 4 s) before pruning non-responders on a rescan; this absorbs jitter and the device-side "random delay 0…MX" rule from UDA 1.0 §1.2.1.
- **Rationale**: SC-001 budgets 5 s for "tree populated"; 4 s session leaves ~1 s for view rendering.

---

## 19. Friendly-name fallback (FR-010)

- **Decision**: When the device description XML has no `<friendlyName>` (or it parses to empty/whitespace), the tree label becomes `uuid:<uuid>`. The device's `Device.Label` property exposes this fallback so view-models don't replicate the logic.
- **Rationale**: Spec Edge Cases ("Device with no friendly name") + FR-010. Same UUID is also the device's identity, so the label remains unique.

---

## 20. Embedded devices: services flattened under root

- **Decision**: When parsing a device description, the parser recursively walks every `<deviceList>` under the root `<device>` and emits one `Service` per `<service>` found at any depth. All services — root and nested — are appended to the root `Device.Services` collection (data-model §1). The embedded devices themselves are **not** materialised as separate tree nodes, consistent with FR-002's three-level tree (Device → Service → Action).
- **Per-service provenance**: Each emitted `Service` records the UDN and friendly name of the device that declared it (`ContainingDeviceUdn`, `ContainingDeviceFriendlyName`). When the containing device is not the root, the service `Label` is prefixed with the embedded device's friendly name (or UDN fallback) so two embedded children that each expose, say, `AVTransport:1` can be told apart in the tree.
- **Identity & collisions**: Service identity within a root device becomes `(ContainingDeviceUdn, ServiceId)`. The SCPD-declared `ServiceId` and `ServiceType` are preserved verbatim on the model for use when constructing SOAP envelopes (UDA 1.0 §3.1.1) and SUBSCRIBE URLs (UDA 1.0 §4.1.1) — the embedded provenance is purely for tree display and identity disambiguation. The parser logs a `Warning` `Description.Parse` diagnostic if two `<service>` elements share both `ContainingDeviceUdn` and `ServiceId` (UDA 1.0 §2.1 implicitly forbids this within a single `<device>` element) and keeps the first occurrence.
- **URL resolution**: `SCPDURL`, `controlURL`, and `eventSubURL` for embedded-device services resolve against the root description's effective base URI (the response's `Content-Location` or the request URL), exactly as for root-device services — UPnP descriptions ship URLs that are either absolute or relative to the description document, not to the containing `<device>` element (UDA 1.0 §2.1).
- **Rationale**: Matches spec Assumptions ("the root device from each advertisement appears in the tree; the services it exposes include the union of its own `<serviceList>` and every `<serviceList>` declared in its embedded children, walked recursively"). Keeping the three-level tree from FR-002 means embedded-device services merge into the root's `Services` list; the label prefix prevents same-typed services across embedded children from being indistinguishable.

---

## Summary of resolved unknowns

| Question | Resolution | Source |
|---|---|---|
| UI framework | WinUI 3 | Constitution III + no blocker |
| Target framework | net7.0 + net7.0-windows | Constitution III; WinUI 3 baseline |
| MX value for M-SEARCH | 3 s | UDA 1.0 §1.2.1; fits SC-001 |
| ST value for M-SEARCH | `ssdp:all` | Spec "every UPnP device"; UDA 1.0 §1.3 |
| Multicast group join | Per-interface, every up non-loopback IPv4 NIC | FR-004 + spec Clarifications |
| Device description fetch timing | **Eager**, on registry add (bounded parallelism = 8) | Spec FR-043 + Clarifications 2026-05-14 |
| SCPD (service description) fetch timing | **Lazy**, on first service-node expansion | Spec Assumptions |
| Action invocation transport | SOAP/HTTP per UDA 1.0 §3.1.1 | UDA 1.0 §3.1 |
| Eventing callback host | `System.Net.HttpListener` | Principle II (no third-party) |
| Subscription renewal margin | granted_timeout − 30 s | Engineering buffer for clock skew |
| Default browser launch | `Windows.System.Launcher.LaunchUriAsync` | Microsoft-recommended |
| Diagnostic sinks | Rolling file (≤16 MB) + in-memory ring (5,000) | FR-039–FR-042 |
| Packaging | MSIX, x64 + ARM64 | Constitution III |

No unresolved `NEEDS CLARIFICATION` items remain. Phase 1 may proceed.
