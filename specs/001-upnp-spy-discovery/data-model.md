# Phase 1 Data Model — UpnpSpy

**Feature**: 001-upnp-spy-discovery
**Date**: 2026-05-12
**Scope**: In-memory data structures only. v1 has no persistent application state (the rolling log file is operational telemetry, not domain state). All types live in `UpnpSpy.Core/Models/` unless noted.

Every entity below is named in the spec's *Key Entities* section. Field types are written in C#-flavoured pseudocode; final declarations live in code generated from the `/speckit-implement` phase.

---

## 1. Device

A discovered UPnP root device. Identity is its UUID (FR-007).

| Field | Type | Notes |
|---|---|---|
| `Uuid` | `string` (record key) | Bare UUID extracted from the SSDP `USN` header (`uuid:<UUID>::...`). UDA 1.0 §1.3. |
| `FriendlyName` | `string?` | From device description `<friendlyName>`. Nullable until the description has been fetched, or if the device omits it. |
| `Label` | `string` (computed) | `FriendlyName` if non-empty, else `"uuid:" + Uuid`. Drives the tree node text (FR-009/FR-010). |
| `LocationUrl` | `Uri` | From the SSDP `LOCATION` header. Used for "Fetch XML" (FR-019) and for description retrieval (FR-011). |
| `DescriptionFetchState` | `enum FetchState { NotFetched, Fetching, Loaded, Failed }` | Transitions `NotFetched → Fetching` automatically on registry add (eager fetch, FR-043). The UI consumes the terminal state on expansion: `Loaded` → hydrate children from the cached `Services`; `Fetching` → transient "Loading…" placeholder; `Failed` → FR-013 inline error placeholder. |
| `DescriptionFetchError` | `string?` | Populated when `DescriptionFetchState == Failed`; shown inline (FR-013). |
| `Services` | `IReadOnlyList<Service>` | Empty until description is loaded. Contains the **flattened union** of the root device's `<serviceList>` and every `<serviceList>` declared in nested `<deviceList>` elements (walked recursively). Each `Service` records its `ContainingDeviceUdn`/`ContainingDeviceFriendlyName` so embedded provenance is preserved for display and identity (see §2). UDA 1.0 §2.1; research §20. |
| `LastSeenUtc` | `DateTimeOffset` | Most-recent alive/M-SEARCH-response timestamp. Used by `DiscoverySession` for rescan pruning. |
| `ObservedOnInterfaces` | `IReadOnlySet<string>` | Names of NICs on which the device has been heard. Informational; not surfaced in v1 UI but recorded for diagnostics. |

**Lifetime / state transitions**:

```text
SSDP alive received → if Uuid unknown → create Device (DescriptionFetchState=NotFetched) and add to registry; emit DeviceAdded
                                       → EagerDescriptionDispatcher enqueues a fetch (FR-043); DescriptionFetchState=Fetching
                                          on success → FriendlyName, Services populated, DescriptionFetchState=Loaded; emit DeviceUpdated (label refresh + Fetching→Loaded transition signal)
                                          on failure → DescriptionFetchState=Failed, DescriptionFetchError set, Services=empty; emit DeviceUpdated (Fetching→Failed transition signal; label stays on FR-010 fallback so the tree-VM's RefreshLabel is a visual no-op)
SSDP alive for known Uuid → update LastSeenUtc, FriendlyName (if a fresh alive carries one and it differs from the description-provided value), ObservedOnInterfaces
                          → **do not** re-trigger description fetch (cached for the lifetime of this registry entry)
User expands node, DescriptionFetchState==Loaded   → DeviceNodeViewModel hydrates Children from Device.Services (no I/O)
User expands node, DescriptionFetchState==Fetching → DeviceNodeViewModel shows a transient "Loading…" placeholder; replaced when fetch completes
User expands node, DescriptionFetchState==Failed   → DeviceNodeViewModel shows the FR-013 inline error placeholder
SSDP byebye received → remove Device from registry; emit DeviceRemoved (triggers FR-037 in any open popup); EagerDescriptionDispatcher cancels any in-flight fetch for this UUID
Rescan session ends without hearing Uuid → remove Device; emit DeviceRemoved; same fetch-cancellation rule as byebye
```

**Invariants**: `Uuid` is non-empty; `Label` never empty; `LocationUrl` set at construction; `Services` is empty iff `DescriptionFetchState != Loaded`. **`DescriptionFetchState` MUST leave `NotFetched` no later than the moment the device is observable to the UI** (the dispatcher enqueues the fetch synchronously with the registry add).

**Spec citations**: FR-005, FR-007, FR-008, FR-009, FR-010, FR-011, FR-013, FR-043.

---

## 2. Service

A capability advertised by a device. The owning tree node is always the **root** device (FR-002 tree is exactly three levels); embedded-child services merge into the root device's `Services` list, distinguished from root services by `ContainingDeviceUdn`.

| Field | Type | Notes |
|---|---|---|
| `OwningDeviceUuid` | `string` | The **root** device's UUID. Foreign reference back to `Device.Uuid`. For services declared by an embedded child, this is still the root UUID — provenance lives in `ContainingDeviceUdn`. |
| `ContainingDeviceUdn` | `string` | UDN of the `<device>` element that actually declared this `<service>` (root or any descendant). Equal to `"uuid:" + OwningDeviceUuid` when the service was declared by the root device. UDA 1.0 §2.1; research §20. |
| `ContainingDeviceFriendlyName` | `string?` | `<friendlyName>` of the containing device, or `null` if the embedded child omits it (or if it equals the root's friendly name). Used by `Label` to disambiguate same-typed services across embedded children. |
| `ServiceId` | `string` | From SCPD `<serviceId>` (e.g., `urn:upnp-org:serviceId:AVTransport`). Identity is `(ContainingDeviceUdn, ServiceId)` within the root device. |
| `ServiceType` | `string` | From `<serviceType>` (e.g., `urn:schemas-upnp-org:service:AVTransport:1`). Used as the `SOAPACTION` and `urn:` prefix in invocation. |
| `ScpdUrl` | `Uri` | From device description `<SCPDURL>`, resolved against the description response's effective base URI (not against the containing `<device>` element). UDA 1.0 §2.1; research §20. |
| `ControlUrl` | `Uri` | From `<controlURL>`, resolved against the description response's effective base URI. |
| `EventSubUrl` | `Uri` | From `<eventSubURL>`, resolved against the description response's effective base URI. |
| `Label` | `string` (computed) | When `ContainingDeviceUdn == "uuid:" + OwningDeviceUuid`: `ServiceType` last colon-separated segment (e.g., `AVTransport:1`), falling back to `ServiceId`. When the service comes from an embedded child: prefixed with `ContainingDeviceFriendlyName` (or the embedded UDN if no friendly name) — e.g., `"Tuner · AVTransport:1"`. |
| `ScpdFetchState` | `enum FetchState` | Same enum as `Device.DescriptionFetchState`. |
| `ScpdFetchError` | `string?` | Populated when `ScpdFetchState == Failed` (FR-013). |
| `Actions` | `IReadOnlyList<ActionDefinition>` | Populated when `ScpdFetchState == Loaded`. UDA 1.0 §2.2. |
| `StateVariables` | `IReadOnlyList<StateVariableDefinition>` | Parsed from SCPD; used to interpret events. UDA 1.0 §2.2. |

**Lifetime / state transitions**: same lazy pattern as `Device.DescriptionFetchState`, triggered by service node expansion.

**Invariants**: `ServiceId`, `ServiceType`, `ContainingDeviceUdn` non-empty; the three URLs all resolved (absolute) before the service is exposed to the view-model. Two services within the same root device MUST NOT share `(ContainingDeviceUdn, ServiceId)` — the parser emits a `Warning` `Description.Parse` diagnostic and drops the duplicate if a description violates this (research §20).

**Spec citations**: FR-002, FR-011, FR-012, FR-013, FR-018; spec Assumptions (embedded devices).

---

## 3. ActionDefinition

A named operation belonging to a service.

| Field | Type | Notes |
|---|---|---|
| `Name` | `string` | SCPD `<name>` of the action. Identity within a service. |
| `Inputs` | `IReadOnlyList<ArgumentDefinition>` | Ordered, matches SCPD declaration order — important for SOAP envelope assembly. UDA 1.0 §3.1.1. |
| `Outputs` | `IReadOnlyList<ArgumentDefinition>` | Ordered. |

**Invariants**: `Name` non-empty; both lists possibly empty (FR-031: actions with zero inputs or zero outputs are first-class).

**Spec citations**: FR-012, FR-025, FR-026, FR-027, FR-028, FR-031.

---

## 4. ArgumentDefinition

A named, typed parameter belonging to an action (input or output).

| Field | Type | Notes |
|---|---|---|
| `Name` | `string` | SCPD `<name>` of the argument. |
| `Direction` | `enum ArgumentDirection { In, Out }` | From SCPD `<direction>`. |
| `RelatedStateVariable` | `string` | SCPD `<relatedStateVariable>`. Indirection used by SCPD to declare the argument's type via the service's state variable table. UDA 1.0 §2.2. |
| `DataType` | `string?` | Resolved from the related state variable's `<dataType>` (e.g., `ui4`, `string`, `boolean`). Optional in v1 — surfaced for display only; v1 does not type-check user input (spec Assumptions). |

**Invariants**: `Name` non-empty; `Direction` set.

**Spec citations**: FR-026, FR-028.

---

## 5. StateVariableDefinition

Auxiliary entity: the SCPD-declared state variables for a service. Not surfaced in the tree in v1, but needed to (a) resolve argument data types and (b) interpret evented updates.

| Field | Type | Notes |
|---|---|---|
| `Name` | `string` | SCPD `<name>`. |
| `DataType` | `string` | SCPD `<dataType>`. |
| `SendsEvents` | `bool` | SCPD `<stateVariable sendEvents="yes/no">`. |
| `AllowedValues` | `IReadOnlyList<string>?` | From `<allowedValueList>` if present (informational v1). |

---

## 6. SsdpLogEntry

One row in the right-pane SSDP message log.

| Field | Type | Notes |
|---|---|---|
| `ReceivedUtc` | `DateTimeOffset` | Timestamp of receipt. Displayed in local time. FR-014/015. |
| `Kind` | `enum SsdpKind { Alive, Byebye }` | Distinct enum from `SubscriptionEventKind` etc. |
| `DeviceUuid` | `string` | Extracted from `USN`. FR-014/015. |
| `Nt` | `string` | Optional helpful detail (e.g., `upnp:rootdevice`, service type) — not strictly required by FR-014/015 but cheap to store and useful when explaining "why am I seeing this row?" diagnostically. Omitted from the default visible columns; reserved for a future detail expander. |
| `SourceInterfaceName` | `string` | NIC on which the datagram was received. Same forward-compatibility argument as `Nt`. |

**Capacity**: 10,000 entries. FIFO eviction. Owner: `SsdpLogViewModel` (see research §8).

**Spec citations**: FR-014, FR-015, FR-016.

---

## 7. DiscoverySession

A bounded period that starts when an active-discovery probe is issued and ends after MX + 1 s elapses. Tracks which previously-known devices have been heard during the window so rescans can prune non-responders.

| Field | Type | Notes |
|---|---|---|
| `StartedUtc` | `DateTimeOffset` | When the M-SEARCH burst was sent. |
| `Deadline` | `DateTimeOffset` | `StartedUtc + MX + grace`. Default: `StartedUtc + 4 s`. |
| `IsStartupSession` | `bool` | `true` for the session run at app launch (no pruning); `false` for rescans (pruning enabled). |
| `KnownAtStart` | `IReadOnlySet<string>` | Snapshot of device UUIDs in the registry at session start. Used for pruning. |
| `HeardThisSession` | `HashSet<string>` | UUIDs heard via M-SEARCH response or alive NOTIFY during the window. |
| `State` | `enum DiscoverySessionState { Running, Completed, Superseded }` | `Superseded` if a new rescan starts before this one finishes (spec Edge Cases). |

**State transitions**:

```text
Created → Running
Running, deadline elapses → Completed → for each uuid in KnownAtStart \ HeardThisSession, remove device (FR-023)
Running, new rescan started → Superseded (previous pruning skipped; new session takes over)
```

**Spec citations**: FR-004, FR-021, FR-022, FR-023, FR-024; spec Edge Cases ("Rescan triggered while previous rescan in progress").

---

## 8. InvocationRequest

A single attempt by the user to call an action on a service. Holds the user-entered input values until the SOAP call returns.

| Field | Type | Notes |
|---|---|---|
| `Service` | `Service` | The target service. |
| `Action` | `ActionDefinition` | The target action. |
| `Inputs` | `IReadOnlyDictionary<string, string>` | Argument name → user-entered text. Always contains a key per `Action.Inputs`; value may be empty string. |
| `SubmittedUtc` | `DateTimeOffset` | When the user pressed Invoke. |
| `Cancellation` | `CancellationToken` | Linked to the invocation popup's lifetime. |

**Spec citations**: FR-025, FR-026, FR-027.

---

## 9. InvocationResult

Outcome of an `InvocationRequest`. A discriminated union (sealed class hierarchy or `OneOf<>`-like record).

```text
InvocationResult
├── Success(IReadOnlyDictionary<string, string> outputs)          // outputs may be empty (FR-031)
├── UpnpFault(int httpStatusCode, int upnpErrorCode,              // FR-029
│             string upnpErrorDescription, string rawFaultXml)
└── TransportError(string message, Exception? underlying)         // FR-030
```

| Field | Type | Notes |
|---|---|---|
| `CompletedUtc` | `DateTimeOffset` | When the result was produced (success or failure). |

**Spec citations**: FR-028, FR-029, FR-030, FR-031.

---

## 10. SubscriptionState

A live binding between a subscription popup and a service's eventing endpoint.

| Field | Type | Notes |
|---|---|---|
| `Service` | `Service` | The subscribed-to service. |
| `Sid` | `string` | Subscription identifier returned by the device on SUBSCRIBE (UDA 1.0 §4.1.2). |
| `CallbackUrl` | `Uri` | Full URL the device is calling back into (this app's `HttpListener` route). |
| `GrantedTimeout` | `TimeSpan` | Value parsed from the response `TIMEOUT: Second-<n>` header. |
| `RenewalDueUtc` | `DateTimeOffset` | `now + GrantedTimeout − 30 s` (research §12). |
| `Status` | `enum SubscriptionStatus { Pending, Active, Lapsed, Failed, Closed }` | See state transitions below. |
| `FailureReason` | `string?` | Populated for `Failed`/`Lapsed`. |
| `Events` | `BoundedObservableCollection<EventNotification>` | Capped (e.g., 5,000) so a chatty service can't blow the popup's memory. **Ordering: newest first** — each arriving `EventNotification` is inserted at index 0 so the popup's scrolling list naturally shows the most-recent event at the top (FR-033). Eviction therefore removes the **tail** item (the oldest) when at capacity, not the head. |
| `CreatedUtc` | `DateTimeOffset` | For diagnostics. |

**State transitions**:

```text
Created          → Pending          // SUBSCRIBE in flight
Pending          → Active           // SUBSCRIBE 200 with SID + TIMEOUT
Pending          → Failed           // SUBSCRIBE 4xx/5xx or transport error → FR-035 (no UNSUBSCRIBE on close)
Active           → Active           // each successful renewal (research §12)
Active           → Lapsed           // renewal refused or fails permanently → FR-038 (no UNSUBSCRIBE on close)
Active           → Closed           // user closes popup → send UNSUBSCRIBE → FR-034
Active           → Closed           // device byebye → FR-037 (no UNSUBSCRIBE on close)
```

**Spec citations**: FR-032, FR-033, FR-034, FR-035, FR-036, FR-037, FR-038.

---

## 11. EventNotification

A single GENA NOTIFY received while a subscription is active.

| Field | Type | Notes |
|---|---|---|
| `ReceivedUtc` | `DateTimeOffset` | Timestamp of receipt. |
| `SequenceNumber` | `uint` | From the `SEQ` header (UDA 1.0 §4.3). 0 for the initial state burst. |
| `Properties` | `IReadOnlyDictionary<string, string>` | Parsed `<e:propertyset>`/`<e:property>` name→value pairs (UDA 1.0 §4.3). Values are the device's raw string form; v1 does not interpret them further. |
| `RawXml` | `string?` | Available for diagnostics if parsing partially fails (spec Edge Case: "Subscription receives an event larger than expected, or a malformed event"). |

**Spec citations**: FR-033; spec Edge Cases.

---

## 12. DiagnosticEntry

One row in the diagnostic log — both the rolling file (FR-040) and the in-memory ring (FR-041).

| Field | Type | Notes |
|---|---|---|
| `Timestamp` | `DateTimeOffset` | When the entry was recorded. |
| `Severity` | `enum DiagnosticSeverity { Trace, Information, Warning, Error }` | Maps to `Microsoft.Extensions.Logging.LogLevel`. |
| `Category` | `string` | Short stable identifier: `Ssdp.Parse`, `Ssdp.Send`, `Description.Fetch`, `Description.Parse`, `Scpd.Fetch`, `Scpd.Parse`, `Control.Soap`, `Control.Transport`, `Eventing.Subscribe`, `Eventing.Renew`, `Eventing.Unsubscribe`, `Eventing.Callback`, `Discovery.Rescan`, `App.Lifecycle`. |
| `Message` | `string` | Human-readable summary. |
| `Context` | `IReadOnlyDictionary<string, string>` | Structured fields: `device.uuid`, `service.id`, `action.name`, `url`, `http.status`, `error.code`, `error.text`, `interface.name`, etc. Populated only with values the call site has. |
| `Exception` | `string?` | `Exception.ToString()` if applicable. |

**Capacity**:

- In-memory ring: 5,000 entries; oldest evicted first (FR-041).
- Rolling file: ≤8 files × ≤2 MB each (FR-040; research §14).

**Spec citations**: FR-039, FR-040, FR-041, FR-042.

---

## 13. Relationships and ownership

```text
DeviceRegistry            (singleton)
   └── owns Device         (keyed by Uuid)
            └── owns Service       (keyed by ServiceId within device)
                     └── owns ActionDefinition  (keyed by Name within service)
                              └── owns ArgumentDefinition (ordered list)
                     └── owns StateVariableDefinition (keyed by Name within service)

SsdpLogViewModel          (singleton)
   └── owns BoundedObservableCollection<SsdpLogEntry>   (cap 10,000, FIFO)

DiscoveryService          (singleton)
   └── owns DiscoverySession    (zero or one at a time; superseded on new rescan)

InvocationPopupViewModel  (one per popup window)
   ├── builds InvocationRequest
   └── holds InvocationResult once produced

SubscriptionPopupViewModel (one per popup window)
   └── owns SubscriptionState
              └── owns BoundedObservableCollection<EventNotification>   (cap 5,000)

RingDiagnosticBuffer      (singleton)
   └── owns DiagnosticEntry ring     (cap 5,000, FIFO)

RollingFileDiagnosticSink (singleton)
   └── persists DiagnosticEntry as JSON-lines on disk
```

All collections that back observable UI are wrapped in `BoundedObservableCollection` so eviction goes through `INotifyCollectionChanged` without dropped notifications.

---

## 14. Notes on identity and equality

- `Device` identity: `Uuid`. Two devices with the same UUID are the same device even if the friendly name or LOCATION changes (UDA 1.0 §1.1.4). Only root devices are materialised; embedded children contribute services only.
- `Service` identity within a root device: `(ContainingDeviceUdn, ServiceId)` — this disambiguates services declared by different embedded children that happen to share a `ServiceId`. Across devices: `(OwningDeviceUuid, ContainingDeviceUdn, ServiceId)`. The bare `ServiceId` is still used verbatim in SOAP/SUBSCRIBE wire traffic (UDA 1.0 §3.1.1, §4.1.1); embedded provenance is purely for in-app identity and labelling (research §20).
- `ActionDefinition` identity within a service: `Name`.
- `ArgumentDefinition` identity within an action: `(Direction, Name)` (a single action can in principle reuse a name across directions, though SCPDs generally don't).
- `SubscriptionState` identity: `Sid` (server-issued). Pre-subscription state has no `Sid` and is identified by the owning popup view-model.
- `DiagnosticEntry`: no stable identity; entries are immutable records consumed by their sinks.
