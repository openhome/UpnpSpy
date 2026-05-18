---
description: "Task list for feature 001-upnp-spy-discovery (UpnpSpy — UPnP Network Device Browser)"
---

# Tasks: UpnpSpy — UPnP Network Device Browser

**Input**: Design documents from `/specs/001-upnp-spy-discovery/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included. The plan enumerates a complete `UpnpSpy.Tests` xUnit project with named test files per non-trivial component; those test files are first-class deliverables (Constitution Principle IV — "happy-path + failure-path tests for each non-trivial behaviour"). Tests are listed alongside the production code in each phase below.

**Organization**: Tasks are grouped by user story (US1…US8 from spec.md). Each user-story phase delivers an independently testable increment as described in its **Independent Test** block in spec.md.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete tasks).
- **[Story]**: User story label (US1…US8). Only set on user-story phase tasks; Setup, Foundational, and Polish carry no story label.
- Each description includes the exact relative file path.

## Path Conventions (per plan.md §"Project Structure")

- Repository root: `C:\work\UpnpSpy\`.
- Production code: `src/UpnpSpy.Core/` (no UI deps) and `src/UpnpSpy.App/` (WinUI 3 host).
- Tests: `tests/UpnpSpy.Tests/`.
- All paths below are repo-relative.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Bring up the empty three-project solution with the build-hardening files mandated by Constitution Principle III. No domain code yet.

- [X] T001 Create solution file at `UpnpSpy.sln` referencing the three projects to be added below
- [X] T002 [P] Add repo-root `Directory.Build.props` with `Nullable=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`, `ImplicitUsings=enable`, deterministic build (`Deterministic=true`, `ContinuousIntegrationBuild` opt-in)
- [X] T003 [P] Add repo-root `Directory.Packages.props` enabling `ManagePackageVersionsCentrally=true` and pinning versions for `Microsoft.WindowsAppSDK`, `CommunityToolkit.Mvvm` (8.x), `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Configuration.Binder`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Logging.Abstractions`, `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `FluentAssertions`, `Moq`
- [X] T004 [P] Add repo-root `.editorconfig` with C# style rules (4-space indent, file-scoped namespaces, var-when-apparent, ordered usings) and turn `dotnet_diagnostic.IDE0073.severity=error` on for the `file_header_template`
- [X] T005 [P] Add repo-root `NuGet.config` pinning `https://api.nuget.org/v3/index.json` as the only restore source
- [X] T006 Create `src/UpnpSpy.Core/UpnpSpy.Core.csproj` with `TargetFramework=net7.0` and references to `CommunityToolkit.Mvvm`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Configuration.Abstractions`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging.Abstractions`
- [X] T007 Create `src/UpnpSpy.App/UpnpSpy.App.csproj` with `TargetFramework=net7.0-windows`, `UseWinUI=true`, `WindowsPackageType=MSIX`, `EnableMsixTooling=true`, project reference to `UpnpSpy.Core`, package references to `Microsoft.WindowsAppSDK`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Configuration.Binder`, `Microsoft.Extensions.Logging`
- [X] T008 Create `tests/UpnpSpy.Tests/UpnpSpy.Tests.csproj` with `TargetFramework=net7.0`, `IsPackable=false`, project reference to `UpnpSpy.Core`, package references to `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `FluentAssertions`, `Moq`
- [X] T009 Create `src/UpnpSpy.App/Package.appxmanifest` with display name "UpnpSpy", a publisher placeholder, package architecture entries for x64 and ARM64, and the `runFullTrust` / `privateNetworkClientServer` capabilities required to bind `HttpListener` and emit multicast UDP under MSIX
- [X] T010 Create `src/UpnpSpy.App/App.xaml` and `src/UpnpSpy.App/App.xaml.cs` with a minimal WinUI 3 `Application` subclass that exits cleanly (the DI composition root is added in T060 in Foundational)
- [X] T011 Create `src/UpnpSpy.App/MainWindow.xaml` and `src/UpnpSpy.App/MainWindow.xaml.cs` with an empty `Window` so `dotnet run` succeeds

**Checkpoint**: `dotnet restore && dotnet build && dotnet test` against an empty solution all succeed; `dotnet run --project src\UpnpSpy.App\UpnpSpy.App.csproj` opens an empty window.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain models, cross-cutting abstractions (clock, dispatcher, NICs, HTTP), diagnostic infrastructure, BoundedObservableCollection, DI registration, App composition root. Every user story below depends on these.

**CRITICAL**: No user-story work may begin until this phase is complete.

### Domain models and enums (data-model.md §1–§12)

- [X] T012 [P] Create `src/UpnpSpy.Core/Models/FetchState.cs` enum `{ NotFetched, Fetching, Loaded, Failed }` (data-model §1, §2)
- [X] T013 [P] Create `src/UpnpSpy.Core/Models/ArgumentDirection.cs` enum `{ In, Out }` (data-model §4)
- [X] T014 [P] Create `src/UpnpSpy.Core/Models/ArgumentDefinition.cs` record with `Name`, `Direction`, `RelatedStateVariable`, `DataType?` (data-model §4)
- [X] T015 [P] Create `src/UpnpSpy.Core/Models/StateVariableDefinition.cs` record with `Name`, `DataType`, `SendsEvents`, `AllowedValues?` (data-model §5)
- [X] T016 [P] Create `src/UpnpSpy.Core/Models/ActionDefinition.cs` record with `Name`, ordered `Inputs`, ordered `Outputs` (data-model §3)
- [X] T017 [P] Create `src/UpnpSpy.Core/Models/Service.cs` mutable model with `OwningDeviceUuid` (root device UUID), `ContainingDeviceUdn`, `ContainingDeviceFriendlyName?`, `ServiceId`, `ServiceType`, `ScpdUrl`, `ControlUrl`, `EventSubUrl`, computed `Label` (root: `ServiceType` tail; embedded child: prefixed with containing device's friendly name/UDN), `ScpdFetchState`, `ScpdFetchError`, `Actions`, `StateVariables` (data-model §2; research §20)
- [X] T018 [P] Create `src/UpnpSpy.Core/Models/Device.cs` mutable model with `Uuid`, `FriendlyName?`, computed `Label`, `LocationUrl`, `DescriptionFetchState`, `DescriptionFetchError`, `Services`, `LastSeenUtc`, `ObservedOnInterfaces` (data-model §1, FR-009/FR-010 fallback to `"uuid:" + Uuid`)
- [X] T019 [P] Create `src/UpnpSpy.Core/Models/SsdpKind.cs` enum `{ Alive, Byebye }` and `src/UpnpSpy.Core/Models/SsdpLogEntry.cs` record with `ReceivedUtc`, `Kind`, `DeviceUuid`, `Nt`, `SourceInterfaceName` (data-model §6, FR-014/FR-015)
- [X] T020 [P] Create `src/UpnpSpy.Core/Models/DiscoverySessionState.cs` enum `{ Running, Completed, Superseded }` and `src/UpnpSpy.Core/Models/DiscoverySession.cs` mutable model with `StartedUtc`, `Deadline`, `IsStartupSession`, `KnownAtStart`, `HeardThisSession`, `State` (data-model §7)
- [X] T021 [P] Create `src/UpnpSpy.Core/Models/InvocationRequest.cs` record with `Service`, `Action`, `Inputs` dictionary, `SubmittedUtc`, `Cancellation` (data-model §8)
- [X] T022 [P] Create `src/UpnpSpy.Core/Models/InvocationResult.cs` discriminated record union `{ Success(outputs), UpnpFault(httpStatus, errorCode, errorDescription, rawFaultXml), TransportError(message, exception?) }` with `CompletedUtc` (data-model §9)
- [X] T023 [P] Create `src/UpnpSpy.Core/Models/SubscriptionStatus.cs` enum `{ Pending, Active, Lapsed, Failed, Closed }` and `src/UpnpSpy.Core/Models/SubscriptionState.cs` mutable model with `Service`, `Sid`, `CallbackUrl`, `GrantedTimeout`, `RenewalDueUtc`, `Status`, `FailureReason?`, `Events`, `CreatedUtc` (data-model §10)
- [X] T024 [P] Create `src/UpnpSpy.Core/Models/EventNotification.cs` record with `ReceivedUtc`, `SequenceNumber`, `Properties` dictionary, `RawXml?` (data-model §11)
- [X] T025 [P] Create `src/UpnpSpy.Core/Models/DiagnosticSeverity.cs` enum `{ Trace, Information, Warning, Error }` and `src/UpnpSpy.Core/Models/DiagnosticEntry.cs` record with `Timestamp`, `Severity`, `Category`, `Message`, `Context` dictionary, `Exception?` (data-model §12)

### Shared collection helper

- [X] T026 [P] Create `src/UpnpSpy.Core/Collections/BoundedObservableCollection.cs` — `ObservableCollection<T>` subclass that on `Add` removes index 0 when `Count == capacity`, raising standard `INotifyCollectionChanged` notifications (data-model §13, research §8)
- [X] T027 [P] Create `tests/UpnpSpy.Tests/Collections/BoundedObservableCollectionTests.cs` covering: capacity respected; FIFO eviction; `CollectionChanged` raised for both removal and addition; large-volume stress

### Clock, dispatcher, NIC enumerator (contracts: IClock, IDispatcher, INetworkInterfaceEnumerator)

- [X] T028 [P] Create `src/UpnpSpy.Core/Platform/IClock.cs` interface returning `DateTimeOffset UtcNow` (contracts/IClock.md)
- [X] T029 [P] Create `src/UpnpSpy.App/Platform/SystemClock.cs` implementation returning `DateTimeOffset.UtcNow`
- [X] T030 [P] Create `src/UpnpSpy.Core/Platform/IDispatcher.cs` interface with `Task RunOnUiAsync(Action)` and `void Post(Action)` (research §17)
- [X] T031 [P] Create `src/UpnpSpy.App/Platform/WinUiDispatcher.cs` adapter over the main window's `DispatcherQueue.TryEnqueue`
- [X] T032 [P] Create `src/UpnpSpy.Core/Platform/INetworkInterfaceEnumerator.cs` interface returning `IReadOnlyList<EligibleInterface>` with fields `(string Name, IPAddress Ipv4Address)`
- [X] T033 [P] Create `src/UpnpSpy.App/Platform/NetworkInterfaceEnumerator.cs` implementation enumerating `NetworkInterface.GetAllNetworkInterfaces()` filtered to `OperationalStatus == Up && !IsLoopback && SupportsMulticast && Supports(IPv4)` (research §6; FR-004)

### Diagnostic logging infrastructure (FR-039–FR-042)

- [X] T034 [P] Create `src/UpnpSpy.Core/Diagnostics/IDiagnosticSink.cs` and `src/UpnpSpy.Core/Diagnostics/IDiagnosticBuffer.cs` interfaces (contracts/IDiagnosticSink.md)
- [X] T035 [P] Create `src/UpnpSpy.Core/Diagnostics/RingDiagnosticBuffer.cs` — thread-safe fixed-capacity (default 5,000) ring with `Snapshot()` and `Subscribe(IObserver<DiagnosticEntry>)` (data-model §12, research §14)
- [X] T036 [P] Create `src/UpnpSpy.Core/Diagnostics/CompositeDiagnosticSink.cs` fanning each `Record` out to every inner sink without blocking the caller
- [X] T037 [P] Create `src/UpnpSpy.Core/Diagnostics/IFileSystem.cs` thin in-repo abstraction (`OpenAppend`, `Exists`, `Move`, `Delete`, `CreateDirectory`, `Length`) and `src/UpnpSpy.App/Platform/SystemFileSystem.cs` adapter — enables file-system injection for rotation tests
- [X] T038 Create `src/UpnpSpy.Core/Diagnostics/RollingFileDiagnosticSink.cs` writing JSON-lines to `%LOCALAPPDATA%\UpnpSpy\logs\upnpspy.log`, rotating at 2 MB to `upnpspy.<n>.log`, keeping ≤8 files, fail-open per FR-042 (depends on T037)
- [X] T039 [P] Create `src/UpnpSpy.Core/Diagnostics/DiagnosticLoggerProvider.cs` `ILoggerProvider` that bridges `Microsoft.Extensions.Logging.ILogger` calls into `IDiagnosticSink.Record(DiagnosticEntry)`
- [X] T040 [P] Create `tests/UpnpSpy.Tests/Diagnostics/RingDiagnosticBufferTests.cs` covering: capacity, FIFO eviction (FR-041), `Snapshot` returns immutable list, `Subscribe` sees only entries after subscription, multi-threaded write safety
- [X] T041 [P] Create `tests/UpnpSpy.Tests/Diagnostics/RollingFileDiagnosticSinkTests.cs` using an in-memory `IFileSystem` fake covering: writes one JSON-line per entry; rotates at 2 MB (FR-040); evicts past 8 files; fail-open on disk error doesn't throw (FR-042)
- [X] T042 [P] Create `tests/UpnpSpy.Tests/Diagnostics/CompositeDiagnosticSinkTests.cs` covering: every wrapped sink receives every entry; one failing sink does not prevent others from receiving

### HTTP client & cancellation discipline

- [X] T043 [P] Create `src/UpnpSpy.Core/Net/HttpClientFactory.cs` exposing `CreateShared()` returning a singleton `HttpClient` with `Timeout = TimeSpan.FromSeconds(5)`, `SocketsHttpHandler` with `PooledConnectionLifetime = 2 min`, redirects enabled (research §9, Constitution III bullet 5)
- [X] T044 [P] Create `src/UpnpSpy.Core/Lifecycle/AppShutdownTokenSource.cs` singleton wrapping a `CancellationTokenSource` that `App.OnClosed` cancels (research §16)

### DI composition

- [X] T045 Create `src/UpnpSpy.Core/Composition/CoreServiceCollectionExtensions.cs` with `AddUpnpSpyCore(this IServiceCollection, IConfiguration)` registering every interface from T028/T030/T032/T034/T043/T044 plus the diagnostic ring/composite/file sink (singletons), the `ILoggerProvider`, and view-models added in later phases — leave platform adapters (`SystemClock`, `WinUiDispatcher`, `NetworkInterfaceEnumerator`, `SystemFileSystem`) to the App-side composition root
- [X] T046 Update `src/UpnpSpy.App/App.xaml.cs` to build an `IServiceProvider`, call `AddUpnpSpyCore`, register the Windows-only adapters (`SystemClock`, `WinUiDispatcher`, `NetworkInterfaceEnumerator`, `SystemFileSystem`), and store the provider on `App.Services`
- [X] T047 Update `src/UpnpSpy.App/MainWindow.xaml.cs` so that `OnClosed` cancels `AppShutdownTokenSource` (T044) and disposes the `IServiceProvider`

**Checkpoint**: Solution builds; all foundational tests pass; the empty app still launches and shuts down cleanly; no domain behaviour yet.

---

## Phase 3: User Story 1 — See the UPnP devices on my network at startup (Priority: P1) 🎯 MVP

**Goal**: Launching the app probes the LAN via SSDP M-SEARCH and shows one tree entry per unique device, labelled by friendly name (FR-001, FR-002, FR-004, FR-005, FR-007, FR-009, FR-010, SC-001, SC-002, SC-008).

**Independent Test**: Run `dotnet run`; with at least one known UPnP device on the LAN, its friendly name appears in the left-pane tree within ~5 seconds. With no devices on the LAN, the tree is empty and the app does not crash.

### SSDP transport and parsing (contracts/ISsdpTransport.md, research §6, §7)

- [X] T048 [US1] Create `src/UpnpSpy.Core/Ssdp/ReceivedSsdpDatagram.cs` record `(ReceivedUtc, InterfaceName, RemoteEndpoint, Payload)`
- [X] T049 [US1] Create `src/UpnpSpy.Core/Ssdp/ISsdpTransport.cs` interface per contracts/ISsdpTransport.md
- [X] T050 [US1] Create `src/UpnpSpy.Core/Ssdp/SsdpNotifyMessage.cs` record `(Nts, Nt, Usn, Location?, Server?, CacheControlMaxAge?, BootId?, ConfigId?)` and `src/UpnpSpy.Core/Ssdp/SsdpSearchResponse.cs` record `(St, Usn, Location, Server?, CacheControlMaxAge?)`
- [X] T051 [US1] Create `src/UpnpSpy.Core/Ssdp/SsdpMessageParser.cs` parsing both NOTIFY and HTTP/1.1 200 OK datagrams; extracts bare UUID from `USN`; returns `null` on malformed datagrams (UDA 1.0 §1.1, §1.3; spec User Story 4 acceptance #4)
- [X] T052 [US1] [P] Create `tests/UpnpSpy.Tests/Ssdp/SsdpMessageParserTests.cs` covering: NOTIFY ssdp:alive parse; NOTIFY ssdp:byebye parse; M-SEARCH 200 OK parse; missing `USN` → null; truncated payload → null; case-insensitive header matching; bare UUID extraction from `uuid:<uuid>::<rest>`
- [X] T053 [US1] Create `src/UpnpSpy.Core/Ssdp/MulticastSsdpTransport.cs` — per-NIC `UdpClient` instances bound to local IPv4 addresses, joining `239.255.255.250:1900` on each; `StartAsync`/`SendMSearchAsync` (HOST, MAN double-quoted, MX=3, ST, USER-AGENT per UDA 1.0 §1.2.1); shared bounded `Channel<ReceivedSsdpDatagram>` consumed by `ReceivedMessages`; logs Warning diagnostics on per-socket failures without bringing down siblings (FR-004, research §6)
- [X] T054 [US1] Create `tests/UpnpSpy.Tests/Ssdp/FakeSsdpTransport.cs` test double exposing a test-controlled `Channel<ReceivedSsdpDatagram>` plus a `SentMSearches` list

### Device registry

- [X] T055 [US1] Create `src/UpnpSpy.Core/Discovery/DeviceRegistryEvents.cs` records `DeviceAddedEvent(Device)`, `DeviceUpdatedEvent(Device)`, `DeviceRemovedEvent(string Uuid)`
- [X] T056 [US1] Create `src/UpnpSpy.Core/Discovery/DeviceRegistry.cs` — UUID-keyed in-memory registry; `TryAddOrUpdate(Device candidate)`; `Remove(string uuid)`; exposes `IReadOnlyDictionary<string, Device>` snapshot and an `IObservable<DeviceRegistryEvent>` (or equivalent event stream) (FR-005, FR-007, FR-008; data-model §1 lifetime)
- [X] T057 [US1] [P] Create `tests/UpnpSpy.Tests/Discovery/DeviceRegistryTests.cs` covering: first alive adds device + raises `DeviceAddedEvent`; second alive for known UUID does not duplicate (FR-007) and raises `DeviceUpdatedEvent` only when friendly-name or `LastSeenUtc` changes; byebye removes + raises `DeviceRemovedEvent` (FR-008); concurrent writes safe

### Discovery service (startup orchestration)

- [X] T058 [US1] Create `src/UpnpSpy.Core/Discovery/DiscoveryService.cs` — hosts the SSDP receive pump (consumes `ISsdpTransport.ReceivedMessages` and feeds the registry), exposes `RunStartupDiscoveryAsync(CancellationToken)` that calls `ISsdpTransport.SendMSearchAsync("ssdp:all", TimeSpan.FromSeconds(3), ct)` and resolves once the MX window plus the 1 s grace from research §18 elapses (FR-004, FR-006)
- [X] T059 [US1] [P] Create `tests/UpnpSpy.Tests/Discovery/DiscoveryServiceTests.cs` covering: M-SEARCH burst sent on startup with correct ST/MX (assert against `FakeSsdpTransport.SentMSearches`); incoming alive datagrams flow into the registry; duplicate alives don't duplicate; startup completes after MX + grace; `OperationCanceledException` cleanly halts the pump

### View-models and minimal view

- [X] T060 [US1] Create `src/UpnpSpy.Core/ViewModels/DeviceNodeViewModel.cs` source-generated `ObservableObject` exposing `Device`, `Label`, `Children` (empty in US1), no expand command yet (FR-009/FR-010 via `Device.Label`)
- [X] T061 [US1] Create `src/UpnpSpy.Core/ViewModels/DeviceTreeViewModel.cs` source-generated `ObservableObject` exposing `Devices: BoundedObservableCollection<DeviceNodeViewModel>`; subscribes to `DeviceRegistry` events (via `IDispatcher`) and mirrors them into `Devices` (FR-002, FR-005)
- [X] T062 [US1] [P] Create `tests/UpnpSpy.Tests/ViewModels/DeviceTreeViewModelTests.cs` using `FakeClock` + a synchronous `IDispatcher` fake covering: `DeviceAddedEvent` appends a `DeviceNodeViewModel`; `DeviceRemovedEvent` removes the matching node; same-UUID add does not duplicate
- [X] T063 [US1] Create `src/UpnpSpy.Core/ViewModels/ShellViewModel.cs` exposing `DeviceTree` (T061); inject `DiscoveryService`; in `InitializeAsync` call `RunStartupDiscoveryAsync` (FR-001)
- [X] T064 [US1] Create `src/UpnpSpy.App/Views/DeviceTreeView.xaml` and `.xaml.cs` — a `Microsoft.UI.Xaml.Controls.TreeView` bound to `DeviceTreeViewModel.Devices`, item template showing `Label` (no `Expanding` handler yet — that arrives in US3)
- [X] T065 [US1] Create `src/UpnpSpy.App/Views/ShellView.xaml` and `.xaml.cs` — a two-pane `Grid` with `DeviceTreeView` on the left and a placeholder `Border` on the right (the SSDP log view replaces the placeholder in US4) (FR-001)
- [X] T066 [US1] Update `src/UpnpSpy.App/MainWindow.xaml` to host `ShellView`; resolve `ShellViewModel` from DI in `MainWindow.xaml.cs`; call `ShellViewModel.InitializeAsync` on window open

**Checkpoint**: Launch the app on a LAN with a UPnP device; its friendly name appears in the left pane within ~5 s. No services, no actions, no SSDP log, no popups yet — but the MVP demo is real.

---

## Phase 4: User Story 2 — Watch the tree update as devices come and go (Priority: P2)

**Goal**: While the app is running, unsolicited NOTIFY alive advertisements add tree entries and NOTIFY byebye advertisements remove them, without user action (FR-006, FR-008, SC-003).

**Independent Test**: With the app running, power on a previously-off UPnP device → its name appears in the tree. Power it off gracefully → its entry disappears.

**Note**: The SSDP transport already pumps NOTIFY messages into `DiscoveryService` from US1, and `DeviceRegistry` already raises `DeviceAddedEvent` / `DeviceRemovedEvent`. US2 verifies the live-update path end-to-end, adds explicit byebye plumbing, and covers acceptance scenarios with tests.

- [X] T067 [US2] Update `src/UpnpSpy.Core/Discovery/DiscoveryService.cs` to also process unsolicited NOTIFY messages received outside any discovery window — alive entries call `DeviceRegistry.TryAddOrUpdate`; byebye entries call `DeviceRegistry.Remove(uuid)` (FR-006, FR-008)
- [X] T068 [US2] Update `src/UpnpSpy.Core/Models/Device.cs` so that subsequent alive advertisements may update `FriendlyName` (spec Edge Case: "Device announces itself, then re-announces with a changed friendly name") and `LastSeenUtc`, raising `DeviceUpdatedEvent` from the registry only when one of those changes
- [X] T069 [US2] [P] Extend `tests/UpnpSpy.Tests/Discovery/DeviceRegistryTests.cs` with: alive for known UUID with a new friendly name updates `Device.FriendlyName` and raises `DeviceUpdatedEvent`; byebye for an unknown UUID is a silent no-op; rapid alive/byebye/alive sequences end in the expected final state
- [X] T070 [US2] [P] Extend `tests/UpnpSpy.Tests/ViewModels/DeviceTreeViewModelTests.cs` with: `DeviceUpdatedEvent` updates the matching `DeviceNodeViewModel.Label` in place (no replace/no flicker, acceptance scenario #3); byebye for a device currently expanded is handled without exception

**Checkpoint**: A device powering on/off causes the tree to update live, with no user action and no flicker for repeats. US1 still works.

---

## Phase 5: User Story 3 — Drill into a device to see its services and actions (Priority: P3)

**Goal**: Expanding a device node fetches its description XML and shows services; expanding a service node fetches its SCPD and shows actions. Fetch failures display an inline message and don't crash (FR-002, FR-011, FR-012, FR-013, SC-004, SC-008).

**Independent Test**: Expand a known media-renderer device → AVTransport / RenderingControl etc. appear; expand a service → its actions appear.

### Description and SCPD fetchers (contracts/IDeviceDescriptionFetcher.md, research §9, §10)

- [X] T071 [US3] [P] Create `src/UpnpSpy.Core/Description/DeviceDescription.cs` record `(Uuid, FriendlyName?, IReadOnlyList<ServiceDescriptor> Services)` (the `Services` list is the **flattened union** of the root's `<serviceList>` and every `<serviceList>` in nested `<deviceList>` elements, walked recursively) and `src/UpnpSpy.Core/Description/ServiceDescriptor.cs` record `(ContainingDeviceUdn, ContainingDeviceFriendlyName?, ServiceId, ServiceType, ScpdUrl, ControlUrl, EventSubUrl)` — provenance fields populated by the parser so the mapping into `Service` (T017) can compute the disambiguating label (data-model §2; research §20)
- [X] T072 [US3] [P] Create `src/UpnpSpy.Core/Description/DeviceDescriptionFetchResult.cs` discriminated union `{ Success(DeviceDescription), HttpError(int, string), TransportError(string, Exception?), ParseError(string) }`
- [X] T073 [US3] [P] Create `src/UpnpSpy.Core/Description/IDeviceDescriptionFetcher.cs` interface per contracts/IDeviceDescriptionFetcher.md
- [X] T074 [US3] Create `src/UpnpSpy.Core/Description/DeviceDescriptionXmlParser.cs` — non-namespace-validating XML reader with DTD/XInclude disabled; resolves relative `SCPDURL`/`controlURL`/`eventSubURL` against the response's effective base URI (not against the containing `<device>` element); strips `uuid:` from `<UDN>`; **walks `<deviceList>` recursively** and emits one `ServiceDescriptor` per `<service>` at any depth, populating `ContainingDeviceUdn` and `ContainingDeviceFriendlyName` from the immediate parent `<device>`; emits a `Warning` `Description.Parse` diagnostic and drops duplicates if two services in the same root description share `(ContainingDeviceUdn, ServiceId)` (research §9, §20)
- [X] T075 [US3] Create `src/UpnpSpy.Core/Description/DeviceDescriptionFetcher.cs` consuming the shared `HttpClient` (T043), mapping HTTP/parse outcomes onto `DeviceDescriptionFetchResult`, emitting a Warning `DiagnosticEntry` per non-success with `Category=Description.Fetch|Description.Parse`
- [X] T076 [US3] [P] Create `tests/UpnpSpy.Tests/Description/DeviceDescriptionXmlParserTests.cs` covering: well-formed description; missing `<friendlyName>` → `FriendlyName=null` (FR-010 fallback path); relative URLs resolved against the description response's effective base URI; **embedded child's `<service>` produces a `ServiceDescriptor` whose `ContainingDeviceUdn`/`ContainingDeviceFriendlyName` come from the embedded `<device>` (research §20)**; **two `<deviceList>` levels deep are walked recursively and every service is flattened into `DeviceDescription.Services`**; **two embedded children that each declare the same `<serviceId>` both appear (identity disambiguated by `ContainingDeviceUdn`)**; **two services within the same `<device>` element that share `<serviceId>` emit a `Description.Parse` Warning and the duplicate is dropped**; malformed XML → ParseError; DTD reference rejected
- [X] T077 [US3] [P] Create `tests/UpnpSpy.Tests/Description/FakeDeviceDescriptionFetcher.cs` test double allowing per-URL pre-programmed results and recording call counts
- [X] T078 [US3] [P] Create `src/UpnpSpy.Core/Description/ScpdDocument.cs` record `(IReadOnlyList<ActionDefinition> Actions, IReadOnlyList<StateVariableDefinition> StateVariables)`, `src/UpnpSpy.Core/Description/ScpdFetchResult.cs` discriminated union `{ Success, HttpError, TransportError, ParseError }`, and `src/UpnpSpy.Core/Description/IScpdFetcher.cs`
- [X] T079 [US3] Create `src/UpnpSpy.Core/Description/ScpdXmlParser.cs` parsing SCPD per UDA 1.0 §2.2 — preserves SCPD-declared order of input/output arguments (critical for SOAP envelope construction in US7); maps `relatedStateVariable` → `dataType` lookup; emits one `Warning` `DiagnosticEntry` per parse failure
- [X] T080 [US3] Create `src/UpnpSpy.Core/Description/ScpdFetcher.cs` mirroring `DeviceDescriptionFetcher` for the SCPD case
- [X] T081 [US3] [P] Create `tests/UpnpSpy.Tests/Description/ScpdXmlParserTests.cs` covering: action with zero inputs; action with zero outputs (FR-031); ordered argument lists; missing/empty `<actionList>` → empty list with no error; bad direction value → ParseError; state variables populated

### Lazy expand wired into view-models and view

- [X] T082 [US3] Create `src/UpnpSpy.Core/ViewModels/ServiceNodeViewModel.cs` and `src/UpnpSpy.Core/ViewModels/ActionNodeViewModel.cs` source-generated `ObservableObject`s exposing `Service`/`Action`, computed `Label`, and (for service) `Actions` collection
- [X] T083 [US3] Update `src/UpnpSpy.Core/ViewModels/DeviceNodeViewModel.cs` to add `Services` collection, an `ExpandAsync` command bound to first-time expansion (FR-011) that calls `IDeviceDescriptionFetcher`, populates `Device.Services` on success, and replaces the children with a single placeholder `"⚠ Services unavailable: <reason>"` on any non-success result (FR-013, research §9)
- [X] T084 [US3] Update `src/UpnpSpy.Core/ViewModels/ServiceNodeViewModel.cs` to add an `ExpandAsync` command bound to first-time service expansion (FR-012) calling `IScpdFetcher`, populating actions on success, and applying the same `"⚠ Actions unavailable: …"` inline placeholder on failure (FR-013)
- [X] T085 [US3] Update `src/UpnpSpy.App/Views/DeviceTreeView.xaml` and `.xaml.cs` — hierarchical `TreeView` `ItemTemplateSelector`, `Expanding` event handler that calls the corresponding view-model's `ExpandAsync` exactly once per node (cached state via `FetchState`)
- [X] T086 [US3] [P] Extend `tests/UpnpSpy.Tests/ViewModels/DeviceTreeViewModelTests.cs` (or add a new `DeviceNodeViewModelTests.cs` and `ServiceNodeViewModelTests.cs`) covering: first expansion triggers fetch; subsequent expansions don't refetch; failure surfaces the inline placeholder and leaves the parent device in place (FR-013, SC-008); cancellation on view-model dispose halts in-flight fetches; **a device with two embedded children renders services from both as siblings under the root device node, with `Service.Label` prefixed by the containing embedded device's friendly name so same-typed services across embedded children are distinguishable in the tree (research §20)**

**Checkpoint**: Tree expansion works end-to-end. A misbehaving device's failure to describe itself does not affect siblings. US1/US2 still work.

---

## Phase 6: User Story 4 — Watch SSDP traffic live in the right pane (Priority: P4)

**Goal**: Every received SSDP alive/byebye advertisement appears as a row in the right-pane log within 1 s, capped at 10,000 entries with FIFO eviction (FR-003, FR-014, FR-015, FR-016, SC-009, SC-013).

**Independent Test**: With a chatty UPnP device on the LAN, rows arrive in the right pane over time; power-cycling produces a BYEBYE followed by ALIVE rows; the user can scroll back without the list jumping to the bottom.

- [X] T087 [US4] Create `src/UpnpSpy.Core/ViewModels/SsdpLogViewModel.cs` source-generated `ObservableObject` exposing `Entries: BoundedObservableCollection<SsdpLogEntry>` (capacity 10,000) and a `DispatcherQueue.TryEnqueue`-backed `Append(SsdpLogEntry)` (FR-014, FR-015, FR-016, research §8)
- [X] T088 [US4] [P] Create `tests/UpnpSpy.Tests/ViewModels/SsdpLogViewModelTests.cs` covering: alive entry appended with `Kind=Alive` (FR-014); byebye entry appended with `Kind=Byebye` (FR-015); 10,001st add evicts the oldest (FR-016); ordering is insertion order; large stress (50k inserts) stays bounded
- [X] T089 [US4] Update `src/UpnpSpy.Core/Discovery/DiscoveryService.cs` to fork each parsed NOTIFY into both the registry (existing path) and the new `SsdpLogViewModel.Append` — datagrams from which a UUID cannot be extracted are dropped for log purposes and recorded as Warning `Ssdp.Parse` diagnostics (User Story 4 acceptance #4)
- [X] T090 [US4] Create `src/UpnpSpy.App/Views/SsdpLogView.xaml` and `.xaml.cs` — a virtualizing `ListView` bound to `SsdpLogViewModel.Entries` with columns `ReceivedUtc → local` / `Kind` / `DeviceUuid`; auto-scroll-to-bottom behaviour that disables when the user scrolls up (acceptance scenario #3)
- [X] T091 [US4] Update `src/UpnpSpy.App/Views/ShellView.xaml` to replace the placeholder right pane with `SsdpLogView` and bind it to the shell's `SsdpLog` property

**Checkpoint**: Right pane fills with alive/byebye rows in real time; the user can scroll up and back down; the FIFO cap is invisible at normal rates.

---

## Phase 7: User Story 5 — View a device's or service's raw XML in the browser (Priority: P5)

**Goal**: Right-clicking a device offers "Fetch XML" → opens device description URL in the default browser; right-clicking a service offers "Fetch service XML" → opens SCPD URL (FR-017, FR-018, FR-019, FR-020, SC-005).

**Independent Test**: Right-click any device → choose Fetch XML → default browser opens to that device's description URL. Same for any service.

- [X] T092 [US5] [P] Create `src/UpnpSpy.Core/Platform/IBrowserLauncher.cs` interface per contracts/IBrowserLauncher.md
- [X] T093 [US5] Create `src/UpnpSpy.App/Platform/DefaultBrowserLauncher.cs` calling `Windows.System.Launcher.LaunchUriAsync(url)` and emitting a Warning `App.Lifecycle` `DiagnosticEntry` on exception (research §13)
- [X] T094 [US5] Update `src/UpnpSpy.Core/ViewModels/DeviceNodeViewModel.cs` to add `FetchXmlCommand` (`RelayCommand`) calling `IBrowserLauncher.OpenAsync(Device.LocationUrl, …)` (FR-017, FR-019)
- [X] T095 [US5] Update `src/UpnpSpy.Core/ViewModels/ServiceNodeViewModel.cs` to add `FetchScpdCommand` calling `IBrowserLauncher.OpenAsync(Service.ScpdUrl, …)` (FR-018, FR-020)
- [X] T096 [US5] Update `src/UpnpSpy.App/Views/DeviceTreeView.xaml` to attach `MenuFlyout` context menus on device and service tree items: device offers "Fetch XML" only; service offers "Fetch service XML" (and a "Subscribe" placeholder bound up in US8); action nodes have no menu (acceptance scenario #3)
- [X] T097 [US5] [P] Add `tests/UpnpSpy.Tests/ViewModels/DeviceNodeViewModelTests.cs` (or extend existing) and `tests/UpnpSpy.Tests/ViewModels/ServiceNodeViewModelTests.cs` covering: `FetchXmlCommand` invokes `IBrowserLauncher.OpenAsync(LocationUrl)` exactly once; `FetchScpdCommand` invokes `OpenAsync(ScpdUrl)`; a `false` return logs a Warning but does not throw; command stays enabled even when fetch state has failed (the URL is still inspectable in the browser)

**Checkpoint**: Right-clicking any device or service offers the correct menu; choosing it opens the URL in the default browser. Other user stories unaffected.

---

## Phase 8: User Story 6 — Force a rescan from the View menu (Priority: P6)

**Goal**: A View > Rescan command re-runs the startup probe; after the MX window elapses, devices that did not respond are pruned (FR-021–FR-024, SC-006).

**Independent Test**: With a device in the tree, yank its power so it cannot send byebye; wait long enough that no further advertisements arrive; choose View > Rescan; after ~4 s the device is pruned from the tree.

- [X] T098 [US6] Create `src/UpnpSpy.Core/Discovery/RescanCoordinator.cs` — manages an at-most-one `DiscoverySession`; on each rescan, snapshots `KnownAtStart` from the registry, calls `ISsdpTransport.SendMSearchAsync(...)`, tracks `HeardThisSession` UUIDs from the live receive pump, and at the MX + grace deadline removes any UUID in `KnownAtStart \ HeardThisSession` (FR-023). A new rescan invoked while another is running marks the older session `Superseded` and the new one takes over (data-model §7, spec Edge Case)
- [X] T099 [US6] Update `src/UpnpSpy.Core/Discovery/DiscoveryService.cs` so that unsolicited alive/byebye handling proceeds unchanged during a rescan (FR-024) — the coordinator and the live pump are independent paths into the registry
- [X] T100 [US6] [P] Create `tests/UpnpSpy.Tests/Discovery/RescanCoordinatorTests.cs` covering: rescan sends one M-SEARCH burst per active interface; devices not heard during the session are pruned at deadline (FR-023); new previously-unseen devices that respond are kept; a second rescan started before the first completes supersedes it; byebye during a rescan removes immediately (FR-024)
- [X] T101 [US6] Update `src/UpnpSpy.Core/ViewModels/ShellViewModel.cs` to expose a `RescanCommand` calling `RescanCoordinator.RescanAsync(...)` and a `IsRescanInProgress` observable property
- [X] T102 [US6] Update `src/UpnpSpy.App/Views/ShellView.xaml` to add a `MenuBar` with a `View` menu containing a `Rescan` `MenuFlyoutItem` bound to `RescanCommand` (FR-021, plus the disabled-while-running visual state)

**Checkpoint**: View > Rescan reliably reconciles the tree against ground truth; concurrent NOTIFY processing continues throughout.

---

## Phase 9: User Story 7 — Invoke an action on a service (Priority: P7)

**Goal**: Double-click an action → popup with editable inputs; submit → success shows outputs, fault shows HTTP status + UPnP error code + fault text, transport error shows a diagnostic-style message (FR-025–FR-031, SC-010, SC-011).

**Independent Test**: Double-click `RenderingControl::GetVolume`, fill InstanceID=0/Channel=Master, invoke → output value shown. Invoke with a bad argument → fault details shown.

- [X] T103 [US7] [P] Create `src/UpnpSpy.Core/Control/IControlClient.cs` interface per contracts/IControlClient.md (the `InvocationResult` union already exists from T022)
- [X] T104 [US7] Create `src/UpnpSpy.Core/Control/SoapEnvelopeBuilder.cs` building a UDA 1.0 §3.1.1 SOAP 1.1 envelope: `<s:Envelope>` / `<s:Body>` / `<u:<actionName> xmlns:u="<serviceType>">` with one child per declared input argument in SCPD order; XML-escapes `< > & " '` only; produces a self-closing action element for zero-input actions (FR-031)
- [X] T105 [US7] Create `src/UpnpSpy.Core/Control/SoapFaultParser.cs` extracting `<UPnPError>/<errorCode>` and `<errorDescription>` from a SOAP 1.1 `<s:Fault>` body (UDA 1.0 §3.1.3); returns `(errorCode=0, errorDescription=reasonPhrase)` on bodies without a recognizable `UPnPError`
- [X] T106 [US7] Create `src/UpnpSpy.Core/Control/ControlClient.cs` — issues `POST <controlUrl>` with mandatory `SOAPACTION: "<serviceType>#<actionName>"` (double quotes mandatory), `HOST`, `CONTENT-LENGTH`, `CONTENT-TYPE: text/xml; charset="utf-8"`, `USER-AGENT`; maps HTTP outcomes onto `InvocationResult.{Success, UpnpFault, TransportError}`; emits one Warning `DiagnosticEntry` per non-success (research §11, FR-028/29/30)
- [X] T107 [US7] [P] Create `tests/UpnpSpy.Tests/Control/SoapEnvelopeBuilderTests.cs` covering: argument order preserved; zero-input action produces self-closing action element; XML-special characters escaped; namespace declaration on action element matches `serviceType`; UTF-8 output
- [X] T108 [US7] [P] Create `tests/UpnpSpy.Tests/Control/SoapFaultParserTests.cs` covering: well-formed fault with `<UPnPError>`; fault without `<UPnPError>` falls back to `(0, reasonPhrase)`; malformed XML returns `(0, rawBody)` and does not throw
- [X] T109 [US7] [P] Create `tests/UpnpSpy.Tests/Control/FakeControlClient.cs` test double that returns canned `InvocationResult`s matched by `(service, action)` predicate
- [X] T110 [US7] Create `src/UpnpSpy.Core/ViewModels/InvocationPopupViewModel.cs` source-generated `ObservableObject` exposing per-input editable strings, `InvokeCommand`, `Result` (data-model §9), and an `OnDeviceRemoved` reaction that transitions to a closeable "device no longer reachable" state (FR-037)
- [X] T111 [US7] [P] Create `tests/UpnpSpy.Tests/ViewModels/InvocationPopupViewModelTests.cs` covering: popup builds `InvocationRequest` from input values; success populates outputs (FR-028); SOAP fault populates HTTP/error/text fields (FR-029); transport error populates message (FR-030); zero-input action invocable (FR-031); zero-output success shows "Succeeded (no output values)" (FR-031); device byebye during invocation flips state to "device no longer reachable" (FR-037)
- [X] T112 [US7] Create `src/UpnpSpy.App/Views/InvocationPopup.xaml` and `.xaml.cs` — secondary `Window` instance per popup hosting a form bound to `InvocationPopupViewModel`; auto-generated input rows; Invoke button; result panel switching between success/fault/transport-error templates
- [X] T113 [US7] Update `src/UpnpSpy.App/Views/DeviceTreeView.xaml.cs` to handle `ItemInvoked` (or `DoubleTapped`) on action nodes by resolving an `InvocationPopupViewModel` for `(service, action)` and opening a new `InvocationPopup` window (FR-025, SC-010)

**Checkpoint**: Invocation works against real devices end-to-end. The popup never crashes the app for any documented failure mode; closing a popup whose underlying device has gone away is safe.

---

## Phase 10: User Story 8 — Subscribe to a service's events (Priority: P8)

**Goal**: Right-click a service → Subscribe → popup opens, the app SUBSCRIBE+auto-renews while the popup is open, every NOTIFY pushes a row into the popup's scrolling list, closing the popup UNSUBSCRIBEs (FR-032–FR-038, SC-012).

**Independent Test**: Subscribe to AVTransport on a media renderer; trigger playback on the device; event rows appear; close the popup and verify an UNSUBSCRIBE was sent.

### Subscription client and renewal

- [X] T114 [US8] [P] Create `src/UpnpSpy.Core/Eventing/SubscribeResult.cs`, `RenewResult.cs`, `UnsubscribeResult.cs` discriminated unions per contracts/ISubscriptionClient.md
- [X] T115 [US8] [P] Create `src/UpnpSpy.Core/Eventing/ISubscriptionClient.cs` interface per contracts/ISubscriptionClient.md
- [X] T116 [US8] Create `src/UpnpSpy.Core/Eventing/SubscriptionClient.cs` — issues `SUBSCRIBE` with mandatory angle-bracketed `CALLBACK`, `NT: upnp:event`, `TIMEOUT: Second-<n>` (UDA 1.0 §4.1.1); parses response `SID` and `TIMEOUT` (`Second-<n>` or `infinite`); RENEW omits `CALLBACK`/`NT`; UNSUBSCRIBE sends `SID` only; maps outcomes onto the union types; per-non-success Warning `DiagnosticEntry` with `Category=Eventing.Subscribe|Renew|Unsubscribe` (research §12)
- [X] T117 [US8] [P] Create `tests/UpnpSpy.Tests/Eventing/FakeSubscriptionClient.cs` test double that queues per-call results and captures every request's headers/timing
- [X] T118 [US8] Create `src/UpnpSpy.Core/Eventing/SubscriptionRenewalScheduler.cs` — one per active subscription; uses `IClock` + `Task.Delay` to fire `granted_timeout − 30 s` before expiry, calling `ISubscriptionClient.RenewAsync`; on `RenewResult.HttpError`/`TransportError` transitions the `SubscriptionState` to `Lapsed` and stops (FR-038, research §12)
- [X] T119 [US8] [P] Create `tests/UpnpSpy.Tests/Eventing/SubscriptionRenewalSchedulerTests.cs` using `FakeClock` covering: first renewal fires exactly 30 s before granted timeout; successful renewal reschedules off the new granted timeout; failed renewal transitions to `Lapsed` and halts further renewals; scheduler stops cleanly on dispose

### GENA event callback host

- [X] T120 [US8] [P] Create `src/UpnpSpy.Core/Eventing/EventCallbackRegistration.cs` record `(Guid Token, Uri CallbackUrl)` and `src/UpnpSpy.Core/Eventing/IEventCallbackHost.cs` interface per contracts/IEventCallbackHost.md
- [X] T121 [US8] Create `src/UpnpSpy.Core/Eventing/GenaNotifyParser.cs` parsing `<e:propertyset>` / `<e:property>` into `EventNotification` (UDA 1.0 §4.3); preserves `RawXml` for partial parses
- [X] T122 [US8] [P] Create `tests/UpnpSpy.Tests/Eventing/GenaNotifyParserTests.cs` covering: single-property notification; multi-property notification; namespace-prefixed variants; malformed XML returns `RawXml` only; empty body → empty `Properties`
- [X] T123 [US8] Create `src/UpnpSpy.App/Platform/HttpListenerEventCallbackHost.cs` (lives in App because of `System.Net.HttpListener` Windows binding requirements) — binds `http://+:<port>/upnpspy/` (port chosen at startup), accepts `NOTIFY` requests at `/upnpspy/<token>/`, verifies `NT: upnp:event`, `NTS: upnp:propchange`, `SID`, `SEQ`, parses the body via `GenaNotifyParser`, dispatches into per-registration bounded channels, responds 200 (success) or 400 (header/parse failure) (research §12; contracts/IEventCallbackHost.md). Register the implementation in T046 alongside other Windows-specific adapters

### Subscription popup view-model and view

- [X] T124 [US8] Create `src/UpnpSpy.Core/ViewModels/SubscriptionPopupViewModel.cs` source-generated `ObservableObject` exposing `Status` (data-model §10), `Events: BoundedObservableCollection<EventNotification>` (cap 5,000), `FailureReason?`; on construction calls `IEventCallbackHost.Register`, then `ISubscriptionClient.SubscribeAsync`; on success starts the renewal scheduler and pumps `IEventCallbackHost.EventsFor(...)` into `Events`; on popup close sends `UnsubscribeAsync` only if `Status == Active` (FR-034/35/38); reacts to device-removed by transitioning to a closeable "device no longer reachable" state (FR-037)
- [X] T125 [US8] [P] Create `tests/UpnpSpy.Tests/ViewModels/SubscriptionPopupViewModelTests.cs` covering: successful subscribe populates SID/timeout and starts renewal; events pushed into `Events`; close while Active sends UNSUBSCRIBE (FR-034); SUBSCRIBE failure → state `Failed`, close does NOT send UNSUBSCRIBE (FR-035); renewal failure → state `Lapsed`, close does NOT send UNSUBSCRIBE (FR-038); device byebye → state `Closed`, close cleanly without UNSUBSCRIBE (FR-037); event channel overflow drops oldest without crashing
- [X] T126 [US8] Create `src/UpnpSpy.App/Views/SubscriptionPopup.xaml` and `.xaml.cs` — secondary `Window` instance per popup hosting a status banner + virtualizing `ListView` bound to `Events`; OnClose triggers the view-model's close logic before the window is fully dismissed (SC-012)
- [X] T127 [US8] Update `src/UpnpSpy.App/Views/DeviceTreeView.xaml` to wire the previously-placeholder "Subscribe" service-context-menu item into resolving a `SubscriptionPopupViewModel` for the chosen service and opening a new `SubscriptionPopup` window (FR-018, FR-032, FR-036 — multiple popups per service supported because each gets its own VM/registration/SID)

**Checkpoint**: Subscriptions roundtrip live, auto-renew across a granted timeout, UNSUBSCRIBE on graceful close, lapse cleanly on failure, and survive device byebye without orphaning UNSUBSCRIBE traffic. All eight user stories now work.

---

## Phase 11: Polish & Cross-Cutting Concerns

**Purpose**: Mandatory functional requirements that are cross-cutting (diagnostic viewer window, MSIX packaging, end-to-end quickstart), plus performance and consistency passes.

### View > Diagnostics window (FR-041)

- [X] T128 [P] Create `src/UpnpSpy.Core/ViewModels/DiagnosticsViewerViewModel.cs` source-generated `ObservableObject` exposing `Entries: BoundedObservableCollection<DiagnosticEntry>`; on open calls `IDiagnosticBuffer.Snapshot()` then `IDiagnosticBuffer.Subscribe(...)` for live updates; unsubscribes on close (FR-041)
- [X] T129 [P] Create `tests/UpnpSpy.Tests/ViewModels/DiagnosticsViewerViewModelTests.cs` covering: initial `Snapshot` populates `Entries` in chronological order; subsequent `Record` calls flow into `Entries`; dispose unsubscribes (no further updates after close); SC-014 — a `Record` made immediately before opening the viewer is visible in `Snapshot`
- [X] T130 Create `src/UpnpSpy.App/Views/DiagnosticsWindow.xaml` and `.xaml.cs` — secondary `Window` hosting a virtualizing `ListView` bound to `DiagnosticsViewerViewModel.Entries` with columns Timestamp/Severity/Category/Message and an expandable detail row for `Context` and `Exception`
- [X] T131 Update `src/UpnpSpy.App/Views/ShellView.xaml` to add a `View > Diagnostics` `MenuFlyoutItem` (sibling of Rescan from T102) that opens a new `DiagnosticsWindow` (FR-041)

### MSIX packaging, signing, and assets

- [X] T132 Update `src/UpnpSpy.App/Package.appxmanifest` with full visual assets (small/large tiles, store logo, splash screen) under `src/UpnpSpy.App/Assets/`, both x64 and ARM64 architecture entries, and the URL ACL registration for `http://+:*/upnpspy/` so HTTP callback eventing works under MSIX without elevation (research §15, contracts/IEventCallbackHost.md "URL ACL note"). _MSIX visual assets generated from the Linn Products brand wordmark via `src/UpnpSpy.App/Assets/_generate.ps1`. Architecture entries come from publish-time `-r win-x64` / `-r win-arm64`; the URL-ACL step is documented in `quickstart.md` §3/§9 (no code change needed)._
- [ ] T133 Verify `dotnet publish src\UpnpSpy.App\UpnpSpy.App.csproj -c Release -r win-x64 -p:WindowsPackageType=MSIX` and `-r win-arm64` both produce a valid `.msix` (quickstart §3). _Deferred: CLI `dotnet publish -p:WindowsPackageType=MSIX` produces unpackaged binaries only; producing the actual `.msix` artefact currently requires Visual Studio 2022 with the MSIX Packaging Tools workload (limitation noted in `quickstart.md` §3)._

### End-to-end validation and hardening

- [ ] T134 Execute the manual smoke test in `specs/001-upnp-spy-discovery/quickstart.md` §5 against a real LAN with at least one media-renderer device; record any deltas and open issues for each (this is a runbook task, no code change unless deltas are found). _Deferred: runbook task requiring live LAN with a media-renderer device; cannot be executed in a sandboxed dev session._
- [X] T135 [P] Run `dotnet format UpnpSpy.sln --verify-no-changes` and `dotnet build UpnpSpy.sln -warnaserror` clean across the whole solution (Constitution III — warnings-as-errors must hold)
- [ ] T136 [P] Performance check: launch the app on a LAN with 20+ devices, confirm tree populates within 5 s (SC-001); confirm a new SSDP advertisement appears in the right pane within 1 s (SC-009); confirm an invocation popup is interactive within 1 s of double-click (SC-010); confirm a typical invocation result is shown within 2 s of submit (SC-011); record any failures. _Deferred: runbook task requiring a live LAN with 20+ devices; cannot be measured in a sandboxed dev session._
- [ ] T137 [P] Memory ceiling check: run an 8-hour session with 20 devices and at least one open subscription popup; confirm resident memory stays under 200 MB (plan.md Scale/Scope), SSDP log stays at 10,000 entries, ring buffer stays at 5,000 entries, on-disk log stays ≤16 MB. _Deferred: runbook task requiring an 8-hour live LAN soak; cannot be executed in a sandboxed dev session._

---

## Phase 12: Eager device-description fetch (FR-043) — added 2026-05-14

**Goal**: A device's description is fetched as soon as the device is discovered, so the tree label resolves from `uuid:<uuid>` to the friendly name without the user expanding the node. Service-level SCPD fetches remain lazy (US3, T084). The change reuses the existing `IDeviceDescriptionFetcher` from US3 — no parser changes.

**Independent Test**: Launch on a LAN with at least one UPnP device that publishes a `<friendlyName>` in its description; within a few seconds of the device appearing in the tree, its label transitions from `uuid:<uuid>` to the human-readable name without any click.

**Where this lands in the codebase**:

- New dispatcher: `src/UpnpSpy.Core/Discovery/EagerDescriptionDispatcher.cs`.
- Updated registration: `src/UpnpSpy.Core/Composition/CoreServiceCollectionExtensions.cs` (singleton, started by `ShellViewModel.InitializeAsync`).
- Updated view-model: `src/UpnpSpy.Core/ViewModels/DeviceNodeViewModel.cs` — `ExpandAsync` becomes a state-machine over `Device.DescriptionFetchState` instead of an HTTP-initiating call (the existing `LoadAsync`/`ApplySuccess` are deleted or moved into the dispatcher).
- Updated registry signal: `src/UpnpSpy.Core/Discovery/DeviceRegistry.cs` — `DeviceUpdatedEvent` MUST now fire when `Device.FriendlyName` or `Device.Services` is replaced by the dispatcher, so the tree label refreshes (T068 already wired up FriendlyName-driven updates; this task confirms it covers the description-driven case).
- Updated tests, listed below.

### Dispatcher and registry plumbing

- [X] T138 [P] [FR-043] Create `src/UpnpSpy.Core/Discovery/EagerDescriptionDispatcher.cs` — singleton hosted by `ShellViewModel`'s startup path. Subscribes to `DeviceRegistry`'s `DeviceAdded` event. For each added `Device`, transitions `DescriptionFetchState` from `NotFetched` to `Fetching`, acquires one slot of a shared `SemaphoreSlim(initialCount: 8)`, calls `IDeviceDescriptionFetcher.FetchAsync(Device.LocationUrl, linkedCt)` where `linkedCt` is the link of `AppShutdownTokenSource.Token` and a per-device CTS, and on completion: (Success) writes `FriendlyName`/`Services`, flips state to `Loaded`, calls `DeviceRegistry.NotifyUpdated(uuid)` so `DeviceUpdated` fires; (HttpError/TransportError/ParseError) sets `DescriptionFetchError` to the failure reason, flips state to `Failed`, emits a Warning `DiagnosticEntry` with `Category=Description.Fetch` or `Description.Parse` and `Context={device.uuid, url, http.status?}`. The dispatcher MUST release the semaphore slot in both branches (`try`/`finally`).
- [X] T139 [FR-043] Update `src/UpnpSpy.Core/Discovery/DeviceRegistry.cs` (and `DeviceRegistryEvents.cs` if needed) so that: (a) `Remove(uuid)` signals the per-device CTS owned by the dispatcher to cancel any in-flight fetch (the dispatcher holds the CTSs in a `ConcurrentDictionary<string, CancellationTokenSource>` keyed by UUID); (b) repeat alive for an already-known UUID does **not** raise `DeviceAdded` (so the dispatcher does not re-enqueue); (c) a fresh registry add for a UUID that was previously removed (byebye then alive) DOES raise `DeviceAdded` and triggers a fresh fetch. Add a public/internal `NotifyUpdated(string uuid)` (or equivalent) that the dispatcher calls after writing description-derived fields, so `DeviceUpdatedEvent` flows out and the tree label refreshes.
- [X] T140 [FR-043, FR-011] Update `src/UpnpSpy.Core/ViewModels/DeviceNodeViewModel.cs`: delete the HTTP-initiating path from `ExpandAsync`/`LoadAsync`/`ApplyResult`. Replace with a state-machine that reads `Device.DescriptionFetchState` once: `Loaded` → call `ApplySuccess` (already exists) to hydrate `Children` from `Device.Services`; `Failed` → call `FailWith(Device.DescriptionFetchError ?? "Unknown error")`; `Fetching` → insert a single `"Loading…"` placeholder child, subscribe (via `IDispatcher.Post`-marshalled `DeviceRegistry.DeviceUpdated` / `DeviceRemoved` handlers scoped to this UUID) so that when the state flips to `Loaded` or `Failed` the placeholder is replaced. `RefreshLabel` is now driven by `DeviceUpdated`, not by the view-model's own fetch path. Cancellation rule: closing the window (`_shutdownToken`) terminates the placeholder-subscription wait without surfacing any error.
- [X] T141 [FR-043] Update `src/UpnpSpy.Core/Composition/CoreServiceCollectionExtensions.cs` to register `EagerDescriptionDispatcher` as a singleton, and update `src/UpnpSpy.Core/ViewModels/ShellViewModel.cs` so `InitializeAsync` resolves it and calls its `Start()` (or equivalent) **before** invoking `DiscoveryService.RunStartupDiscoveryAsync` (so the very first M-SEARCH-response add fires the eager fetch).

### Tests

- [X] T142 [P] [FR-043] Create `tests/UpnpSpy.Tests/Discovery/EagerDescriptionDispatcherTests.cs` (with `FakeDeviceDescriptionFetcher` from T077 and `FakeClock` / synchronous `IDispatcher`) covering: a single `DeviceAdded` event triggers exactly one `FetchAsync` call against the device's `LocationUrl`; on success the device's `FriendlyName` and `Services` are populated and `DeviceUpdated` fires; on failure `DescriptionFetchState==Failed`, `DescriptionFetchError` is populated, a Warning `DiagnosticEntry` is recorded, and no `DeviceUpdated` is raised; a `DeviceRemoved` raised mid-fetch cancels the in-flight `FetchAsync` (assert via the fake's recorded `CancellationToken`); a discovery burst of 20 simultaneous adds produces at most 8 concurrent fetch calls (assert via in-flight counter on the fake); a second `DeviceAdded` for the same UUID does NOT trigger a second fetch; a `DeviceRemoved`+`DeviceAdded` sequence for the same UUID DOES trigger a fresh fetch.
- [X] T143 [P] [FR-043] Extend `tests/UpnpSpy.Tests/Discovery/DeviceRegistryTests.cs` covering: `Remove(uuid)` signals the dispatcher-owned CTS; subsequent alive for the same UUID raises a fresh `DeviceAdded`; `NotifyUpdated` raises `DeviceUpdated` only when called and never for unknown UUIDs.
- [X] T144 [P] [FR-043, FR-011] Extend `tests/UpnpSpy.Tests/ViewModels/DeviceTreeViewModelTests.cs` (or add `tests/UpnpSpy.Tests/ViewModels/DeviceNodeViewModelTests.cs` if it doesn't already exist) covering: a `DeviceUpdated` event whose `Device.FriendlyName` flipped from null to a real value refreshes `DeviceNodeViewModel.Label` in place (no node replacement / no flicker); expanding a node with `DescriptionFetchState==Loaded` hydrates `Children` synchronously without any HTTP fetch (assert against a `FakeDeviceDescriptionFetcher` call count of zero post-expansion); expanding a node with `DescriptionFetchState==Fetching` shows a `"Loading…"` placeholder and resolves to real children when the state flips to `Loaded`; expanding a node with `DescriptionFetchState==Failed` shows the FR-013 inline error placeholder.
- [X] T145 [P] [FR-043] Extend `tests/UpnpSpy.Tests/Discovery/DiscoveryServiceTests.cs` (or the equivalent integration-style test) covering: from the moment an M-SEARCH response is parsed to the moment the resulting `Device.FriendlyName` is populated, the only HTTP-side actor is the dispatcher (the discovery service itself does NOT call `IDeviceDescriptionFetcher`); device byebye received while the eager fetch is in flight cancels it cleanly (assert no unobserved exceptions on the test scheduler).

### Existing tasks that change in scope (no new code, just amended acceptance)

- [X] T146 [FR-011, FR-043] Update the description text of T083 (now historical) — `DeviceNodeViewModel.ExpandAsync` no longer initiates the description fetch. Done as part of T140. Tracking entry only; no separate code change.
- [X] T147 [FR-043] Update `specs/001-upnp-spy-discovery/quickstart.md` §5 smoke-test step ("Expand the device to see services") to add a preceding bullet: "Confirm the device label resolves from `uuid:<uuid>` to its friendly name within ~5 s of appearing in the tree, **without** expanding the node."

**Checkpoint**: Discovering a device produces a tree entry whose label transitions from `uuid:<uuid>` to the friendly name within the SC-001 budget. Expanding a device is now a UI-only step (no HTTP). SCPD fetch on service expansion is unchanged. All existing user-story checkpoints still pass.

---

## Phase 13: Tree affordance — chevron + node-type glyphs (FR-044, FR-045, SC-015) — added 2026-05-14

**Goal**: Make it visually obvious which tree rows are expandable, even before the user has clicked anything, and distinguish device / service / action rows at a glance. Fixes the WinUI `TreeView` behaviour where the chevron is omitted until a node's `ItemsSource` has at least one item (so previously expandable nodes only sprouted a chevron *after* the user expanded them once).

**Independent Test**: Launch the app on a LAN with at least one UPnP device. Without expanding anything, confirm: (a) the device row shows an expand chevron and a "Device" glyph; (b) expanding it reveals service rows that each show a chevron and a "Service" glyph; (c) expanding a service reveals action rows that show no chevron and an "Action" glyph. The chevron must be present on devices and services *before* any user interaction.

### Implementation

- [X] T148 [FR-044] Update `src/UpnpSpy.Core/ViewModels/DeviceNodeViewModel.cs`: pre-seed `Children.Add(LoadingPlaceholder)` from the bare-bones constructor so every `DeviceNodeViewModel` carries one child item from the moment it is created (the parameterized constructor chains through it, so the placeholder applies to both production and test code paths). The existing `ExpandCoreAsync` placeholder-add guarded by `Children.Count == 0` continues to be a no-op when the pre-seed is already present. The `RenderTerminal` `Children.Clear()` then repopulate continues to atomically swap the placeholder for the real children (or for the FR-013 inline error string).
- [X] T149 [FR-044] Update `src/UpnpSpy.Core/ViewModels/ServiceNodeViewModel.cs`: pre-seed `Children.Add("Loading…")` in the constructor (mirroring T148). `ApplyResult`'s existing `Children.Clear()` then repopulate continues to swap the placeholder for the SCPD-derived actions (or the FR-013 inline error placeholder).
- [X] T150 [FR-045] Update `src/UpnpSpy.App/Views/DeviceTreeView.xaml`: replace each of `DeviceTemplate`, `ServiceTemplate`, `ActionTemplate` with a layout that renders a `FontIcon` (font family bound to the `SymbolThemeFontFamily` system resource) before the label. Glyph choices: device → `&#xE968;` (NetworkAdapter); service → `&#xE950;` (Component); action → `&#xE945;` (LightningBolt). The `PlaceholderTemplate` (string fallback for the `"Loading…"` placeholder and `"⚠ Services/Actions unavailable: …"` error strings) does **not** carry an icon — the leading text glyph already conveys its meaning.

### Tests

- [X] T151 [P] [FR-044] Add a fresh `Newly_constructed_DeviceNodeViewModel_contains_Loading_placeholder_child` test in `tests/UpnpSpy.Tests/ViewModels/DeviceNodeViewModelTests.cs` asserting that immediately after construction (no `ExpandAsync` called) `sut.Children` contains exactly the `LoadingPlaceholder` string. Add a sibling `Newly_constructed_ServiceNodeViewModel_contains_Loading_placeholder_child` test in `tests/UpnpSpy.Tests/ViewModels/ServiceNodeViewModelTests.cs`. Existing tests that assert `Children` count after `ExpandAsync` continue to hold because the post-expand `Children.Clear()` + repopulate path is unchanged.

**Checkpoint**: Every existing user-story checkpoint still passes; in addition, a brand-new tree shows chevrons on every device/service node before any interaction, and rows carry distinguishing icons. SC-015 holds.

---

## Phase 14: Secondary window ownership (FR-046, SC-016) — added 2026-05-14

**Goal**: The invocation, subscription, and Diagnostics windows are *owned* by the main window so they always sit z-above it, minimise/restore with it, and close with it. Fixes the WinUI 3 default where secondary `Window` instances are unowned top-level OS windows and can be sent behind the main window by routine focus shifts.

**Independent Test**: Launch the app, expand a device, expand a service, double-click an action to open the invocation popup. Click anywhere on the main window's tree. The popup MUST remain visible on top of the main window. Repeat with the subscription popup (right-click service → Subscribe) and the Diagnostics viewer (View → Diagnostics).

### Implementation

- [X] T152 [FR-046] Create `src/UpnpSpy.App/Platform/OwnedWindowHelper.cs` — static utility with `SetOwner(Window child, IntPtr ownerHwnd)`. Wraps a P/Invoke to `SetWindowLongPtrW(child, GWLP_HWNDPARENT=-8, ownerHwnd)` (with a 32-bit fallback to `SetWindowLongW` for ARM32/x86 builds, even though v1 ships x64/ARM64 only — keeps the helper portable). Child HWND is obtained via `WinRT.Interop.WindowNative.GetWindowHandle(child)`. No-op if `ownerHwnd == IntPtr.Zero`.
- [X] T153 [FR-046] Create `src/UpnpSpy.App/Platform/MainWindowHandleProvider.cs` — DI-managed singleton with a write-once `IntPtr Handle` property and an `Initialize(IntPtr handle)` method that throws on double-initialize. Holds the main window's HWND so popup call sites can ask for it without traversing the UI tree.
- [X] T154 [FR-046] Update `src/UpnpSpy.App/App.xaml.cs` to register `MainWindowHandleProvider` as a DI singleton.
- [X] T155 [FR-046] Update `src/UpnpSpy.App/MainWindow.xaml.cs` to accept `MainWindowHandleProvider` via constructor injection and call `provider.Initialize(WinRT.Interop.WindowNative.GetWindowHandle(this))` immediately after `InitializeComponent()`. Pass the provider down to the `ShellView` it constructs.
- [X] T156 [FR-046] Update `src/UpnpSpy.App/Views/ShellView.xaml.cs` to accept `MainWindowHandleProvider` via constructor and (a) call `OwnedWindowHelper.SetOwner(window, provider.Handle)` in `OnDiagnosticsClicked` between `new DiagnosticsWindow(vm)` and `window.Activate()`, and (b) pass the provider down to `DeviceTreeView`.
- [X] T157 [FR-046] Update `src/UpnpSpy.App/Views/DeviceTreeView.xaml.cs` to accept `MainWindowHandleProvider` via constructor and apply `OwnedWindowHelper.SetOwner(popup, provider.Handle)` in both `OnActionDoubleTapped` (for `InvocationPopup`) and `OnSubscribeClicked` (for `SubscriptionPopup`), between `new …Popup(vm)` and `popup.Activate()`.

### Tests

- T158 — _Not added._ The owner-HWND relationship is a Win32-level property of the OS window manager and is not testable from xUnit without standing up a real WinUI 3 host. SC-016 is validated by the Phase 14 Independent Test above (manual smoke).

**Checkpoint**: Every existing checkpoint still passes; in addition, opening any popup and then clicking back on the main window leaves the popup visibly on top. SC-016 holds.

---

## Phase 15: Hide-until-loaded device tree visibility (FR-047, SC-017) — added 2026-05-14

**Goal**: Devices whose description XML cannot be fetched do not appear in the left-pane tree at all. Their failure is still recorded as a Warning diagnostic, visible to the user via `View → Diagnostics`. Devices that have been discovered but whose fetch is still pending or in flight are also absent from the tree (no transient `uuid:` placeholder labels).

**Independent Test**: Launch the app on a LAN that contains a UPnP device whose description URL is unreachable (or whose XML is malformed). Confirm: (a) the device does NOT appear in the left-pane tree; (b) `View → Diagnostics` contains a Warning entry tagged with the device's UUID and `LOCATION` URL identifying the failure reason; (c) other devices on the LAN appear normally.

### Implementation

- [X] T159 [FR-047] Update `src/UpnpSpy.Core/ViewModels/DeviceTreeViewModel.cs`:
  - In the private constructor's registry-snapshot seeding loop, add a `Device` only when `device.DescriptionFetchState == FetchState.Loaded`.
  - In `OnAdded`, return without adding when the device's state is not `Loaded` (in practice the dispatcher leaves new devices as `NotFetched` until the fetch starts, so the typical `DeviceAdded` event becomes a no-op for the tree).
  - In `OnUpdated`, add a "promotion" branch: if no node currently matches the event's UUID and the device's state is `Loaded`, append a fresh node (this is the moment the eager fetch has completed successfully). The existing in-tree refresh-label branch is unchanged.
  - `OnRemoved` is unchanged.

### Tests

- [X] T160 [P] [FR-047] Update `tests/UpnpSpy.Tests/ViewModels/DeviceTreeViewModelTests.cs`:
  - Add an optional `FetchState` parameter to the `Make` helper (or introduce `MakeLoaded`); update every existing test that depends on a device appearing in the tree to construct it with `state == Loaded`.
  - Rewrite `Device_with_no_friendly_name_falls_back_to_uuid_label` to construct a `Loaded` device with `FriendlyName == null` (a successful fetch whose XML did not contain a `<friendlyName>` element) — this is the legitimate post-FR-047 path for the uuid: fallback.
  - Add new tests: `Device_in_NotFetched_state_does_not_appear_in_tree`, `Device_in_Fetching_state_does_not_appear_in_tree`, `Device_in_Failed_state_does_not_appear_in_tree`, `Failed_device_remains_in_registry_but_not_in_tree`, `Device_promoted_to_Loaded_appears_in_tree_via_DeviceUpdated`.

**Checkpoint**: Every existing user-story checkpoint still passes; in addition, a device whose description fetch fails is absent from the tree but visible in `View → Diagnostics`. SC-017 holds.

---

## Phase 16: Single-adapter operation + ACL-free eventing (FR-048/FR-049/FR-050, SC-018) — added 2026-05-15

**Goal**: Eventing works on an unpackaged non-Administrator developer build with no `netsh http add urlacl` step. The application binds SSDP and the GENA callback host to a single user-selected IPv4 adapter (default: first eligible) chosen from a `View → Network adapter` menu, and the callback host is rebuilt on `System.Net.Sockets.TcpListener` so it never touches Windows HTTP.SYS.

**Independent Test**: Launch `dotnet run` on a clean Windows account with no URL ACL grants. Open `View → Diagnostics` — no `Eventing.Callback bind failure` entries should appear. Right-click any service known to emit events, choose Subscribe, trigger a state change on the device, and confirm event rows appear in the subscription popup. Then open `View → Network adapter`, pick a different adapter, and confirm the tree clears, re-populates from devices reachable on the new adapter, and a fresh subscription on the new adapter also receives events.

### HTTP/1.1 NOTIFY parser

- [ ] T161 [FR-049] Create `src/UpnpSpy.Core/Eventing/HttpRequestReader.cs` — small BCL-only parser exposing `ReadAsync(Stream, CancellationToken, ReaderLimits) → Task<ParsedHttpRequest?>`. Reads the request line (`METHOD SP REQUEST-URI SP HTTP-VERSION CRLF`), header block (lines terminated by CRLF, name and value separated by colon, value left-trimmed; case-insensitive name comparisons; tolerates LWS continuation lines per RFC 7230 §3.2.4 even though UPnP devices don't typically use them), then reads the body up to `Content-Length` bytes (or returns an empty body if the header is absent and the connection-end is the body boundary). Limits: max 8 KB headers, max 64 KB body, max 5 s per-request total wall-time. Returns `null` on framing errors; never throws on user-supplied input. Lives in Core because it is BCL-only and platform-neutral.
- [ ] T162 [FR-049] [P] Create `tests/UpnpSpy.Tests/Eventing/HttpRequestReaderTests.cs` covering: well-formed NOTIFY with Content-Length and small body; case-insensitive header names; tolerated whitespace around header values; oversized header block rejected; oversized body rejected; missing `\r\n` between headers and body returns null; connection closed mid-headers returns null; per-request timeout cancels.

### TcpListener-based callback host

- [ ] T163 [FR-049] Create `src/UpnpSpy.Core/Eventing/TcpListenerEventCallbackHost.cs` — implements `IEventCallbackHost`. `StartAsync(IPAddress localAddress, CancellationToken)` binds `TcpListener(localAddress, port)` where `port` is chosen by the same dynamic-port-walk logic used today (probe 49152 + N for free). Accept loop awaits `AcceptTcpClientAsync`; per-connection `HandleRequestAsync` reads via `HttpRequestReader`, validates `NOTIFY` method + path prefix + NT/NTS/SID/SEQ headers, parses body via existing `GenaNotifyParser`, dispatches into per-registration channels (logic identical to today's `HttpListenerEventCallbackHost`), writes `HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n`, closes the TcpClient. Diagnostic categories preserved: `Eventing.Callback` Warning on malformed input, Error on bind failure. Lives in Core (BCL-only).
- [ ] T164 [FR-049] Update `src/UpnpSpy.Core/Eventing/IEventCallbackHost.cs` so `StartAsync` takes the bound `IPAddress` as a parameter (was: no-parameter; bound to `+`). The `Register(IPAddress preferredLocalAddress)` parameter becomes informational: the host's bound IP wins; document that the parameter is retained only because subscription popups already have an IP in hand from the factory.
- [ ] T165 [FR-049] [P] Create `tests/UpnpSpy.Tests/Eventing/TcpListenerEventCallbackHostTests.cs` — loopback round-trip tests: bind to `127.0.0.1` on a free port, `Register` a callback, fire a real NOTIFY over `TcpClient`, assert it lands in the registration's event stream; assert malformed bodies yield `EventNotification.RawXml` and a Warning diagnostic; assert wrong method / wrong path / missing NT / missing SID gets `400 Bad Request`; assert `UnregisterAsync` followed by a NOTIFY for that token returns `412 Precondition Failed`; assert `DisposeAsync` releases the port.
- [ ] T166 [FR-049] Delete `src/UpnpSpy.App/Platform/HttpListenerEventCallbackHost.cs` and remove its DI registration from `App.xaml.cs`. The MSIX manifest's URL ACL declaration in `Package.appxmanifest` becomes unused; leave the manifest entry alone for now (it does no harm and removing it is a packaging-only concern).

### Network adapter selector

- [ ] T167 [FR-048] Create `src/UpnpSpy.Core/Platform/INetworkAdapterSelector.cs` exposing `IReadOnlyList<EligibleInterface> Available`, `EligibleInterface? Selected`, `Task SelectAsync(EligibleInterface adapter)`, `event Action<AdapterSelectionChanged>? Changed` (where `AdapterSelectionChanged` carries `Previous?` and `Current?`).
- [ ] T168 [FR-048] Create `src/UpnpSpy.Core/Platform/NetworkAdapterSelector.cs` — DI singleton; populates `Available` once at startup via `INetworkInterfaceEnumerator`; defaults `Selected` to `Available[0]` (or null if empty); `SelectAsync` raises `Changed` only when the selection actually moves; thread-safe; tolerates empty-adapter hosts (Warning diagnostic, `Selected == null`).
- [ ] T169 [FR-048] [P] Create `tests/UpnpSpy.Tests/Platform/NetworkAdapterSelectorTests.cs` covering: default selection is the first eligible adapter; empty enumerator yields `Selected == null` and no event; `SelectAsync` raises `Changed` with the correct Previous/Current; re-selecting the same adapter is a no-op (no event); selecting an unknown adapter throws.

### SSDP transport rebinds on selected adapter

- [ ] T170 [FR-004, FR-050] Update `src/UpnpSpy.Core/Ssdp/MulticastSsdpTransport.cs`:
  - Take `INetworkAdapterSelector` (not `INetworkInterfaceEnumerator`) in the constructor.
  - On `StartAsync`, bind exactly one `UdpClient` on `selector.Selected.Ipv4Address`; warn-diagnostic if `Selected == null` (host has no eligible adapters) and exit cleanly.
  - Add `RestartAsync(CancellationToken)` that closes the existing socket and re-binds on the selector's current selection. Idempotent if not yet started.
- [ ] T171 [FR-004, FR-050] [P] Extend `tests/UpnpSpy.Tests/Ssdp/MulticastSsdpTransportTests.cs` (or its equivalent — author one if needed) covering: single-NIC bind based on selector; `RestartAsync` rebinds (assert the old socket is closed and the new one is open); empty selector yields no socket and a Warning.

### Subscription popup factory simplification

- [ ] T172 [FR-048] Update `src/UpnpSpy.Core/ViewModels/SubscriptionPopupFactory.cs`: remove `PickLocalAddressFor` and the `INetworkInterfaceEnumerator` dependency. Take `INetworkAdapterSelector` and read `Selected.Ipv4Address` at popup-creation time (fallback to `IPAddress.Loopback` if no adapter is selected). The popup's callback URL therefore always announces the user-chosen adapter.

### Shell orchestration: adapter switch

- [ ] T173 [FR-050] Update `src/UpnpSpy.Core/ViewModels/ShellViewModel.cs`:
  - Take `INetworkAdapterSelector`, `IEventCallbackHost`, and `DeviceRegistry` in the constructor.
  - In `InitializeAsync`, bind the callback host on `selector.Selected.Ipv4Address` BEFORE starting discovery, then run the SSDP rebind + startup discovery sweep as today.
  - Subscribe to `selector.Changed`; on change post a `RestartAsync` task that: (a) cancels in-flight description fetches via the eager dispatcher's existing per-device CTSs, (b) clears the registry (every device fires `DeviceRemoved` so the tree empties and any open popups receive their "device no longer reachable" signal per FR-037), (c) `await callbackHost.StopAsync()` (new method) + `await callbackHost.StartAsync(newIp)`, (d) `await ssdpTransport.RestartAsync()`, (e) re-runs `DiscoveryService.RunStartupDiscoveryAsync`. All steps are off the UI thread.
  - Expose `IReadOnlyList<EligibleInterface> AvailableAdapters` and `EligibleInterface? SelectedAdapter` properties (the View binds to these); expose an `IAsyncRelayCommand SelectAdapterCommand(EligibleInterface)` that drives `selector.SelectAsync`.

### View menu

- [ ] T174 [FR-048] Update `src/UpnpSpy.App/Views/ShellView.xaml` to add a `View → Network adapter` `MenuBarItem` (sibling of `Rescan` and `Diagnostics`) whose children are a `MenuFlyoutSubItem` populated at construction with one `ToggleMenuFlyoutItem` per `AvailableAdapter`. Selecting an item calls `SelectAdapterCommand`. The currently-selected item is checked.

### DI wiring

- [ ] T175 [FR-048, FR-049] Update `src/UpnpSpy.App/App.xaml.cs`:
  - Remove the `HttpListenerEventCallbackHost` singleton + `IEventCallbackHost` registration.
  - Register `TcpListenerEventCallbackHost` as the singleton implementation of `IEventCallbackHost`.
  - Register `NetworkAdapterSelector` as a singleton implementing `INetworkAdapterSelector`.

### MainWindow

- [ ] T176 [FR-049, FR-050] Update `src/UpnpSpy.App/MainWindow.xaml.cs` — the existing first-activation handler currently calls `_callbackHost.StartAsync(_shutdownSource.Token)` with no IP. Update the call site to delegate startup binding to `ShellViewModel.InitializeAsync` (which now knows the bound IP via the selector). Remove the direct `HttpListenerEventCallbackHost` dependency from MainWindow.

**Checkpoint**: Every existing user-story checkpoint still passes; in addition, eventing works on a clean non-Admin developer build with no URL ACL ever granted; switching the adapter via the View menu rebinds everything and rediscovers on the new NIC. SC-018 holds.

---

## Phase 17: Device row disambiguator + Properties window (FR-051, FR-052, SC-019) — added 2026-05-15

**Goal**: Make co-resident root devices (canonical case: an IGD router's three rows) visually distinct in the tree, and give power users a single-click way to inspect every field UpnpSpy has captured for a device.

**Independent Test**: Run against a LAN that contains a UPnP router. Confirm the tree shows several rows with the same friendly name, and that each row has a distinct `<deviceType-tail> · <ip:port>` secondary line. Right-click any device → `Properties…` opens a window showing manufacturer, model, serial, presentationURL (clickable), SSDP `SERVER` header, CACHE-CONTROL max-age, BOOTID/CONFIGID (if any), first-seen / last-seen timestamps, and the list of embedded `<device>` children.

### Parser + model

- [ ] T177 [FR-051, FR-052] Extend `src/UpnpSpy.Core/Description/DeviceDescription.cs` (and the parser-internal `ServiceDescriptor` stays unchanged) to carry `DeviceType`, `Manufacturer`, `ManufacturerUrl`, `ModelName`, `ModelDescription`, `ModelNumber`, `ModelUrl`, `SerialNumber`, `Upc`, `PresentationUrl`, and a recursive `EmbeddedDevices` list (`IReadOnlyList<EmbeddedDeviceSummary>` with `Udn`, `DeviceType`, `FriendlyName?`, `EmbeddedDevices`). All except `DeviceType` are nullable (UDA 1.0 §2.3 marks `friendlyName` / `manufacturer` / `manufacturerURL` etc. as `O` "optional").
- [ ] T178 [FR-051, FR-052] Extend `src/UpnpSpy.Core/Description/DeviceDescriptionXmlParser.cs` to read each new element from the root `<device>` and from each embedded `<device>` recursively. URL fields (`manufacturerURL`, `modelURL`, `presentationURL`) MUST be resolved against the response's effective base URI like the existing `SCPDURL`/`controlURL`/`eventSubURL`; absent or malformed values become `null` without aborting the parse.
- [ ] T179 [FR-051, FR-052] [P] Extend `tests/UpnpSpy.Tests/Description/DeviceDescriptionXmlParserTests.cs` to cover: a description with every metadata field populated; a description with only the required fields; embedded `<device>` children show up in `EmbeddedDevices` recursively; relative `presentationURL` is resolved.
- [ ] T180 [FR-051, FR-052] Extend `src/UpnpSpy.Core/Models/Device.cs` with `DeviceType`, `Manufacturer`, `ManufacturerUrl`, `ModelName`, `ModelDescription`, `ModelNumber`, `ModelUrl`, `SerialNumber`, `Upc`, `PresentationUrl`, `EmbeddedDevices`, `ServerHeader`, `CacheControlMaxAge`, `BootId`, `ConfigId`, `FirstSeenUtc`, and `AliveCount`. Add a `DetailLabel` computed property returning `"{deviceTypeTail} · {ip}:{port}"` when both pieces are present (with sensible fallbacks: deviceType-only or empty string if neither is known).

### Field propagation

- [ ] T181 [FR-051, FR-052] Update `src/UpnpSpy.Core/Discovery/EagerDescriptionDispatcher.cs` `ApplySuccess` to copy every new field from `DeviceDescription` onto `Device` (alongside the existing `FriendlyName`/`Services` copy). Failure path is unchanged — failed devices are absent from the tree per FR-047 so the metadata fields stay default.
- [ ] T182 [FR-051, FR-052] Update `src/UpnpSpy.Core/Discovery/DiscoveryService.cs` so that every alive call to `_registry.TryAddOrUpdate(...)` propagates `SsdpNotifyMessage.Server`, `CacheControlMaxAge`, `BootId`, `ConfigId` onto the candidate `Device`. Update `MulticastSsdpTransport`/`SsdpSearchResponse` parsing if any required field isn't currently in scope.
- [ ] T183 [FR-051, FR-052] Update `src/UpnpSpy.Core/Discovery/DeviceRegistry.cs` `MergeInto` to: (a) set `FirstSeenUtc` on the first insert; (b) increment `AliveCount` on each alive update; (c) accept newer `ServerHeader` / `CacheControlMaxAge` / `BootId` / `ConfigId` values from the candidate, mutating only when the new value is non-null.

### Tier 1 — tree row template

- [ ] T184 [FR-051] Update `src/UpnpSpy.App/Views/DeviceTreeView.xaml` `DeviceTemplate` so the row layout is a vertical `StackPanel`: top child = friendly name (existing `Label` binding); bottom child = `DetailLabel` rendered with `Foreground={ThemeResource SystemControlDescriptionTextForegroundBrush}` and a slightly smaller font. The leading FontIcon stays on the row.
- [ ] T185 [FR-051] Update `src/UpnpSpy.Core/ViewModels/DeviceNodeViewModel.cs` to expose `DetailLabel` (proxies `Device.DetailLabel`) and refresh it in `RefreshLabel`.

### Tier 3 — Properties window

- [ ] T186 [FR-052] Create `src/UpnpSpy.Core/ViewModels/DevicePropertiesViewModel.cs` — display-only `ObservableObject`. Constructor takes a `Device`. Exposes flat properties for every section (Identity / Manufacturer / Network / Discovery / Embedded), with `"—"` string placeholders for null fields so the binding renders unambiguously. Subscribes to `DeviceRegistry.DeviceRemoved` to flip an `IsDeviceUnreachable` flag (FR-037).
- [ ] T187 [FR-052] Create `src/UpnpSpy.Core/ViewModels/DevicePropertiesPopupFactory.cs` — singleton with `Create(Device)` that builds a `DevicePropertiesViewModel` wired to the registry's events.
- [ ] T188 [FR-052] Create `src/UpnpSpy.App/Views/DevicePropertiesWindow.xaml(.cs)` — a secondary `Window` bound to the VM. ScrollViewer with grouped sections. Each section is a labelled grid with two columns (field name / value). URL-typed values render as HyperlinkButton. Embedded devices section is a TreeView or simple ItemsControl.
- [ ] T189 [FR-052] Update `src/UpnpSpy.App/Views/DeviceTreeView.xaml` to add a `MenuFlyoutItem` "Properties…" to the device context menu (alongside "Fetch XML"). Wire its `Click` handler in the code-behind to build a `DevicePropertiesViewModel` via the factory, open a `DevicePropertiesWindow`, and apply `OwnedWindowHelper.SetOwner(window, _handleProvider.Handle)` before `Activate()`.
- [ ] T190 [FR-052] Update `src/UpnpSpy.App/Views/DeviceTreeView.xaml.cs` constructor to take `DevicePropertiesPopupFactory`; update `src/UpnpSpy.App/Views/ShellView.xaml.cs` to pass it through.
- [ ] T191 [FR-052] Update `src/UpnpSpy.Core/Composition/CoreServiceCollectionExtensions.cs` to register `DevicePropertiesPopupFactory` as a singleton.

### Tests

- [ ] T192 [P] [FR-051, FR-052] Add `tests/UpnpSpy.Tests/Models/DeviceTests.cs` (if it doesn't exist) covering `DetailLabel`: full case (deviceType + ip + port); deviceType-only; empty when nothing is known.
- [ ] T193 [P] [FR-052] Add `tests/UpnpSpy.Tests/ViewModels/DevicePropertiesViewModelTests.cs` covering: every field is surfaced from the Device; null fields render as `"—"`; `DeviceRemoved` for the matching UUID flips `IsDeviceUnreachable`; `DeviceRemoved` for a different UUID is a no-op.
- [ ] T194 [P] [FR-051, FR-052] Extend the parser tests in T179 above (they cover the new fields end-to-end through `DeviceDescription`).

**Checkpoint**: Every existing user-story checkpoint still passes; the tree row's secondary line tells co-resident root devices apart (SC-019); right-clicking a device opens a Properties window with every captured field. Tier 2 (hover tooltip) is intentionally deferred.

---

## Phase 18: Diagnostics row identity columns (FR-041, SC-014) — added 2026-05-15

**Goal**: Surface each diagnostic entry's affiliated device (friendly name or UUID) and endpoint (host:port) as first-class columns in `View → Diagnostics`, so the user can correlate a failure to a specific device on the LAN at a glance.

**Independent Test**: With a populated tree, force a description-fetch failure (e.g., block port 80 to a known device) and open `View → Diagnostics`. The new failure row's **Identity** column shows the device's friendly name (or `uuid:<uuid>` if it never resolved) and the **Endpoint** column shows the IP and port from the LOCATION URL. Entries that carry no device context (`App.Lifecycle`, raw `Ssdp.Parse` failures for fully-malformed datagrams) render the placeholder in those columns.

- [ ] T195 [FR-041] Extend `src/UpnpSpy.Core/ViewModels/DiagnosticsViewerViewModel.cs` to take `DeviceRegistry` in its constructor and project incoming `DiagnosticEntry` records into a new `DiagnosticEntryRow` class (in the same file, mirroring the `EventPropertyRow` pattern in `SubscriptionPopupViewModel.cs`). The row exposes the underlying `Entry` plus computed `Identity` and `Endpoint` strings, plus pass-through `Timestamp` / `Severity` / `Category` / `Message` so `x:Bind` and tests stay terse. Resolution is snapshot-at-arrival: identity from `Context["device.uuid"]` (friendlyName via registry lookup, else `"uuid:<uuid>"`, else `"—"`); endpoint from `Context["url"]` (URI host:port, default-port elided), falling back to `Context["remote.endpoint"]`, else `"—"`. Both the `Snapshot()` priming pass in `Start()` and the live `OnEntry` path MUST run entries through the same projection.

- [ ] T196 [FR-041] Update `src/UpnpSpy.App/Views/DiagnosticsWindow.xaml` to add two columns to the `ListView`'s `DataTemplate` between the existing Category and Message: an **Identity** column bound to `Identity` (width ~180) and an **Endpoint** column bound to `Endpoint` (width ~140, monospace). The bound type changes from `models:DiagnosticEntry` to `vm:DiagnosticEntryRow`; `Timestamp` / `Severity` / `Category` / `Message` bindings stay unchanged because the row exposes pass-through properties.

- [ ] T197 [FR-041] Update `src/UpnpSpy.Core/Composition/CoreServiceCollectionExtensions.cs` so the transient `DiagnosticsViewerViewModel` factory passes the singleton `DeviceRegistry` alongside the existing `IDiagnosticBuffer` + `IDispatcher`.

- [ ] T198 [P] [FR-041] Extend `tests/UpnpSpy.Tests/ViewModels/DiagnosticsViewerViewModelTests.cs` with cases for the new resolution: identity is friendlyName when registry has one; identity is `"uuid:<uuid>"` when device known but name absent; identity is `"uuid:<uuid>"` when device not in registry; identity is `"—"` when entry has no `device.uuid` context; endpoint is `host:port` from `url`; endpoint elides default port; endpoint falls back to `remote.endpoint` when `url` is absent; endpoint is `"—"` when neither is present. Update the existing constructor calls to pass a `DeviceRegistry`.

**Checkpoint**: Existing diagnostic acceptance scenarios continue to hold; SC-014 strengthened — the affected device is identifiable without expanding any row.

---

## Phase 19: Alphabetical device-tree ordering (FR-054) — added 2026-05-18

**Goal**: Replace the previous "fetch-completion order" of left-pane device rows with case-insensitive alphabetical ordering by friendly name (UUID tiebreak), so users can find a specific device on busy networks without scanning a non-deterministic list.

**Independent Test**: On a LAN with at least three discoverable devices, launch the app. Once descriptions resolve, the device rows appear in alphabetical order of their friendly names regardless of fetch order. Force a rescan; the order is preserved. Rename a device on the network and re-announce it; the row migrates to its new alphabetical position without losing selection.

- [X] T199 [FR-054] Update `src/UpnpSpy.Core/ViewModels/DeviceTreeViewModel.cs`: route every insertion (constructor snapshot, `OnAdded`, `OnUpdated`'s promotion branch) through a private `InsertSorted` helper that scans for the correct index using a `(Label, Uuid)` comparator — case-insensitive on `Label`, ordinal on `Uuid` for stability. In `OnUpdated`'s in-place label-refresh branch, call a new `ResortIfNeeded(currentIndex)` that uses `ObservableCollection<T>.Move` when a label change crosses a neighbour boundary, so WinUI raises a single Move and the node identity (selection / expansion state) survives.

- [X] T200 [P] [FR-054] Extend `tests/UpnpSpy.Tests/ViewModels/DeviceTreeViewModelTests.cs` with: out-of-order arrivals end up alphabetically sorted; constructor-seeded snapshot is sorted; rename moves the row and preserves node identity; duplicate-label rows order stably by UUID; a late `Loaded` promotion inserts at the correct sorted position (not appended).

**Checkpoint**: User Story 1 / 2 acceptance still holds; the rename edge case (spec.md Edge Cases) is upgraded from "label updates" to "label updates **and** row migrates"; the discovery-burst edge case no longer leaks fetch-completion order into the user-visible UI.

---

## Phase 20: Newest-first SSDP log ordering (FR-003, FR-055) — added 2026-05-18

**Goal**: Reorder the right-pane SSDP NOTIFY log so the most recently received advertisement is at the top of the list and the oldest retained advertisement is at the bottom — the natural reading order for a live diagnostic stream. FIFO eviction at the 10,000-entry cap (FR-016) is preserved: the bottom row is the one evicted.

**Independent Test**: Launch the app on a chatty LAN. The first row to appear is at the top. As further advertisements arrive, new rows push older ones downward; the user's eye does not have to chase a moving tail. Scroll down to read older entries; the list does not yank back to the top on every new arrival. Stress-evict (≥10,001 entries) and confirm the oldest entries are dropped off the bottom, not the top.

- [ ] T201 [FR-055] Update `src/UpnpSpy.Core/ViewModels/SsdpLogViewModel.cs`: construct the `BoundedObservableCollection<SsdpLogEntry>` with `BoundedEvictionMode.EvictTail`, and change `Append` to `Entries.Insert(0, entry)` instead of `Entries.Add(entry)`. Capacity (10,000), dispatcher posting, and the null guard stay unchanged.

- [ ] T202 [FR-055] Update `src/UpnpSpy.App/Views/SsdpLogView.xaml.cs`: invert the auto-follow. The sticky zone becomes the *top* (`_autoScrollEnabled = _scrollViewer.VerticalOffset <= StickyTopThresholdPx`), and `OnEntriesChanged` scrolls to `ViewModel.Entries[0]` rather than `ViewModel.Entries[^1]`. Rename the threshold constant to reflect the new semantics.

- [ ] T203 [P] [FR-055] Update `tests/UpnpSpy.Tests/ViewModels/SsdpLogViewModelTests.cs`: flip the insertion-order test (newest expected at index 0, oldest at `^1`); flip the capacity-eviction test (the bottom entries are evicted, so after `a, b, c, d` with capacity 3 the expected order is `d, c, b`); flip the stress test (index 0 = newest UUID, `^1` = the oldest retained one).

**Checkpoint**: User Story 4 acceptance scenarios still hold under the new direction; FR-016 eviction is still FIFO (oldest discarded) but evicts from the tail; the User Story 4 acceptance #3 phrasing (do not jump back to the top on every new arrival) is now what `SsdpLogView.xaml.cs` enforces.

---

## Dependencies & Execution Order

### Phase dependencies

- **Phase 1 (Setup)** has no dependencies and can start immediately.
- **Phase 2 (Foundational)** depends on Phase 1 and **blocks every user story**.
- **Phase 3 (US1)** depends only on Phase 2.
- **Phase 4 (US2)** depends on Phase 3 (it extends `DiscoveryService`/`DeviceRegistry` from US1, but the work is independently testable as a separate increment).
- **Phase 5 (US3)** depends on Phase 2; the description/SCPD fetchers themselves are independent of US1/US2, but the lazy-expand wiring in T083–T085 plugs into the `DeviceTreeViewModel` introduced in US1. US3 can be implemented in parallel with US2 if staffed.
- **Phase 6 (US4)** depends on Phase 2 and on the `DiscoveryService` receive pump from US1; the right-pane view itself is independent.
- **Phase 7 (US5)** depends on Phase 2 and on the device/service node view-models introduced in US1/US3.
- **Phase 8 (US6)** depends on Phase 2, on `ISsdpTransport`/`DeviceRegistry` from US1, and on the menu plumbing introduced in US5 (it reuses the `MenuBar` host added in T102).
- **Phase 9 (US7)** depends on Phase 2 and on the action nodes introduced in US3.
- **Phase 10 (US8)** depends on Phase 2 and on the service nodes / context menu introduced in US3/US5; it also exercises the diagnostic infrastructure from Foundational heavily.
- **Phase 11 (Polish)** depends on every user story whose features it cross-references; the diagnostic viewer (T128–T131) only requires Foundational and so could be pulled forward if useful for debugging earlier stories.
- **Phase 12 (Eager description fetch, FR-043)** depends on Phase 3 (US1: `DeviceRegistry`, `DiscoveryService`, `DeviceNodeViewModel`) and Phase 5 (US3: `IDeviceDescriptionFetcher` + parser). It does not introduce a new user story — it amends the behaviour of US1's tree-labelling and US3's expansion path. Tasks T138–T147 supersede the lazy-fetch portion of T083; the test suite must be re-run after this phase to confirm US1/US3 acceptance still holds with the new wiring.
- **Phase 13 (Tree affordance, FR-044/FR-045)** depends on Phase 3 (US1: `DeviceNodeViewModel` exists), Phase 5 (US3: `ServiceNodeViewModel` exists and `DeviceTreeView.xaml` already declares per-node templates), and Phase 12 (placeholder semantics align with the FR-043 state-machine). It does not introduce a new user story — it makes the expand affordance and node kind visible in the UI. Tasks T148–T151.
- **Phase 14 (Secondary window ownership, FR-046)** depends on Phase 7 (US5: `InvocationPopupFactory` + `SubscriptionPopupFactory` constructed in `DeviceTreeView`), Phase 10 (US8: `SubscriptionPopup` exists), and Phase 11 (`DiagnosticsWindow` exists in `ShellView`). It does not introduce a new user story — it corrects the z-order behaviour of every secondary window opened by the app. Tasks T152–T158.
- **Phase 15 (Hide-until-loaded visibility, FR-047)** depends on Phase 3 (US1: `DeviceTreeViewModel`), Phase 5 (US3: `DescriptionFetchState` lifecycle), and Phase 12 (FR-043: eager dispatcher transitions and `NotifyUpdated`). It revises FR-009/FR-010/FR-043's "device stays with fallback label on fetch failure" rule into "device hidden from tree on fetch failure; diagnostic still recorded." Tasks T159–T160.
- **Phase 16 (Single-adapter operation + ACL-free eventing, FR-048/FR-049/FR-050)** depends on Phase 3 (US1: `MulticastSsdpTransport`, `INetworkInterfaceEnumerator`), Phase 10 (US8: `IEventCallbackHost`, `SubscriptionPopupFactory`), and Phase 11 (`ShellView` `View` menu host). It revises FR-004's "every up, non-loopback IPv4 interface" model into "one user-selected adapter" and replaces the `HttpListener`-based callback host with a `TcpListener`-based one so no URL ACL is ever required. Tasks T161–T176.
- **Phase 17 (Device row disambiguator + Properties window, FR-051/FR-052)** depends on Phase 3 (US1: `DeviceTreeViewModel`, `DeviceNodeViewModel`), Phase 5 (US3: `DeviceDescriptionXmlParser`), Phase 7 (US5: device context menu in `DeviceTreeView.xaml`), Phase 12 (FR-043: eager dispatcher copies description fields), and Phase 14 (FR-046: `OwnedWindowHelper` and `MainWindowHandleProvider` for the new secondary window). Tasks T177–T194.
- **Phase 18 (Diagnostics row identity columns, FR-041 / SC-014)** depends on Phase 2 (Foundational: `DiagnosticEntry`, `RingDiagnosticBuffer`, `DeviceRegistry`) and Phase 11 (`DiagnosticsViewerViewModel`, `DiagnosticsWindow`). It does not introduce a new user story — it enriches the Diagnostics viewer so each row carries device-affiliation columns. Tasks T195–T198.

### Within each user story

- Domain records/interfaces and parsers before the service that consumes them.
- View-model before view.
- Tests for non-trivial parsers/builders/view-models are listed alongside (and may be authored first when the developer wants a TDD pass, per Constitution Principle IV).
- Each user-story phase ends at its **Checkpoint** in a state that satisfies that story's **Independent Test**.

### Parallel opportunities

- All Phase-2 model files (T012–T025) are independent file additions — fully parallelizable.
- The four contract pair (interface + result union + record types) groups within US3 (T071–T073, T078) and US8 (T114–T115, T120) are independent and parallelizable per group.
- Test files marked [P] within each phase can be authored alongside the production code they cover.
- Different user stories can be developed by different team members in parallel after Foundational completes; the only cross-story integration points are the menu hosts in `ShellView.xaml`/`DeviceTreeView.xaml` and the `DeviceTreeViewModel`.

---

## Parallel Example: Phase 2 (Foundational) — domain models

```text
# These twelve file additions touch disjoint files and have no dependencies on
# each other; they can all be authored simultaneously:

Task: T012 Create src/UpnpSpy.Core/Models/FetchState.cs
Task: T013 Create src/UpnpSpy.Core/Models/ArgumentDirection.cs
Task: T014 Create src/UpnpSpy.Core/Models/ArgumentDefinition.cs
Task: T015 Create src/UpnpSpy.Core/Models/StateVariableDefinition.cs
Task: T016 Create src/UpnpSpy.Core/Models/ActionDefinition.cs
Task: T017 Create src/UpnpSpy.Core/Models/Service.cs
Task: T018 Create src/UpnpSpy.Core/Models/Device.cs
Task: T019 Create src/UpnpSpy.Core/Models/SsdpKind.cs + SsdpLogEntry.cs
Task: T020 Create src/UpnpSpy.Core/Models/DiscoverySession.cs + DiscoverySessionState.cs
Task: T021 Create src/UpnpSpy.Core/Models/InvocationRequest.cs
Task: T022 Create src/UpnpSpy.Core/Models/InvocationResult.cs
Task: T023 Create src/UpnpSpy.Core/Models/SubscriptionState.cs + SubscriptionStatus.cs
Task: T024 Create src/UpnpSpy.Core/Models/EventNotification.cs
Task: T025 Create src/UpnpSpy.Core/Models/DiagnosticEntry.cs + DiagnosticSeverity.cs
```

## Parallel Example: User Story 1 — independent tests

```text
# Authored while the corresponding production code is being written by someone else
# (or by the same developer in a TDD-first pass):

Task: T052 [P] [US1] Create tests/UpnpSpy.Tests/Ssdp/SsdpMessageParserTests.cs
Task: T057 [P] [US1] Create tests/UpnpSpy.Tests/Discovery/DeviceRegistryTests.cs
Task: T059 [P] [US1] Create tests/UpnpSpy.Tests/Discovery/DiscoveryServiceTests.cs
Task: T062 [P] [US1] Create tests/UpnpSpy.Tests/ViewModels/DeviceTreeViewModelTests.cs
```

---

## Implementation Strategy

### MVP first (User Story 1 only)

1. Complete Phase 1: Setup (T001–T011).
2. Complete Phase 2: Foundational (T012–T047). **Critical**: this phase blocks every user story.
3. Complete Phase 3: User Story 1 (T048–T066).
4. **STOP and VALIDATE**: Independent Test from spec User Story 1 — launch the app on a LAN, see a populated tree. Demo-ready.

### Incremental delivery

1. Setup + Foundational → infrastructure ready.
2. + US1 → MVP demo.
3. + US2 → tree is now a live view, not just a startup snapshot.
4. + US3 → expansion shows services and actions; tool is now an "explorer".
5. + US4 → right-pane SSDP log; tool is now also a "diagnostic".
6. + US5 → right-click XML viewing; tool is now also a "power-user inspector".
7. + US6 → View > Rescan; tool can recover from network-change scenarios cleanly.
8. + US7 → action invocation; tool is now an active "control point".
9. + US8 → eventing subscription; tool is now a full UPnP diagnostic suite.
10. + Polish (View > Diagnostics window, MSIX packaging, performance/memory verification) → ship-ready.

### Parallel team strategy

With multiple developers, after Foundational (Phase 2) completes:

- **Developer A**: US1 → US2 (the discovery spine).
- **Developer B**: US3 (description/SCPD fetch and lazy expand) in parallel with US4 (right-pane log) — different files, no overlap with A after US1's `DeviceTreeViewModel` lands.
- **Developer C**: starts on US5/US6 (browser launch, rescan) once US3 has produced `ServiceNodeViewModel`.
- **Developer D**: takes on US7 (invocation) once US3's `ActionDefinition`/`ActionNodeViewModel` exist.
- **Developer E**: takes on US8 (eventing) — the largest single user story; can begin in parallel with US7 since it touches different files.
- Polish (Phase 11) is shared and runs after each developer's user story stabilizes.

---

## Notes

- Every task carries a checkbox, sequential ID, optional `[P]` parallel marker, story label (only in user-story phases), and a file path. The strict format matches the speckit-tasks Task Generation Rules.
- Tests are first-class deliverables per Constitution Principle IV; they are listed alongside the production code they cover rather than in a separate phase.
- The diagnostic logging infrastructure (FR-039–FR-042) is split: the cross-cutting plumbing (`IDiagnosticSink`, `RingDiagnosticBuffer`, `RollingFileDiagnosticSink`, `DiagnosticLoggerProvider`, `CompositeDiagnosticSink`) is in Foundational so every other phase can emit Warning entries through `Microsoft.Extensions.Logging`. The user-visible `View > Diagnostics` window (FR-041) is in Phase 11 because no single user story owns it.
- Multicast-blocked / firewall scenarios (spec Edge Cases) are covered by FakeSsdpTransport tests of `DiscoveryService` and by the empty-tree user-visible behaviour — no separate task; an empty result is already exercised by US1 acceptance scenario #4.
- The `View > Rescan` and `View > Diagnostics` menu items share the `MenuBar` host added in T102 (US6). T131 (Polish) only adds the menu item, not the menu.
- "Commit after each task or logical group" — the `speckit.git.commit` after-hook is registered in `.specify/extensions.yml` and will be offered after this generation completes.

---

## Extension Hooks

**Optional Hook**: git
Command: `/speckit-git-commit`
Description: Auto-commit after task generation

Prompt: Commit task changes?
To execute: `/speckit-git-commit`
