# Implementation Plan: UpnpSpy — UPnP Network Device Browser

**Branch**: `001-upnp-spy-discovery` | **Date**: 2026-05-12 (last revised 2026-05-14) | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/001-upnp-spy-discovery/spec.md`

## Summary

UpnpSpy is a Windows-only desktop diagnostic tool with a two-pane layout: a left-pane tree of live UPnP devices (populated by SSDP M-SEARCH at startup and continuous NOTIFY listening, identified by UUID, labelled by friendly name) and a right-pane chronological list of every SSDP ALIVE/BYEBYE advertisement received. The app operates on a **single user-selected network adapter** at any time (FR-048), default-binding to the first eligible IPv4 adapter at startup and switchable at runtime via a `View → Network adapter` menu; the eventing callback host is built on `System.Net.Sockets.TcpListener` (FR-049) so no `netsh http add urlacl` is ever required. Device descriptions are fetched **eagerly** on discovery (FR-043) so friendly-name labels resolve without user interaction; per-service SCPD documents are still fetched **lazily** the first time the user expands a service. Power-user interactions hang off the tree: right-click a device or service to fetch its description XML in the default browser, right-click a service to subscribe to its eventing (auto-renewed while the popup is open, unsubscribed on close), double-click an action to invoke it through a dynamic input/output popup. A View menu provides Rescan (re-run discovery and prune non-responders) and Diagnostics (in-memory ring viewer mirroring a rolling on-disk log).

The technical approach is a single MSIX-packaged WinUI 3 app on .NET 7, structured as two C# projects — `UpnpSpy.App` (XAML views and composition root) and `UpnpSpy.Core` (models, view-models, networking, diagnostics, abstractions) — with a third project `UpnpSpy.Tests` (xUnit) covering the testable surface. Networking is built directly on the BCL (`System.Net.Sockets.UdpClient` per up/non-loopback IPv4 interface for SSDP, `HttpClient` for description/SOAP/SUBSCRIBE/UNSUBSCRIBE, `HttpListener` for the GENA event callback endpoint) per Principle II so behaviour is auditable against the UPnP Device Architecture v1.0 PDF rather than mediated through a third-party UPnP library.

## Technical Context

**Language/Version**: C# 11 on .NET 7 (target framework `net7.0-windows` for the WinUI 3 app, `net7.0` for `UpnpSpy.Core` and tests). `Nullable=enable`, `TreatWarningsAsErrors=true`, latest language features enabled.

**Primary Dependencies**:

- Microsoft.WindowsAppSDK (WinUI 3 / Windows App SDK 1.x) — UI framework. Tree-node glyphs (FR-045) use the system **Segoe Fluent Icons** font shipped with Windows 10/11 (no separate icon-asset package); the XAML system resource `SymbolThemeFontFamily` resolves to it.
- CommunityToolkit.Mvvm — source-generated `ObservableObject`/`RelayCommand` for view-models.
- Microsoft.Extensions.DependencyInjection — composition root.
- Microsoft.Extensions.Configuration + Options<T> — settings binding.
- Microsoft.Extensions.Logging — structured logging facade. (Concrete sinks: an in-repo bounded rolling **JSON-lines** file sink and an in-repo ring-buffer sink, both registered behind a single `DiagnosticLoggerProvider` and fanned out via a `CompositeDiagnosticSink`; see research §14 and tasks T034–T039.)
- System.Net.Sockets, System.Net.NetworkInformation, System.Net.Http, System.Net.HttpListener — protocol I/O directly on the BCL (Principle II: no third-party UPnP library).
- xUnit + FluentAssertions + Moq — tests.

**Storage**: Local user state under `%LOCALAPPDATA%\UpnpSpy\` per Constitution. v1 writes only:

- `logs\upnpspy.log` (current) plus rotated `upnpspy.<n>.log` files (size-based rollover, ≤8 files of ≤2 MB each).
- No JSON state files in v1 (no last-seen-devices persistence, no window-layout persistence — all session state is in-memory). The path is reserved for future use.

**Testing**: xUnit, run via `dotnet test`. All default tests run without admin, without network access, and without real devices — networking, clock, HTTP, browser-launch, and event-callback hosting are reached only through interfaces (`ISsdpTransport`, `IDeviceDescriptionFetcher`, `IScpdFetcher`, `IControlClient`, `ISubscriptionClient`, `IEventCallbackHost`, `IBrowserLauncher`, `IClock`, etc.), faked in unit tests. A separate `UpnpSpy.DeviceTests` opt-in project may be added later for live-device smoke tests; it is out of scope for v1.

**Target Platform**: Windows 10 22H2 (x64, ARM64) and Windows 11 (x64, ARM64). Packaging: MSIX (per Constitution). Single-file self-contained publish allowed for developer builds.

**Project Type**: Desktop application (single Windows GUI app + supporting libraries, no server, no web, no mobile).

**Performance Goals**: Driven by spec SC-001…SC-014. Concretely:

- Tree populated within 5 s of launch on a LAN of ≤20 devices (SC-001) → M-SEARCH MX = 3 s + ≤2 s UI render budget. Friendly-name labels resolve within an additional ≤2 s thanks to the eager description fetch (FR-043).
- New SSDP advertisement reflected in right pane within 1 s of receipt (SC-009).
- Device expansion is a pure UI hydration (no HTTP) and shows children within 100 ms once the eager description fetch from FR-043 has completed; service expansion (SCPD fetch) is still lazy and shows children within 2 s on LAN (SC-004). The eager device-description fetch runs with bounded concurrency (target: 8 in flight) so a discovery burst on a large LAN does not produce an unbounded HTTP fan-out.
- Tree affordance (FR-044, FR-045, SC-015): the expand chevron is visible on every device and service node from the moment the node enters the tree. This is achieved by pre-seeding each such node's `Children` collection with a single `"Loading…"` placeholder string at view-model construction time; the placeholder is replaced atomically by the real children (or by the FR-013 inline error string) once the underlying fetch resolves. Action nodes carry no children and therefore no chevron. Each row prepends a small Segoe Fluent Icons glyph (Device / Service / Action) so node kind is identifiable without reading the label.
- Device row disambiguation + Properties window (FR-051, FR-052, SC-019): the `DeviceDescriptionXmlParser` is extended to extract the root device's full UPnP description fields (`deviceType`, `manufacturer`, `manufacturerURL`, `modelName`, `modelNumber`, `modelDescription`, `modelURL`, `serialNumber`, `UPC`, `presentationURL`) into `DeviceDescription`. The eager dispatcher copies these onto the canonical `Device` alongside the existing friendly-name + services data. SSDP-side fields (`SERVER`, `CACHE-CONTROL` max-age, `BOOTID.UPNP.ORG`, `CONFIGID.UPNP.ORG`) are propagated onto `Device` by `DiscoveryService` on every alive. `Device.FirstSeenUtc` is captured on the initial registry add; `Device.AliveCount` is incremented on every alive. The tree row template renders a two-line cell — friendly name (bold) above a muted secondary line `<deviceType-tail> · <ip:port>`. A new `DevicePropertiesViewModel` is exposed via a `DevicePropertiesPopupFactory`; right-clicking a device opens an owned `DevicePropertiesWindow` with sections for Identity / Manufacturer / Network / Discovery history / Embedded devices. Tier 2 (hover tooltip) is deferred to a future phase.
- Single-adapter operation + ACL-free eventing (FR-048/FR-049/FR-050, SC-018): the app's SSDP transport and eventing callback host both bind to one user-selected adapter at a time, default-picking the first eligible IPv4 NIC at startup. The callback host is built on `System.Net.Sockets.TcpListener` rather than `System.Net.HttpListener` — `TcpListener` uses raw BSD sockets and bypasses Windows HTTP.SYS, so binding to a non-loopback local IP needs no URL ACL grant and no Administrator privilege. UPnP NOTIFY traffic is small and well-formed in practice, so the host parses HTTP/1.1 in-process with a hand-rolled `HttpRequestReader` (request line + headers + `Content-Length`-bounded body; oversized / malformed requests are rejected with `400 Bad Request` and a Warning diagnostic; per-request read is bounded by a timeout to defend against slowloris). Adapter switching at runtime is mediated by a `NetworkAdapterSelector` singleton; on change `ShellViewModel` tears down SSDP + callback host, clears the registry, cancels in-flight fetches, notifies open popups (FR-037), then rebinds on the new adapter and re-runs startup discovery.
- Device tree visibility (FR-047, SC-017) and ordering (FR-054): the left-pane tree shows a device if and only if its `DescriptionFetchState == Loaded`. The enforcement point is `DeviceTreeViewModel`: it filters the registry snapshot at construction, ignores `DeviceAdded` events whose device is not yet Loaded, and on `DeviceUpdated` either refreshes the label of an in-tree node or — for a device that just transitioned to Loaded — promotes it into the tree. Devices in `NotFetched`, `Fetching`, or `Failed` states are absent from the tree by design; their failures are still recorded as Warning `DiagnosticEntry` items (FR-039) so the user can find them via `View → Diagnostics`. Registry membership and tree visibility are deliberately decoupled: the dispatcher, byebye handler and rescan coordinator continue to address devices by UUID regardless of tree state. Every insertion (seed, add, promote) goes through a sorted-insert helper keyed on `(Label, Uuid)` — case-insensitive on `Label`, ordinal `Uuid` tiebreak — so a discovery burst's fetch-completion race does not affect the final row order. A label change on an existing row (rare — a device re-announcing with a new friendly name) reorders via `ObservableCollection.Move` so the WinUI tree raises a single Move event and the node's selection / expansion state survives the relocation.
- SSDP log newest-first ordering (FR-003, FR-055): `SsdpLogViewModel.Append` calls `Entries.Insert(0, entry)` on the UI dispatcher and the underlying `BoundedObservableCollection<SsdpLogEntry>` is configured with `BoundedEvictionMode.EvictTail` so FR-016 still drops the oldest row when capacity is reached — that row is now at the bottom rather than the head. The view-side auto-follow in `SsdpLogView.xaml.cs` flips correspondingly: the "sticky" zone is the *top* of the scroll viewer (parked when `VerticalOffset <= StickyThresholdPx`), and on a new-entry CollectionChanged the view scrolls to `Entries[0]` rather than `Entries[^1]`. The newest-first arrangement matches the user's mental model when watching a live diagnostic stream (eyes don't have to chase down a moving tail) and the existing `BoundedEvictionMode` enum was added precisely to serve this kind of newest-first collection (already used by the subscription popup's event list per FR-033), so no new infrastructure is required.
- Secondary window ownership (FR-046, SC-016): WinUI 3 deliberately removed WPF's `Window.Owner` property, so secondary `Microsoft.UI.Xaml.Window` instances are unowned top-level OS windows by default and can be sent behind the main window by a stray focus shift. The fix is a small interop helper in `UpnpSpy.App/Platform/OwnedWindowHelper.cs` that calls `SetWindowLongPtr(GWLP_HWNDPARENT, mainWindowHwnd)` on each popup's HWND immediately after creation and before `Activate()`. The main window's HWND is published via `MainWindowHandleProvider` (a DI-managed singleton; `MainWindow` initializes it during construction using `WinRT.Interop.WindowNative.GetWindowHandle(this)`). Owned-popup behaviour is z-order + lifetime only — popups remain independently activatable so the user can still interact with the main window.
- Invocation popup interactive within 1 s of action double-click (SC-010); result shown within 2 s after submit when device answers in <1 s (SC-011).
- UI thread MUST NOT block under any of the above paths (Principle III).

**Constraints**:

- Standard (non-elevated) user only — no Administrator privileges.
- All network I/O async; `.Result`/`.Wait()` forbidden (Principle III).
- Bounded memory: SSDP log capped at 10,000 entries FIFO (FR-016); in-memory diagnostic ring bounded (target: 5,000 entries); on-disk log rolling-bounded (≤16 MB total).
- Bounded HTTP fan-out: eager device-description fetches (FR-043) are gated by a single shared `SemaphoreSlim` (target: 8 concurrent) so a single discovery burst across many devices does not saturate the host or the upstream link.
- IPv4 only for v1 (spec Assumptions). SSDP multicast group `239.255.255.250:1900`. Joined on every up, non-loopback IPv4 interface (FR-004).
- Event callback HTTP listener binds to `http://+:<ephemeral>/upnpspy/<token>/` on the local host; the URL announced to each device on SUBSCRIBE uses the per-interface local IP so the device can reach back over the same link it was discovered on.
- One public type per file; files target <200 lines, justified if >400 (Principle IV).

**Scale/Scope**: Single-user desktop tool. Expected device counts: 1–50 on a typical LAN; SSDP message rate: a few per minute steady-state, bursts during M-SEARCH replies. Memory ceiling target: <200 MB resident for an 8-hour session with 20 devices, 5 open subscription popups, and a saturated SSDP log.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase-0 evaluation

| Principle | Compliance plan | Verdict |
|---|---|---|
| **I. Specification First (NON-NEGOTIABLE)** | `specs/001-upnp-spy-discovery/spec.md` exists with FR-001…FR-042 and SC-001…SC-014 already authored and clarified (Session 2026-05-12). Every task generated by `/speckit-tasks` from this plan will carry the matching FR-### tag(s). No implementation code is written before `/speckit-tasks` and `/speckit-implement` run. | PASS |
| **II. UPnP Device Architecture v1.0 is authoritative** | All protocol decisions are sourced from `docs/specs/UPnP-arch-DeviceArchitecture-v1.0-20080424.pdf` and cited in `research.md` (Phase 0) with section/page references. No third-party UPnP library is adopted; SSDP/description/SOAP/GENA are implemented on `System.Net.*` primitives. `NEEDS CLARIFICATION` markers remain in the spec only if a question is genuinely uncovered by the PDF. | PASS |
| **III. Windows Desktop Best Practices** | .NET 7; nullable enable + warnings-as-errors; async/await end-to-end with `CancellationToken` plumbing; MVVM via `CommunityToolkit.Mvvm` source generators; DI via `Microsoft.Extensions.DependencyInjection`; logging via `Microsoft.Extensions.Logging`; central package management via `Directory.Packages.props`; checked-in `.editorconfig`. UI thread never blocked. | PASS |
| **IV. Clean, focused, testable source files** | One public type per file; target <200 LOC; out-of-process collaborators (sockets, HTTP, file system, browser, clock, callback host) accessed only via interfaces and injected via DI. No static mutable state. View-models live in `UpnpSpy.Core` so tests never instantiate views. xUnit happy-path + failure-path tests for each non-trivial behaviour. | PASS |

No violations identified at pre-Phase-0. **Complexity Tracking table is empty.**

### Post-Phase-1 re-evaluation

Re-evaluated after `research.md`, `data-model.md`, `contracts/`, and `quickstart.md` were written. No new violations introduced; the design's three-project layout, BCL-only networking, and per-interface multi-socket discovery all remain within the Constitution's envelope. **Complexity Tracking table remains empty.**

## Project Structure

### Documentation (this feature)

```text
specs/001-upnp-spy-discovery/
├── plan.md              # This file (/speckit-plan command output)
├── spec.md              # Feature specification (already exists)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (interface contracts for the abstractions)
│   ├── README.md
│   ├── ISsdpTransport.md
│   ├── IDeviceDescriptionFetcher.md
│   ├── IControlClient.md
│   ├── ISubscriptionClient.md
│   ├── IEventCallbackHost.md
│   ├── IBrowserLauncher.md
│   ├── IDiagnosticSink.md
│   └── IClock.md
└── tasks.md             # Phase 2 output (/speckit-tasks command — NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
UpnpSpy.sln
Directory.Build.props          # Nullable=enable, TreatWarningsAsErrors=true, ImplicitUsings=enable
Directory.Packages.props       # Central package versions
.editorconfig                  # Style rules
NuGet.config                   # Restore sources

src/
├── UpnpSpy.Core/                                  # No UI dependencies; fully unit-testable
│   ├── UpnpSpy.Core.csproj                        # TargetFramework: net7.0
│   ├── Models/
│   │   ├── Device.cs                              # Identity: UUID; FR-007, FR-009, FR-010, FR-043
│   │   ├── Service.cs                             # FR-011, FR-018
│   │   ├── ActionDefinition.cs                    # FR-012
│   │   ├── ArgumentDefinition.cs                  # FR-026
│   │   ├── SsdpLogEntry.cs                        # FR-014, FR-015, FR-016
│   │   ├── DiscoverySession.cs                    # FR-004, FR-021-024
│   │   ├── InvocationRequest.cs                   # FR-027
│   │   ├── InvocationResult.cs                    # FR-028, FR-029, FR-030
│   │   ├── SubscriptionState.cs                   # FR-032-036, FR-038
│   │   ├── EventNotification.cs                   # FR-033
│   │   └── DiagnosticEntry.cs                     # FR-039
│   ├── Ssdp/
│   │   ├── ISsdpTransport.cs                      # Abstraction over UDP/multicast
│   │   ├── MulticastSsdpTransport.cs              # FR-004 (per-NIC sockets)
│   │   ├── SsdpMessageParser.cs                   # Edge case: malformed message ignored
│   │   ├── SsdpNotifyMessage.cs
│   │   ├── SsdpSearchResponse.cs
│   │   └── SsdpListener.cs                        # Orchestrates per-NIC sockets + parser
│   ├── Description/
│   │   ├── IDeviceDescriptionFetcher.cs           # FR-011, FR-013
│   │   ├── DeviceDescriptionFetcher.cs            # HttpClient-backed
│   │   ├── IScpdFetcher.cs                        # FR-012, FR-013
│   │   ├── ScpdFetcher.cs
│   │   ├── DeviceDescriptionXmlParser.cs
│   │   └── ScpdXmlParser.cs
│   ├── Control/
│   │   ├── IControlClient.cs                      # FR-025-031
│   │   ├── ControlClient.cs                       # SOAP envelope builder + sender
│   │   ├── SoapEnvelopeBuilder.cs
│   │   └── SoapFaultParser.cs                     # FR-029
│   ├── Eventing/
│   │   ├── ISubscriptionClient.cs                 # FR-032-036, FR-038
│   │   ├── SubscriptionClient.cs                  # SUBSCRIBE / RENEW / UNSUBSCRIBE
│   │   ├── IEventCallbackHost.cs                  # FR-033
│   │   ├── TcpListenerEventCallbackHost.cs        # FR-049 (BCL-only, no URL ACL)
│   │   ├── HttpRequestReader.cs                   # FR-049 (HTTP/1.1 NOTIFY parser)
│   │   ├── SubscriptionRenewalScheduler.cs        # FR-038 (auto-renew)
│   │   └── GenaNotifyParser.cs
│   ├── Discovery/
│   │   ├── DeviceRegistry.cs                      # FR-005, FR-007, FR-008 (UUID-keyed)
│   │   ├── DiscoveryService.cs                    # FR-004, FR-006, FR-021-024
│   │   ├── EagerDescriptionDispatcher.cs          # FR-043 (bounded-parallel on-add fetch)
│   │   └── RescanCoordinator.cs                   # FR-023
│   ├── Diagnostics/
│   │   ├── IDiagnosticSink.cs                     # FR-039
│   │   ├── IDiagnosticBuffer.cs                   # FR-041 (in-memory ring)
│   │   ├── RingDiagnosticBuffer.cs
│   │   ├── RollingFileDiagnosticSink.cs           # FR-040 (size-bounded rotation)
│   │   ├── DiagnosticLoggerProvider.cs            # M.E.L. bridge
│   │   └── DiagnosticEnricher.cs
│   ├── Platform/
│   │   ├── IBrowserLauncher.cs                    # FR-019, FR-020
│   │   ├── IClock.cs
│   │   ├── INetworkInterfaceEnumerator.cs         # FR-004
│   │   ├── INetworkAdapterSelector.cs             # FR-048 (single-NIC selection)
│   │   └── NetworkAdapterSelector.cs              # FR-048 (BCL-only)
│   ├── ViewModels/
│   │   ├── ShellViewModel.cs                      # FR-001 (two-pane shell)
│   │   ├── DeviceTreeViewModel.cs                 # FR-002, FR-005-013, FR-043
│   │   ├── SsdpLogViewModel.cs                    # FR-003, FR-014-016
│   │   ├── DeviceNodeViewModel.cs                 # FR-017 (context menu)
│   │   ├── ServiceNodeViewModel.cs                # FR-018
│   │   ├── ActionNodeViewModel.cs                 # FR-025
│   │   ├── InvocationPopupViewModel.cs            # FR-025-031, FR-037
│   │   ├── SubscriptionPopupViewModel.cs          # FR-032-038, FR-037
│   │   ├── DevicePropertiesViewModel.cs           # FR-052
│   │   ├── DevicePropertiesPopupFactory.cs        # FR-052
│   │   └── DiagnosticsViewerViewModel.cs          # FR-041
│   └── Composition/
│       └── CoreServiceCollectionExtensions.cs     # AddUpnpSpyCore(...) DI registration
│
└── UpnpSpy.App/                                   # WinUI 3 host
    ├── UpnpSpy.App.csproj                         # TargetFramework: net7.0-windows
    ├── Package.appxmanifest                       # MSIX manifest
    ├── App.xaml(.cs)                              # Composition root: builds DI container
    ├── MainWindow.xaml(.cs)                       # Hosts the two-pane Shell view
    ├── Views/
    │   ├── ShellView.xaml(.cs)
    │   ├── DeviceTreeView.xaml(.cs)
    │   ├── SsdpLogView.xaml(.cs)
    │   ├── InvocationPopup.xaml(.cs)
    │   ├── SubscriptionPopup.xaml(.cs)
    │   ├── DevicePropertiesWindow.xaml(.cs)            # FR-052
    │   └── DiagnosticsWindow.xaml(.cs)
    ├── Converters/                                # XAML value converters (kept tiny)
    ├── Resources/                                 # Styles, brushes, theme dictionaries
    └── Platform/
        ├── DefaultBrowserLauncher.cs              # Windows.System.Launcher.LaunchUriAsync
        ├── SystemClock.cs
        ├── NetworkInterfaceEnumerator.cs
        ├── MainWindowHandleProvider.cs            # FR-046 (holds main window HWND)
        └── OwnedWindowHelper.cs                   # FR-046 (SetWindowLongPtr interop)

tests/
└── UpnpSpy.Tests/                                 # xUnit; no network, no admin, no real devices
    ├── UpnpSpy.Tests.csproj                       # TargetFramework: net7.0
    ├── Ssdp/
    │   ├── SsdpMessageParserTests.cs              # FR-014/015 + malformed-message edge case
    │   └── DeviceRegistryTests.cs                 # FR-005/007/008
    ├── Description/
    │   ├── DeviceDescriptionXmlParserTests.cs     # FR-011 + missing-friendlyName fallback (FR-010)
    │   └── ScpdXmlParserTests.cs                  # FR-012
    ├── Control/
    │   ├── SoapEnvelopeBuilderTests.cs            # FR-027
    │   └── SoapFaultParserTests.cs                # FR-029
    ├── Eventing/
    │   ├── SubscriptionRenewalSchedulerTests.cs   # FR-038
    │   └── GenaNotifyParserTests.cs               # FR-033
    ├── Discovery/
    │   ├── DiscoveryServiceTests.cs               # FR-004/006
    │   ├── EagerDescriptionDispatcherTests.cs     # FR-043 (bounded parallelism, cancel on byebye)
    │   └── RescanCoordinatorTests.cs              # FR-021-024
    ├── Diagnostics/
    │   ├── RingDiagnosticBufferTests.cs           # FR-041 (bounded ring)
    │   └── RollingFileDiagnosticSinkTests.cs      # FR-040 (rotation, fail-open)
    └── ViewModels/
        ├── DeviceTreeViewModelTests.cs            # FR-002/005-013
        ├── SsdpLogViewModelTests.cs               # FR-016 (FIFO eviction at 10,000)
        ├── InvocationPopupViewModelTests.cs       # FR-025-031, FR-037
        ├── SubscriptionPopupViewModelTests.cs     # FR-032-038, FR-037
        └── DiagnosticsViewerViewModelTests.cs     # FR-041
```

**Structure Decision**: Three-project solution — `src/UpnpSpy.Core` (no UI deps, holds models, view-models, networking, diagnostics, abstractions); `src/UpnpSpy.App` (WinUI 3 host: XAML views, MSIX manifest, platform adapters); `tests/UpnpSpy.Tests` (xUnit). View-models live in `UpnpSpy.Core` so they can be tested without instantiating any view (Principle IV). All out-of-process collaborators (UDP, HTTP, HTTP listener, browser launcher, clock, NIC enumerator) are reached through interfaces in `UpnpSpy.Core/Platform`, `UpnpSpy.Core/Ssdp`, etc., and faked in tests. The MSIX package, signing, and Windows App SDK references live only in `UpnpSpy.App` so the rest of the solution remains plain `net7.0` and testable on standard CI runners.

## Complexity Tracking

> No Constitution Check violations. This section is intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| _(none)_ | _(n/a)_ | _(n/a)_ |
