# Feature Specification: UpnpSpy — UPnP Network Device Browser

**Feature Branch**: `001-upnp-spy-discovery`
**Created**: 2026-05-10
**Updated**: 2026-05-14
**Status**: Draft
**Input**: User description: "Two-pane desktop app. Left pane: tree of UPnP devices, populated by SSDP M-SEARCH at startup and continuous NOTIFY advertisements; unique by UUID; labelled by friendly name; byebye removes the entry; expand to show services (from device XML serviceList) and actions (from SCPDURL). Right-click device for 'Fetch XML' (opens in default browser). Right-click service for 'Fetch service XML' or 'Subscribe' (popup with evented updates; unsubscribe on close). Double-click action for an invocation popup (editable input args, output args or HTTP/UPnP error and fault text on failure). Right pane: scrolling list of all received SSDP messages (timestamp | ALIVE/BYEBYE | uuid). View menu has 'Rescan' which re-runs the startup discovery and prunes non-responders after the MX period."

## Clarifications

### Session 2026-05-12

- Q: What target platform(s) must the v1 application support? → A: Windows-only desktop (Windows 10/11)
- Q: How should the application handle UPnP eventing subscription expiry while a subscription popup is open? → A: Auto-renew the subscription before each device-granted timeout, for as long as the popup is open
- Q: Which local network interface(s) should the application use for SSDP discovery and the eventing callback? → A: All eligible IPv4 interfaces (every up, non-loopback IPv4 interface); discovered devices merged into a single tree
- Q: What is the bounded cap on the SSDP message log? → A: 10,000 entries (oldest discarded first when the cap is reached)
- Q: How should the application record internal diagnostic events (parse failures, network errors, subscription renewal failures, etc.) in addition to user-visible inline messages? → A: Both — a bounded rolling log file on disk **and** an in-app diagnostics viewer (View > Diagnostics) backed by an in-memory ring

### Session 2026-05-14

- Q: When should a device's description XML be fetched relative to SSDP discovery? → A: **Eagerly, immediately on discovery** (so the tree label shows the friendly name without waiting for the user to expand the node). Service-level SCPD fetches remain lazy on service expansion. (Revises the earlier "lazy device description on first expansion" assumption.)
- Q: How should the device tree communicate which nodes are expandable, given that WinUI's `TreeView` only renders the expand chevron once a node's children collection is non-empty? → A: **Two complementary affordances** — (a) every device and service node carries at least one child item (a transient "Loading…" placeholder) from the moment the node is created, so the chevron is always visible for nodes that can be expanded; (b) every tree row displays a small node-type glyph (Segoe Fluent Icons) so the user can tell devices, services and actions apart at a glance.
- Q: What should happen to a device whose description XML cannot be fetched — should it still appear in the tree with a fallback label, or should it be hidden? → A: **Hide-until-loaded** — a device appears in the left-pane tree only after its description has been successfully fetched. Devices that have never been fetched, are mid-fetch, or whose fetch failed do not appear in the tree. Failures are still recorded as Warning diagnostic entries so the user can identify which devices failed (and why) via `View → Diagnostics`. (Revises the earlier "device remains in tree with FR-010 fallback label on fetch failure" rule.)

### Session 2026-05-15

- Q: Eventing fails with `HttpListenerException: Access is denied.` on unpackaged developer builds because `HttpListener` binds to `http://+:<port>/upnpspy/` which requires a URL ACL grant under Windows HTTP.SYS. How should this be resolved without relying on URL ACL? → A: **Single-adapter operation with a `TcpListener`-based callback host.** (a) The application operates on exactly one network adapter at a time, defaulting to the first eligible adapter at startup and switchable at runtime via a `View → Network adapter` menu. (b) The eventing callback host is rebuilt on `System.Net.Sockets.TcpListener` bound to that adapter's specific IPv4 address, with a small hand-rolled HTTP/1.1 NOTIFY parser. `TcpListener` uses raw BSD sockets and bypasses HTTP.SYS entirely, so no URL ACL is needed for any user. (Revises the earlier "every eligible IPv4 interface" rule in FR-004 and the `HttpListener` choice in FR-033's implementation.)
- Q: A single physical chassis (e.g. a Sky ADSL router) often advertises multiple root devices under different UUIDs but the same `friendlyName`, so the user sees several identically-named tree rows and cannot tell them apart. What detail should the tree show to disambiguate? → A: **Two-tier disclosure.** (Tier 1) Every device row carries a muted secondary line beneath the friendly name: the tail of the `<deviceType>` URN plus the device's `LOCATION` IP and port (e.g., `InternetGatewayDevice · 192.168.0.1:49152`). (Tier 3) Right-clicking a device offers a `Properties…` option opening a separate read-only window with the full UPnP description fields plus SSDP-side metadata (manufacturer, model, serial, presentationURL, SERVER header, CACHE-CONTROL max-age, BOOTID, CONFIGID, first/last seen). Tier 2 (hover tooltip) is deferred. *(Note: the IGD example originally given here was inaccurate — `WANDevice`/`WANConnectionDevice` are **embedded children** of one `InternetGatewayDevice` root, not co-resident roots. The 2026-05-15 root-only registration rule below means an IGD chassis now correctly appears as one row. SC-019's acceptance still applies to genuine cases of two physical chassis sharing a friendly name.)*

- Q: Sky's ADSL router (an IGD chassis) was appearing as three identically-named tree rows — `Sky ADSL Router` with the same deviceType and IP — because each embedded child (`WANDevice`, `WANConnectionDevice`) was being registered as a separate top-level device. UDA 1.0 models them as embedded children of a single `InternetGatewayDevice` root, not as roots in their own right. How should discovery distinguish a root device from an embedded child at the SSDP layer? → A: **Filter on `upnp:rootdevice`.** (a) Both the startup and rescan M-SEARCH probes use `ST: upnp:rootdevice` (UDA 1.0 §1.3.3 guarantees exactly one response per root device) rather than `ssdp:all` (which yields a response per root + per embedded device + per service). (b) Incoming `NOTIFY ssdp:alive` and `NOTIFY ssdp:byebye` datagrams only create or remove registry entries when their `NT` header is exactly `upnp:rootdevice`; alive/byebye with any other `NT` (an embedded device's UDN, a service type URN, etc.) is still logged to the right-pane SSDP list per FR-014/FR-015 but does not affect the tree. (c) As a backstop for non-conformant devices, after the eager description fetch (FR-043) completes, if the fetched description's root `<UDN>` does not match the requesting UUID, the requesting UUID is treated as an embedded child, removed from the registry, and an `Information` `Description.Fetch` diagnostic is recorded with the UUID, URL, and declared root UDN.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See the UPnP devices on my network at startup (Priority: P1)

When the user launches the application, it actively probes the local network for UPnP devices and lists every device that responds, labelled by its human-readable friendly name. This gives the user immediate visibility of what is on the network the moment they open the tool.

**Why this priority**: This is the core promise of the tool. Without a populated device list at startup the application has no value, and every other capability depends on a device being visible in the tree.

**Independent Test**: Launch the application on a network that has at least one known UPnP device (e.g., a media renderer, NAS, or router). Within a few seconds the device's friendly name appears as an entry in the left-pane tree. Stopping there delivers a usable "what's on my network?" tool.

**Acceptance Scenarios**:

1. **Given** the application is not yet running and a UPnP device is reachable on the LAN, **When** the user launches the application, **Then** the device's friendly name appears as a top-level node in the left-pane tree within the discovery wait period plus the description-fetch budget (see SC-001) — without the user needing to expand the node.
2. **Given** several UPnP devices are reachable on the LAN, **When** the user launches the application, **Then** each unique device appears exactly once in the tree, regardless of how many discovery responses it sent.
3. **Given** the same physical device is announced under the same UUID through multiple advertisement messages, **When** the responses arrive, **Then** only one tree entry is created and subsequent duplicates are silently ignored.
4. **Given** there are no UPnP devices on the network, **When** the user launches the application, **Then** the tree is empty and the application remains responsive (no error dialog, no hang).

---

### User Story 2 - Watch the tree update as devices come and go (Priority: P2)

While the application is running, devices that announce themselves on the network (alive advertisements) appear in the tree, and devices that announce they are leaving (byebye advertisements) disappear from it — without the user doing anything.

**Why this priority**: Networks are dynamic; a static snapshot taken at startup gets stale within minutes. Continuous updates make the tool trustworthy as a live view rather than a one-shot scan.

**Independent Test**: With the application running and the tree populated, power on a previously-off UPnP device — its friendly name appears in the tree without user action. Power it off (graceful shutdown) — its entry disappears from the tree.

**Acceptance Scenarios**:

1. **Given** the application is running and a UPnP device with UUID X is not currently in the tree, **When** that device sends an alive advertisement, **Then** a new tree entry for it is added using its friendly name.
2. **Given** the application is running and a UPnP device with UUID X is in the tree, **When** that device sends a byebye advertisement, **Then** the corresponding tree entry is removed.
3. **Given** a device with UUID X is already in the tree, **When** another alive advertisement for the same UUID arrives, **Then** the tree is unchanged (no duplicate, no flicker).

---

### User Story 3 - Drill into a device to see its services and actions (Priority: P3)

When the user expands a device node in the tree, they see the list of services that device exposes. When they expand a service node, they see the list of actions that service offers. This lets the user explore the capabilities of any device they have discovered.

**Why this priority**: Discovery alone tells you a device exists; service and action enumeration tells you what it can do. This is the foundation that lets the user reach any of the deeper interactions (XML viewing, invocation, subscription).

**Independent Test**: Discover a known device (e.g., a Media Renderer), expand its node, and confirm its services (e.g., AVTransport, RenderingControl) appear as children. Expand one of those and confirm its actions (e.g., Play, Stop, SetVolume) appear as grandchildren.

**Acceptance Scenarios**:

1. **Given** a device is in the tree, **When** the user expands its node for the first time, **Then** each service listed in the device description's `serviceList` appears as a child node, identified clearly enough that the user can tell services apart.
2. **Given** a service node is visible under a device, **When** the user expands the service node, **Then** each action listed in the service description (the document at `SCPDURL`) appears as a grandchild node identified by its action name.
3. **Given** a device's description cannot be retrieved or parsed, **When** the user attempts to expand the device, **Then** the user is informed that service details are unavailable and the application remains responsive.
4. **Given** a service's description cannot be retrieved or parsed, **When** the user attempts to expand the service, **Then** the user is informed that action details are unavailable and the application remains responsive.

---

### User Story 4 - Watch SSDP traffic live in the right pane (Priority: P4)

The right pane shows a continuously updating, scrollable list of every SSDP advertisement the application receives, one row per message, with a timestamp, the message kind (ALIVE or BYEBYE), and the device's UUID. This lets the user observe the network's UPnP chatter as it happens, independent of what is in the device tree.

**Why this priority**: This makes the tool genuinely useful as a diagnostic. Even when the tree is correct, seeing the raw traffic helps the user understand *why* something appeared or disappeared, and confirms the application is hearing what they expect.

**Independent Test**: Start the application on a LAN with at least one chatty UPnP device. The right pane shows alive lines arriving over time. Power-cycle the device gracefully and observe a BYEBYE line for it followed by ALIVE lines after it comes back up.

**Acceptance Scenarios**:

1. **Given** the application is running, **When** an SSDP alive advertisement is received, **Then** a new row is inserted at the **top** of the right-pane list showing the timestamp it was received, the literal `ALIVE`, and the device's UUID.
2. **Given** the application is running, **When** an SSDP byebye advertisement is received, **Then** a new row is inserted at the **top** showing the timestamp, the literal `BYEBYE`, and the device's UUID.
3. **Given** the list has grown long enough to exceed the visible area, **When** the user scrolls down to read older entries, **Then** earlier entries remain readable and the list does not jump back to the top on every new arrival.
4. **Given** an advertisement arrives that the application cannot parse far enough to extract a UUID, **Then** the malformed message is ignored for logging purposes and the application remains responsive.

---

### User Story 5 - View a device's or service's raw XML in the browser (Priority: P5)

Right-clicking on a device node offers a menu item to fetch and view that device's description XML in the user's default web browser. Right-clicking on a service node offers an equivalent option for the service description (the SCPD document).

**Why this priority**: Power users want to inspect the raw description to confirm details, copy values, or diagnose unexpected behaviour. Useful, but secondary to the parsed tree and the live traffic view.

**Independent Test**: Right-click any device in the tree, choose the XML menu item, and confirm that the user's default browser opens to a URL serving that device's description. Right-click any service and do the same for the service description XML.

**Acceptance Scenarios**:

1. **Given** a device node is visible in the tree, **When** the user right-clicks the node and chooses the "Fetch XML" option, **Then** the user's default browser opens displaying the device's description XML.
2. **Given** a service node is visible in the tree, **When** the user right-clicks the node and chooses the "Fetch service XML" option, **Then** the user's default browser opens displaying that service's description XML (SCPD).
3. **Given** the user right-clicks a node that is not a device or service (e.g., an action), **Then** the menu offered does not include any XML-fetching option (or no menu is shown).

---

### User Story 6 - Force a rescan from the View menu (Priority: P6)

The user picks "Rescan" from the View menu. The application re-runs the same active probe it ran at startup and, after the discovery wait period elapses, removes any devices that did not respond to this latest probe. The result is a tree containing exactly the devices that are reachable right now.

**Why this priority**: Useful when the user joins a different network, just plugged something in, or wants a clean slate — but the alive/byebye listening (P2) keeps the tree mostly accurate without it, so this is a convenience.

**Independent Test**: With the tree populated, disconnect a device from the network without sending byebye (e.g., yank its power). Wait long enough that no further advertisements arrive. Choose "Rescan" from the View menu. After the discovery wait period elapses, the disconnected device's entry is gone.

**Acceptance Scenarios**:

1. **Given** the application is running with devices in the tree, **When** the user chooses "Rescan" from the View menu, **Then** the application sends a fresh discovery probe and devices that respond remain in the tree.
2. **Given** a rescan is in progress, **When** the discovery wait period (MX) elapses, **Then** any device that was in the tree before the rescan but did not respond to this rescan is removed.
3. **Given** a rescan is in progress, **When** a new (previously unseen) device responds within the wait period, **Then** that device is added to the tree.
4. **Given** a rescan is in progress, **When** a byebye advertisement arrives for a device, **Then** that device is removed from the tree (rescan does not suppress normal advertisement handling).

---

### User Story 7 - Invoke an action on a service (Priority: P7)

Double-clicking on an action node opens a popup window. The popup lists every input argument the action declares, lets the user enter a value for each, and offers a control to invoke the action. On success, the popup displays the output arguments returned by the device. On failure, the popup displays the HTTP status code, the UPnP error code, and any UPnP fault text returned.

**Why this priority**: Invocation turns the tool from a passive browser into an active control point — extremely useful for testing devices and learning what their actions do, but only meaningful once discovery, expansion, and action enumeration are working.

**Independent Test**: Discover a known device with a well-understood action (e.g., RenderingControl::GetVolume on a media renderer). Double-click the action, leave or fill the required arguments (e.g., InstanceID = 0, Channel = "Master"), invoke, and confirm the popup shows the returned value. Repeat with an invalid argument and confirm the popup shows the error code and fault text.

**Acceptance Scenarios**:

1. **Given** an action node is visible in the tree, **When** the user double-clicks it, **Then** a popup window opens listing each input argument of that action with an editable field for its value.
2. **Given** the invocation popup is open, **When** the user submits the invocation and the device returns a success response, **Then** the popup displays each output argument and its value.
3. **Given** the invocation popup is open, **When** the user submits the invocation and the device returns a SOAP fault, **Then** the popup displays the HTTP status code, the UPnP error code, and the UPnP fault description text.
4. **Given** the invocation popup is open, **When** the user submits the invocation and the network request fails entirely (e.g., the device is unreachable), **Then** the popup displays an error condition that includes the available diagnostic information without crashing the application.
5. **Given** an action that has no input arguments, **When** the user double-clicks the action, **Then** the popup still opens and can be submitted with no input.
6. **Given** an action that has no output arguments, **When** invocation succeeds, **Then** the popup indicates success even though no output values are shown.

---

### User Story 8 - Subscribe to a service's events (Priority: P8)

Right-clicking a service node offers a "Subscribe" option. Choosing it opens a popup window: the application issues a UPnP eventing subscription to that service and then displays each subsequent evented update in a scrolling list inside the popup. When the user closes the popup, the application issues an unsubscribe for that subscription.

**Why this priority**: Eventing is how UPnP devices push state changes to interested parties; being able to watch them live is high value for diagnosis and learning, but it is the deepest interaction in the tool and depends on every other layer.

**Independent Test**: Right-click a service known to emit events (e.g., AVTransport on a media renderer), choose Subscribe, then trigger a state change on the device (e.g., start playback). Confirm an event row appears in the popup. Close the popup and confirm (via the device or a network trace) that the application sent an unsubscribe.

**Acceptance Scenarios**:

1. **Given** a service node is visible in the tree, **When** the user right-clicks it and chooses "Subscribe", **Then** a popup window opens and the application initiates a subscription to that service.
2. **Given** an open subscription popup, **When** the device sends an event notification, **Then** a new entry representing that event appears **at the top** of the popup's scrolling event list, pushing previously-received events further down; the most-recent event is therefore always the first row visible.
3. **Given** an open subscription popup, **When** the user closes it, **Then** the application sends an unsubscribe request for that subscription.
4. **Given** the subscription cannot be established (device rejects it, network fails, etc.), **Then** the popup informs the user that subscription failed and remains responsive; closing it does not attempt an unsubscribe for a subscription that was never created.
5. **Given** a subscription popup is open, **When** the underlying device leaves the network (byebye) or its tree entry is otherwise removed, **Then** the popup informs the user that the subscription is no longer active; closing it does not produce errors.

---

### Edge Cases

- **Device with no friendly name in its description**: The label cannot be empty; the application falls back to a sensible identifier (e.g., the UUID) so the entry is still selectable.
- **Two distinct devices with the same friendly name**: Both appear as separate entries (UUID is the identity, not the label); the user may see two identically-labelled rows. This is acceptable.
- **Description URL is unreachable**: The eager fetch from FR-043 fails; per FR-047 the device does **not** appear in the tree. The failure is recorded as a Warning `DiagnosticEntry` (FR-039) tagged with the device's UUID and `LOCATION` URL, visible to the user via `View → Diagnostics` (FR-041). The device's presence was confirmed by SSDP but its capabilities cannot be rendered, so the cleaner UX is to hide it from the tree while making the failure inspectable in Diagnostics. Other devices are unaffected.
- **Description XML is malformed**: Same as above — the parse fails, the device does not appear in the tree, the parse failure is recorded as a Warning diagnostic with category `Description.Parse`, and `View → Diagnostics` lets the user inspect the cause. Other devices are unaffected.
- **Discovery burst yields many simultaneous fetches**: When dozens of devices respond to a single M-SEARCH, FR-043's bounded-parallelism cap means some descriptions resolve after others — devices materialise into the tree as each fetch completes, but per FR-054 they are inserted at their sorted alphabetical position rather than appended in fetch-completion order, so the final ordering does not depend on which fetches won the race. Devices whose fetch fails are silently absent from the tree; the user can correlate via Diagnostics if needed.
- **Device leaves the network mid-fetch**: A byebye or rescan-prune that fires while the eager description fetch is still in flight cancels the fetch and removes the registry entry; the device never appears in the tree, and (per FR-043 cancellation rules) no orphan diagnostic is emitted for the cancelled fetch.
- **Multicast traffic blocked by the local firewall or network**: The tree is empty and the right pane shows no traffic after startup. The application does not crash and the user can still attempt a rescan.
- **Device sends byebye while its services are being browsed, or while one of its actions is being invoked, or while one of its services has an open subscription**: The device entry is removed from the tree; any open invocation or subscription popup for that device is informed it is no longer connected and remains closeable without error.
- **Rescan triggered while a previous rescan is still in progress**: The previous rescan's pruning is superseded by the new one; only one rescan window is active at a time.
- **Many devices on the network (dozens)**: The tree remains responsive and all devices are listed; the SSDP log pane remains scrollable.
- **Device announces itself, then re-announces with a changed friendly name**: The label updates to reflect the latest announcement and the row migrates to its new alphabetical position (FR-054). The row's identity is preserved across the move, so any selection or expansion state survives.
- **SSDP log grows very long**: The application caps the log at 10,000 entries and discards the oldest entries (from the bottom of the list) first as new entries arrive at the top (see Assumptions).
- **Action invocation returns an unexpectedly large response, or the response is malformed**: The popup shows what it can parse and indicates the rest could not be displayed; the application does not crash.
- **Subscription receives an event larger than expected, or a malformed event**: The popup shows what it can parse and logs the issue without disrupting the rest of the application or other subscriptions.
- **Multiple subscription popups open simultaneously across different services**: Each popup tracks its own subscription independently and unsubscribes on its own close; closing one does not affect the others.
- **Multiple local network interfaces**: Discovery covers every up, non-loopback IPv4 interface on the host (Ethernet, Wi-Fi, virtual adapters from Hyper-V/VPN/Docker/WSL, etc.); a device that is reachable on any of them appears in the tree. (See Assumptions.)
- **Device or service node with the FR-044 "Loading…" placeholder visible when the user expands it**: The placeholder appears as the single child while the underlying eager description fetch (for a device) or lazy SCPD fetch (for a service) is in flight; it is replaced atomically by the real children once the fetch resolves, or by the FR-013 inline error placeholder if the fetch fails. No flicker, no transient empty state, no spurious chevron disappearance.

## Requirements *(mandatory)*

### Functional Requirements

All protocol-mandating requirements below cite the relevant section of `docs/specs/UPnP-arch-DeviceArchitecture-v1.0-20080424.pdf` ("UDA 1.0") per Constitution Principle II. Behavioural and UX requirements (layout, labelling, menu items, popup behaviour) carry no UDA citation because UDA 1.0 does not constrain UX.

#### Layout

- **FR-001**: The application MUST present two side-by-side panes within a single window: a device tree on the left and an SSDP message log on the right.
- **FR-002**: The left pane MUST display discovered devices as the top level of a tree, with their services as children and each service's actions as grandchildren.
- **FR-003**: The right pane MUST present its content as a scrolling list, with newer entries inserted at the **top** and earlier entries reachable by scrolling **down** (see FR-055).

#### Discovery

- **FR-004**: On application startup, the system MUST issue an SSDP active-discovery (M-SEARCH) request with **`ST: upnp:rootdevice`** (UDA 1.0 §1.2.1, §1.3.3). UDA 1.0 §1.3.3 guarantees one response per root device for this search target, which is the granularity the tree models — using the broader `ssdp:all` would additionally yield one response per embedded child and per service, every one of which would otherwise create a duplicate registry entry for the same physical chassis (canonically: an IGD router yields a response for `InternetGatewayDevice` + `WANDevice` + `WANConnectionDevice` + each service). The probe MUST be sent and the multicast group joined on **the user-selected adapter** (FR-048) — exactly one up, non-loopback, multicast-capable IPv4 interface. The default selection at startup MUST be the first eligible adapter enumerated by the host. Switching adapter at runtime (FR-048) MUST tear down the existing SSDP socket and rebuild it on the newly-selected adapter.
- **FR-005**: For every distinct device that responds to a discovery request, the system MUST add a corresponding tree entry.
- **FR-006**: For the entire time the application is running, the system MUST listen for SSDP advertisement messages (UDA 1.0 §1.1, §1.2) and add a tree entry for any newly-announced device.
- **FR-007**: The system MUST identify devices uniquely by the UUID carried in their advertisements' `USN` header (UDA 1.0 §1.1.4, §1.3); further advertisements for an already-known UUID MUST NOT create additional tree entries.
- **FR-008**: When the system receives an SSDP advertisement announcing that a device is no longer available (`NTS: ssdp:byebye`, UDA 1.0 §1.1.3), it MUST remove that device's entry from the tree.

- **FR-053**: The registry (and therefore the left-pane tree) MUST contain only **root** UPnP devices. Embedded children (devices declared inside a parent's `<deviceList>`) and individual services MUST NOT appear in the registry as separate entries; their services flatten into the root device's `<serviceList>` per FR-011 / spec Assumptions. The system enforces this at three points: **(a)** the M-SEARCH `ST` is `upnp:rootdevice` (FR-004, FR-022) so unicast responses are only emitted by roots; **(b)** an incoming `NOTIFY ssdp:alive` or `NOTIFY ssdp:byebye` only adds to or removes from the registry when its `NT` header is exactly `upnp:rootdevice` — alive/byebye datagrams with any other `NT` (an embedded device's UDN such as `uuid:<udn>`, an embedded device's `<deviceType>` URN, or a service type URN) are still appended to the right-pane SSDP log per FR-014/FR-015 but do not create or remove a tree entry; **(c)** the eager description fetch (FR-043) acts as a backstop for non-conformant devices — see FR-043's mismatch-handling clause.

- **FR-054**: The left-pane tree MUST present device rows in **case-insensitive alphabetical order of friendly name** (the FR-009 label, with the FR-010 `uuid:<uuid>` fallback used for devices whose description lacks `<friendlyName>`). Ordering MUST be stable: when two devices share a label, their relative position is determined by an ordinal comparison of their UUIDs, so the user does not see equal-labelled rows swap positions between sessions or rescans. The sort applies at every insertion point — initial seeding from the registry snapshot, the `DeviceAdded` path for devices that enter the registry already in `Loaded` state, and the `DeviceUpdated` promotion path for devices whose eager description fetch lands after they were first announced. When a device re-announces with a changed friendly name (and the new label maps to a different sorted position), the row MUST migrate to its new position while preserving the underlying node identity, so any selection or expansion state on that row survives the reorder.

#### Labels

- **FR-009**: Each device entry in the tree MUST be labelled with the device's friendly name as declared in its description. A device entry MUST NOT appear in the tree until its description has been successfully fetched (FR-047), so the user never sees a transient placeholder label for a still-resolving device.
- **FR-010**: When a device's description has been fetched but the parsed XML does not contain a `<friendlyName>` element, the system MUST display a fallback label that still uniquely identifies the entry to the user (e.g., `"uuid:<uuid>"`). Devices whose description fetch failed entirely do NOT use this fallback — they are hidden from the tree per FR-047.

#### Service & action enumeration

- **FR-011**: When the user expands a device node, the system MUST display every service listed in the device description's `<serviceList>` (UDA 1.0 §2.1) — together with the services declared by any embedded children of that root (recursively) — as child nodes. The description itself is fetched eagerly on discovery (FR-043); on expansion the system MUST therefore not perform a second HTTP fetch. If the description fetch is still in flight at expansion time, the node MUST show a transient "loading" indication and resolve to the child nodes as soon as the fetch completes; if the description fetch has failed, FR-013's inline error placeholder applies.
- **FR-012**: When the user expands a service node, the system MUST retrieve the service's description (the document referenced by `<SCPDURL>`, UDA 1.0 §2.2, §2.4) and display every action in its `<actionList>` (UDA 1.0 §2.2) as child nodes.
- **FR-013**: When the system cannot retrieve or parse a description needed to populate a node's children, it MUST inform the user inline (in or near the affected node) without crashing or affecting other nodes.

#### SSDP message log (right pane)

- **FR-014**: For every SSDP alive advertisement the system receives (`NTS: ssdp:alive`, UDA 1.0 §1.1.2), it MUST insert a row at the top of the right-pane list showing the timestamp at which the message was received, the literal `ALIVE`, and the device's UUID.
- **FR-015**: For every SSDP byebye advertisement the system receives (`NTS: ssdp:byebye`, UDA 1.0 §1.1.3), it MUST insert a row at the top of the right-pane list showing the timestamp at which the message was received, the literal `BYEBYE`, and the device's UUID.
- **FR-016**: The system MUST cap the SSDP log at **10,000 entries**; once the cap is reached, the oldest entry MUST be discarded each time a new entry arrives (FIFO eviction). Because new rows enter at the top (FR-055), the oldest entry sits at the bottom of the list and is the one discarded. This bound prevents unbounded memory growth during long sessions while preserving many hours of chatter on a typical LAN.
- **FR-055**: The right-pane SSDP log MUST be ordered **newest-first**: the most recently received advertisement occupies the top row and the oldest retained advertisement occupies the bottom row. The ordering MUST hold both during steady-state arrivals and across the FR-016 eviction boundary (eviction removes the bottom row, not the top). The view MUST auto-follow newly-arriving rows only while the user is parked at (or near) the top of the list; once the user has scrolled away from the top to read history, the list MUST NOT yank back to the top on every new arrival (acceptance #3).

#### XML viewing

- **FR-017**: Right-clicking a device node MUST present a context menu with an option to fetch that device's description XML.
- **FR-018**: Right-clicking a service node MUST present a context menu with a "Fetch service XML" option and a "Subscribe" option.
- **FR-019**: Choosing the "Fetch XML" option on a device node MUST open the device's description XML resource in the user's default web browser.
- **FR-020**: Choosing the "Fetch service XML" option on a service node MUST open the service description XML resource (SCPD) in the user's default web browser.

#### Rescan

- **FR-021**: The application MUST provide a "Rescan" command under a "View" menu.
- **FR-022**: Choosing "Rescan" MUST issue the same active-discovery (M-SEARCH, UDA 1.0 §1.2.1) request that startup uses, including the same `ST: upnp:rootdevice` search target (FR-004) so the rescan and startup probes have identical semantics.
- **FR-023**: After the discovery wait period (MX) elapses for a rescan, the system MUST remove any device in the tree that did not respond to this rescan.
- **FR-024**: A rescan in progress MUST NOT suspend handling of unsolicited alive or byebye advertisements.

#### Action invocation

- **FR-025**: Double-clicking an action node MUST open an invocation popup window for that action.
- **FR-026**: The invocation popup MUST list every input argument declared by the action and provide an editable input for each, allowing the user to set any input value before invoking.
- **FR-027**: The invocation popup MUST offer a control that sends the invocation to the device as a SOAP action request to the service's `<controlURL>` (UDA 1.0 §3.1.1) using the values the user has entered.
- **FR-028**: When the device returns a success response, the popup MUST display every output argument returned by the device along with its value.
- **FR-029**: When the device returns a SOAP/UPnP fault (UDA 1.0 §3.1.3, `<UPnPError><errorCode/><errorDescription/></UPnPError>`), the popup MUST display the HTTP status code, the UPnP error code, and the UPnP fault description text returned.
- **FR-030**: When the invocation request fails before a response is parsed (e.g., the device is unreachable), the popup MUST display an error condition with the available diagnostic information without crashing the application.
- **FR-031**: The invocation popup MUST handle actions that declare no input arguments (invocable with empty input) and actions that declare no output arguments (show success without output values).

#### Service subscription

- **FR-032**: Choosing the "Subscribe" option on a service's right-click menu MUST open a subscription popup window and initiate a UPnP eventing subscription (`SUBSCRIBE`, UDA 1.0 §4.1.1) against the service's `<eventSubURL>`. The `CALLBACK` URL announced in the SUBSCRIBE MUST point at the currently-selected adapter's IPv4 address (FR-048) on the local callback host's port (FR-049).
- **FR-033**: While the subscription popup is open, every event notification received from the subscribed service (`NOTIFY` / `<e:propertyset>`, UDA 1.0 §4.3) MUST be inserted at the **top** (index 0) of the popup's scrolling event list, so the newest event is always the first row visible and older events scroll off the bottom. Above the event list, the popup MUST display a fixed "Latest property values" summary that, for each evented property name seen so far during the subscription's lifetime, shows the property's most-recent value (later events for the same name overwrite the row in place). The summary remains anchored at the top of the popup independent of the event list's scroll position. When the popup's event-buffer cap is reached, the oldest event (now at the **tail** of the list) MUST be discarded first.
- **FR-034**: When the user closes the subscription popup, the application MUST send an `UNSUBSCRIBE` request (UDA 1.0 §4.1.4) for that subscription.
- **FR-035**: If the subscription cannot be established, the popup MUST inform the user and MUST NOT attempt to send an unsubscribe for a subscription that was never created.
- **FR-036**: The application MUST allow multiple subscription popups (across different services) to be open simultaneously, each managing its own subscription lifecycle independently.
- **FR-038**: For as long as a subscription popup remains open, the application MUST renew the subscription with the device (`SUBSCRIBE` with `SID` only, UDA 1.0 §4.1.3) before each device-granted timeout (`TIMEOUT` header on the SUBSCRIBE response, UDA 1.0 §4.1.2) expires, so that event delivery is uninterrupted. If a renewal is refused or fails, the popup MUST inform the user that the subscription has lapsed and MUST stop attempting to renew; closing the popup in that state MUST NOT attempt to send an unsubscribe for an expired subscription.

#### Robustness across interactions

- **FR-037**: When a device leaves the network (byebye) or is otherwise removed from the tree, any open invocation or subscription popup for that device or its services MUST inform the user that the device is no longer reachable and MUST remain closeable without producing errors.

#### Diagnostic logging

- **FR-039**: The application MUST record internal diagnostic events — including but not limited to SSDP parse failures, device-description fetch or parse failures, SCPD fetch or parse failures, action-invocation transport errors, subscription establishment failures, subscription renewal failures, and unsubscribe failures — in addition to whatever user-visible inline message is shown for them. Each diagnostic entry MUST carry a timestamp, a severity level, and enough context (device UUID, service id, action name, URL, status code, or error text as applicable) to identify what went wrong.
- **FR-040**: The application MUST write diagnostic entries to a **rolling log file** in a standard per-user location on disk (e.g., under `%LOCALAPPDATA%`). The log file MUST be bounded — implemented via size-based rollover with a small fixed number of rotated files — so it cannot grow without limit across long sessions or many runs.
- **FR-041**: The application MUST keep an **in-memory diagnostic buffer** of bounded size (ring buffer) and expose it to the user via a `View > Diagnostics` menu item that opens a scrollable viewer window showing the buffered entries. The viewer MUST remain responsive while new entries arrive and MUST update live as new entries are recorded.

  Each row in the viewer MUST surface two device-affiliation columns alongside the timestamp/severity/category/message already shown:
    - **Identity** — resolved from the entry's `device.uuid` context value, displayed as the device's current `friendlyName` if the device is in the registry and has one, otherwise as `"uuid:<uuid>"`; entries with no `device.uuid` context MUST render a visually muted placeholder (e.g., `—`) so the user can distinguish "no associated device" from "device with empty name".
    - **Endpoint** — resolved from the entry's `url` context value (parsed as a URI, displayed as `host` or `host:port` depending on whether the port is the URI's default), falling back to `remote.endpoint` for diagnostics that carry only a network endpoint (e.g., `Ssdp.Parse` failures), and to the same muted placeholder when neither is present.

  Resolution is **best-effort and snapshot-at-arrival**: identity is resolved from the registry once when the entry enters the viewer's collection. Devices that have since left the registry (byebye / rescan-prune) will fall back to `"uuid:<uuid>"`. This preserves the diagnostic as a stable historical record while still giving the user a recognisable name for devices still in the tree.
- **FR-042**: Diagnostic logging MUST NOT block the UI thread, MUST NOT prevent the application from starting if the log file cannot be opened (in that case the in-memory buffer and viewer continue to function and a single user-visible warning is shown), and MUST NOT include sensitive data beyond what is already implicit in the UPnP protocol exchange.

#### Secondary window ownership

- **FR-046**: Every secondary window the application opens (the action **invocation** popup, the service **subscription** popup, and the **Diagnostics** viewer) MUST be visually *owned* by the main window: it MUST appear above the main window the moment it is shown, MUST remain z-ordered above the main window as long as both are visible (routine focus shifts back to the main window MUST NOT push the popup behind it), MUST minimise and restore together with the main window, and MUST close automatically when the main window closes. Each popup is otherwise independently activatable (the user can still interact with the main window underneath); ownership is a z-order and lifetime contract, not modality.

#### Tree affordance (expandability cues)

- **FR-044**: For every tree node whose children are populated lazily or asynchronously (currently: device nodes and service nodes), the application MUST ensure the node's children collection is non-empty from the moment the node is added to the tree, so that the UI framework renders the expand chevron without waiting for the user's first click. The placeholder child MUST be visually distinguishable from real children (e.g., the literal text "Loading…") and MUST be replaced (or removed) atomically when the real children become available; on a fetch failure the placeholder MUST be replaced by the inline error placeholder from FR-013. Action nodes — which have no children by design — MUST NOT carry a placeholder and MUST NOT show an expand chevron.
- **FR-045**: Each tree row MUST display a small glyph in front of the node label identifying the node's kind (device, service, or action). The glyphs MUST be drawn from a font already shipped by Windows (no external icon assets) and MUST be distinct enough that a user scanning the tree can tell a device, a service, and an action apart without reading the label.

#### Device tree visibility

- **FR-047**: A device MUST appear in the left-pane tree if and only if its description has been successfully fetched and parsed (i.e., `DescriptionFetchState == Loaded`). Devices that have just been discovered but whose description fetch is still pending, in flight, or has failed MUST NOT appear in the tree. The device entry MUST remain in the underlying registry while its fetch is in flight (so the dispatcher and the SSDP byebye handler can address it by UUID) and MAY remain in the registry after a failed fetch — registry membership is not the same as tree visibility. Diagnostic entries for fetch failures MUST still be recorded under FR-039 so the user can identify, via `View → Diagnostics` (FR-041), every device whose description could not be retrieved and the underlying cause.

#### Device row disambiguation and Properties window

- **FR-051**: Every device row in the left-pane tree MUST display, beneath the friendly name, a muted secondary line containing **(a)** the tail of the device's `<deviceType>` URN (the segment after `:device:`, e.g., `InternetGatewayDevice`) and **(b)** the IPv4 host and port extracted from the device's `LOCATION` URL, separated by a middle-dot. The detail line MUST be drawn from the fields populated by the eager description fetch (FR-043); devices that have only just been discovered MUST NOT appear in the tree until those fields are populated (FR-047), so the detail line is never empty for a visible device. The detail line MUST be styled with a secondary foreground brush so the friendly name remains the visual focus.
- **FR-052**: Right-clicking a device node MUST present a `Properties…` option in its context menu (alongside the existing `Fetch XML` from FR-017). Choosing it MUST open a read-only Properties window owned by the main window (FR-046) that displays every field the application has captured for that device, organised into sections:
  - **Identity**: `friendlyName`, full `deviceType` URN, `UDN`/UUID, `presentationURL` (rendered as a clickable link that opens the device's web UI in the default browser when present).
  - **Manufacturer**: `manufacturer`, `manufacturerURL` (link), `modelName`, `modelNumber`, `modelDescription`, `modelURL` (link), `serialNumber`, `UPC`.
  - **Network**: `LOCATION` URL (link), the IP and port extracted from it, the SSDP `SERVER` header, `CACHE-CONTROL` max-age in seconds.
  - **Discovery history**: `FirstSeenUtc`, `LastSeenUtc`, total alive count, `BOOTID.UPNP.ORG` and `CONFIGID.UPNP.ORG` (UDA 1.1 §1.2 — present only if the device advertised them).
  - **Embedded devices**: recursive list of `<deviceList>` children, showing each child's `deviceType` and `friendlyName`. The list MAY be empty (most devices have none).
  
  The window MUST remain closeable without producing errors if the device is removed from the tree while open (FR-037). Fields the device did not declare MUST be shown as a visually muted placeholder (e.g., "—") so the user can distinguish "absent" from "empty".

#### Network adapter selection and ACL-free eventing

- **FR-048**: The application MUST operate on exactly one IPv4 network adapter at a time. The set of available adapters MUST be the eligible-IPv4 interfaces enumerated at startup (operational, non-loopback, multicast-capable, IPv4). At startup the system MUST default to the first eligible adapter. The application MUST expose a `View → Network adapter` menu listing every available adapter as a radio item; selecting a different adapter MUST become the new "current adapter" and trigger the rebind sequence in FR-050. Hosts with zero eligible adapters MUST keep running with an empty tree (a Warning diagnostic MUST be recorded).
- **FR-049**: The eventing callback host (FR-033) MUST bind via `System.Net.Sockets.TcpListener` to the specific IPv4 address of the currently-selected adapter (FR-048) and parse incoming `NOTIFY` requests in-process. The implementation MUST NOT rely on `System.Net.HttpListener`, MUST NOT register a URL ACL via `netsh http`, and MUST NOT require Administrator privileges or any one-shot installer step. The hand-parsed HTTP/1.1 surface is restricted to: request line, header block, `Content-Length`-bounded request body. Requests with malformed framing, oversized headers, or oversized bodies MUST be rejected with `400 Bad Request` and a Warning `DiagnosticEntry`. Per-request read MUST be bounded by a timeout to defend against half-open / slowloris connections.
- **FR-050**: When the user selects a different adapter (FR-048), the application MUST atomically (a) stop the SSDP transport and the callback host, (b) clear the device registry (devices observed on the previous adapter are no longer reachable in the same way and MUST be re-discovered), (c) cancel every in-flight description / SCPD fetch, (d) tell every open invocation or subscription popup that its device is no longer reachable (per FR-037), (e) rebind the SSDP transport and the callback host on the new adapter, (f) re-run the startup discovery sweep (FR-004) so the tree refills on the new adapter. The adapter switch MUST complete within the normal discovery wait period (SC-001 budget) and MUST NOT block the UI thread.

#### Eager device-description fetch

- **FR-043**: Whenever a new device is added to the registry as a result of SSDP discovery (M-SEARCH response or NOTIFY alive — FR-005, FR-006), the system MUST asynchronously fetch that device's description (UDA 1.0 §2.1, §2.4) from its `LOCATION` URL without waiting for any user interaction. On success the system MUST populate the device's friendly name and parsed service list and add the device to the tree (FR-047), so that subsequent expansion (FR-011) is purely a UI render. On failure the device MUST NOT appear in the tree (FR-047); the failure MUST be recorded as a Warning `DiagnosticEntry` (FR-039) so the user can identify what failed and why via `View → Diagnostics` (FR-041). The eager fetches MUST run with bounded parallelism so that a discovery burst across many devices does not produce an unbounded fan-out of concurrent HTTP requests. Subsequent advertisements for an already-known UUID MUST NOT trigger a re-fetch; the description is cached for the lifetime of the registry entry (a fresh registry entry, created after a byebye or rescan-prune, MUST fetch again). A pending or in-flight eager fetch MUST be cancelled if the device leaves the registry (byebye or rescan-prune) before the fetch completes.

  **Mismatched-root backstop (FR-053(c)):** if the fetched description's root `<UDN>` (UDA 1.0 §2.1) does not match the requesting registry UUID, the requesting UUID is an embedded child or a non-conformant entity that slipped past the SSDP-layer NT filter. In that case the system MUST NOT write the description's friendly name or service list onto the requesting device, MUST remove the requesting UUID from the registry, and MUST record an `Information` `Description.Fetch` diagnostic carrying `device.uuid`, `url`, and `declared.root.uuid` so the reconciliation is auditable via `View → Diagnostics` (FR-041).

### Key Entities

- **Device**: A discovered UPnP device. Identity is its UUID. Display attributes include its friendly name and a fallback label. References its description location and the set of services it advertises.
- **Service**: A capability exposed by a device. Identity is its service identifier within the owning device. Display attribute is something the user can use to tell services apart (e.g., service type). References the URL of its description document (SCPD) and the set of actions it offers.
- **Action**: A named operation belonging to a service. Display attribute is the action's name. Has an ordered list of input arguments and an ordered list of output arguments.
- **Argument**: A named, typed parameter belonging to an action — either input or output. The user provides values for input arguments; output arguments carry returned values.
- **SSDP Log Entry**: A single row in the right pane: timestamp of receipt, kind (`ALIVE` or `BYEBYE`), and the device UUID extracted from the advertisement.
- **Discovery Session**: A bounded period that starts when an active-discovery probe is issued (startup or rescan) and ends after the discovery wait period elapses. Tracks which previously-known devices have been confirmed during the session so non-responders can be pruned at the end.
- **Invocation**: A single attempt by the user to call an action on a service. Carries the entered input values; resolves to either a set of returned output values or a fault (HTTP status, UPnP error code, fault text).
- **Subscription**: A live binding between a popup window and a service's eventing endpoint. Has a lifetime bounded by the popup window; receives a stream of event notifications until the popup closes or the device disappears. The application renews the subscription with the device before each device-granted timeout to keep events flowing for as long as the popup is open.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After startup on a typical home or office LAN, every UPnP device that is online, responsive, AND whose description XML can be fetched and parsed appears in the tree within the discovery wait period plus the eager-fetch time (target: ~7 seconds total on a LAN with up to 20 devices — 5 s discovery + ≤2 s description fetch for typical XML sizes). No transient placeholder labels are shown: a device appears in the tree only with its real friendly-name label (or the FR-010 fallback when its description has no `<friendlyName>` element). Devices whose description fetch fails do not appear in the tree per FR-047; their failure is recorded in the diagnostic ring and visible via `View → Diagnostics` (FR-041, SC-017).
- **SC-002**: The tree contains exactly one entry per unique device UUID at all times (zero duplicate entries observed across a 30-minute session with continuous advertisements).
- **SC-003**: When a device that announces alive/byebye correctly is unplugged or powered off, its entry disappears from the tree within the time the device's byebye is delivered (typically under 2 seconds on a quiet LAN).
- **SC-004**: When the user expands a device or service node, the children appear within 2 seconds for descriptions of typical size on a LAN.
- **SC-005**: When the user chooses to view XML for a device or service, the default browser opens to that XML within 2 seconds.
- **SC-006**: After a rescan completes, every device still on the network is in the tree and no device that has left the network is in the tree (verified by reconciling against ground truth).
- **SC-007**: A user familiar with UPnP can locate a known device, find one of its services, and view that service's XML in three or fewer interactions (right-click counts as one).
- **SC-008**: A failure to retrieve any single device or service description does not prevent any other device or service in the tree from being browsed.
- **SC-009**: Every SSDP alive or byebye advertisement received during normal operation results in a row appearing in the right-pane log within 1 second of receipt.
- **SC-010**: From the moment the user double-clicks an action node, the invocation popup is interactive (input fields editable) within 1 second.
- **SC-011**: For an action invocation that the device answers within typical LAN latency (under 1 second), the popup shows the result (output values or fault) within 2 seconds of the user submitting the invocation.
- **SC-012**: Closing a subscription popup results in an unsubscribe being sent to the device before the popup is fully dismissed from the user's screen (or in the application's exit path if the user is closing the whole application).
- **SC-013**: A session of at least 1 hour of continuous operation on a typical LAN does not exhaust memory: the SSDP log remains bounded, the in-memory diagnostic buffer remains bounded, the on-disk diagnostic log rolls over instead of growing unbounded, and other resource use stays within reasonable limits.
- **SC-014**: After any user-visible failure message (e.g., "unable to load services", "subscription failed"), the user can open `View > Diagnostics` and find a corresponding entry within 1 second of the failure being shown, with enough context to identify the affected device/service and the underlying cause. The row's **Identity** column MUST show the device's friendly name (or `uuid:<uuid>` if unresolved) and the **Endpoint** column MUST show its IP and port, so the user can correlate the failure to a specific device on the LAN without having to click into the row to inspect the raw `Context` dictionary (FR-041).
- **SC-015**: At every point in the application's lifetime, every device or service node in the tree visibly indicates that it can be expanded **before** the user has interacted with it (chevron present from creation), and every row visibly indicates the node's kind via a leading glyph; a user shown the tree without prior instruction can identify which rows are expandable and which are leaves at a glance.
- **SC-016**: From the moment a secondary window is opened (invocation popup, subscription popup, or Diagnostics viewer), it is visible to the user; subsequent interaction with the main window (e.g., clicking another row in the tree) does not place the popup behind the main window. Verified by opening any popup, clicking on the main window's tree, and confirming the popup is still on top.
- **SC-017**: For every device on the LAN whose description fetch fails (HTTP error, transport error, or parse error), the user can open `View → Diagnostics` and find a corresponding Warning entry tagged with the device's UUID, its `LOCATION` URL, and the failure reason. Devices that fail to fetch MUST NOT appear in the left-pane tree (FR-047) but MUST be discoverable via the Diagnostics viewer within the latency budget of SC-014.
- **SC-018**: A clean unpackaged developer build (`dotnet run --project src\UpnpSpy.App`) starts on a non-Administrator account, with **no prior `netsh http add urlacl` grant of any kind**, and the eventing callback host comes up successfully. Verified by opening any subscription popup against a service known to emit events on a real device and observing event rows appear in the popup. The Diagnostics viewer MUST NOT contain any `Eventing.Callback` `bind failure` entries for the run.
- **SC-019**: On a LAN that exposes multiple distinct **root** UPnP devices sharing the same friendly name (e.g., two factory-default media renderers both labelled `Living Room`, or two Sonos units that have not been individually renamed), a user can distinguish every tree row from any other using the Tier 1 detail line alone (FR-051), without right-clicking or opening any popup. Verified by inspecting the tree on such a LAN and confirming every row's `(deviceType, IP:port)` pair is unique. *(Note: the previously-given IGD example is not applicable post-FR-053 — `WANDevice` and `WANConnectionDevice` are embedded children of a single `InternetGatewayDevice` root, so an IGD chassis correctly surfaces as exactly one tree row regardless of FR-051.)*

## Assumptions

- **Target platform**: The application targets Windows desktop (Windows 10 and Windows 11) for v1. UI framework, multicast/UDP and HTTP-callback APIs, default-browser invocation, and packaging are all chosen to suit this platform. Linux and macOS are out of scope for v1.
- The "discovery wait period" used at startup and on rescan corresponds to the MX value the application includes in its M-SEARCH (a standard 2–5 second window). The exact value is an implementation detail.
- Service and action enumeration is **mixed eager/lazy**: a device's description (which carries the friendly name and the device's service list) is fetched **eagerly** as soon as the device is discovered via SSDP (FR-043), so the tree label can resolve from the FR-010 fallback to the friendly name without the user expanding the node. A service's description (SCPD, which carries the action list) is still fetched **lazily** the first time the user expands the service. The combination means device labels and service lists are paid for at discovery time (one HTTP fetch per new device, capped in parallelism), while per-service action lists are paid for only when the user is interested in that specific service. This avoids hammering every device-and-service on the network at startup while still letting the user see human-readable device names without any interaction.
- The right-pane SSDP log records SSDP advertisement messages (alive and byebye NOTIFY). Direct unicast responses to M-SEARCH and unrelated multicast traffic are not displayed in this log (devices discovered via M-SEARCH still appear in the tree; they just do not produce an `ALIVE`/`BYEBYE` row unless they also advertise).
- The SSDP log is capped at **10,000 entries**; when the cap is reached, the oldest entry (at the bottom, per FR-055) is discarded as each new entry arrives at the top (FIFO eviction).
- Embedded devices (declared via nested `<deviceList>` inside a device's description) are not shown as separate tree entries in v1. The root device from each advertisement appears in the tree; the services it exposes include the union of its own `<serviceList>` and every `<serviceList>` declared in its embedded children, walked recursively. A service originating from an embedded child is labelled in a way that identifies the embedded device it came from, so two services of the same type declared in different embedded children can be told apart in the tree. The "root only" rule is enforced at the SSDP layer (FR-053): M-SEARCH uses `ST: upnp:rootdevice` and `NOTIFY` advertisements only register/remove a tree entry when their `NT` is exactly `upnp:rootdevice`; a description-fetch backstop catches any non-conformant device that slips through (FR-043). Without these gates an IGD-style chassis (canonically Sky's ADSL router) would advertise one root + multiple embedded children under separate UUIDs, all sharing the same `LOCATION`, and would surface as several identical-looking rows.
- The application targets the IPv4 SSDP multicast group (`239.255.255.250:1900`) on a **single user-selected eligible adapter** (FR-048): it binds one UDP socket on that adapter's IP, joins the multicast group there, sends M-SEARCH probes from there, and binds the eventing callback host on that adapter's specific IP via `TcpListener` (FR-049). The default at startup is the first eligible adapter; a `View → Network adapter` menu lets the user switch at runtime, which rebinds everything atomically per FR-050. Multi-NIC merging is therefore not part of v1 — a device reachable on multiple adapters appears only when the user has the corresponding adapter selected. IPv6 is out of scope for v1.
- The user has permission on the host to send and receive UDP multicast on the chosen interface, and to receive TCP callbacks on a local port. Firewall configuration is the user's responsibility; **no URL ACL grant is required** because the callback host uses `TcpListener` rather than `HttpListener` (FR-049).
- A device's friendly name is treated as immutable for the lifetime of a tree entry except where a later advertisement for the same UUID provides a different friendly name; in that case the label is updated to the latest value.
- The default web browser is determined by the operating system; the application does not embed an HTML viewer.
- Action input arguments are entered as free-form text in v1. Enumerated or constrained value lists declared in the SCPD are not surfaced as dropdowns or validated in v1 (the device will reject invalid values and the resulting fault will be shown).
- Event notifications are displayed in their received form (the popup shows what the device sent, parsed enough to be readable; richer per-service interpretation is out of scope for v1).
- The application is a single-user desktop tool; there are no accounts, persistence between sessions, or multi-user concerns.
