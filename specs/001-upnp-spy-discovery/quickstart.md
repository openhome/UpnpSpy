# Quickstart — UpnpSpy

**Feature**: 001-upnp-spy-discovery
**Date**: 2026-05-12
**Audience**: a developer cloning this repository for the first time who wants to build, run, and smoke-test UpnpSpy. Operational/end-user documentation lives elsewhere.

This document is a Phase-1 design artefact: it codifies the developer-facing entry points that the implementation must produce. The commands and paths below are the contract the build/repo layout commits to.

---

## 1. Prerequisites

| Tool | Version | Notes |
|---|---|---|
| Windows | 10 22H2 or Windows 11 | x64 or ARM64 |
| .NET SDK | 7.0 | `dotnet --list-sdks` should show `7.0.x` |
| Windows App SDK | 1.x (matches `Directory.Packages.props`) | Installed transitively by NuGet restore |
| Visual Studio 2022 (17.10+) **or** VS Code with C# Dev Kit | latest | "Windows App SDK C# Templates" workload required for designer support, not for build |
| Git | any recent | The repo is a Git repository |

You do **not** need administrator rights to develop. Running the app from an MSIX package on a developer machine the first time may require a one-off URL ACL grant for the event-callback `HttpListener`; the installed MSIX handles this for end users.

---

## 2. First-time setup

```powershell
git clone <repo-url> UpnpSpy
cd UpnpSpy

# Restore NuGet packages (centrally managed via Directory.Packages.props)
dotnet restore UpnpSpy.sln

# Build all projects (warnings-as-errors is on)
dotnet build UpnpSpy.sln -c Debug
```

The first build pulls down the Windows App SDK runtime components; subsequent builds are local.

---

## 3. Running the app

### As an unpackaged developer build (fastest inner loop)

```powershell
dotnet run --project src\UpnpSpy.App\UpnpSpy.App.csproj
```

The app launches as a non-packaged WinUI 3 app. SSDP discovery starts immediately; the device tree should populate within ~3 s on a LAN with reachable UPnP devices. If `View > Diagnostics` shows `Eventing.Callback bind failure`, register the URL ACL once:

```powershell
# One-time, per developer machine. Replace 5005 with the port shown in the diagnostic entry.
netsh http add urlacl url=http://+:5005/upnpspy/ user=Everyone
```

### As an MSIX-packaged build

```powershell
dotnet publish src\UpnpSpy.App\UpnpSpy.App.csproj -c Release -r win-x64 -p:Platform=x64 -p:WindowsPackageType=MSIX
# Output: src\UpnpSpy.App\bin\x64\Release\net7.0-windows\win-x64\UpnpSpy.App_<version>_x64.msix
```

Install with double-click (developer mode) or `Add-AppxPackage`. The MSIX installer registers the URL ACL; the packaged build needs no manual ACL step.

> **CLI packaging limitation.** Producing a signed, installable `.msix` from
> the command line in WinUI 3 currently requires Visual Studio 2022 with
> the **MSIX Packaging Tools** workload. Standalone `dotnet publish
> -p:WindowsPackageType=MSIX` produces the unpackaged binaries under
> `bin/.../win-x64/publish/`, which are sufficient for `dotnet run`-style
> developer testing but do not include the packaging step. To build the
> shipping artefact, open `UpnpSpy.sln` in Visual Studio 2022 and use
> **Project → Publish → Create App Packages…** on `UpnpSpy.App`.

---

## 4. Running the tests

```powershell
dotnet test UpnpSpy.sln
```

All default tests are unit tests in `tests/UpnpSpy.Tests/` and run without network access, without admin, and without real UPnP devices. The expected wall-clock for a clean `dotnet test` is a few seconds.

There is no live-device test project in v1 (it is reserved for a future opt-in `UpnpSpy.DeviceTests` project that lives outside the default CI configuration, satisfying Constitution gate 6).

---

## 5. Smoke test: end-to-end manual check

After `dotnet run`, with at least one known UPnP device on the LAN (a media renderer, NAS, or router):

| # | Action | Expected |
|---|---|---|
| 1 | Launch the app | Tree populates within ~5 s; the SSDP log on the right starts receiving rows |
| 1b | Wait ~2 s after a device row appears (FR-043) | The device label transitions from `uuid:<uuid>` to its human-readable friendly name **without** the user expanding the node |
| 2 | Expand a device node | Services appear as children immediately (no HTTP delay — the description was fetched eagerly at discovery time per FR-043) |
| 3 | Expand a service node | Actions appear as grandchildren |
| 4 | Right-click a device → **Fetch XML** | Default browser opens at the device's description URL |
| 5 | Right-click a service → **Fetch service XML** | Default browser opens at the service's SCPD URL |
| 6 | Double-click an action with no inputs (e.g., `RenderingControl::ListPresets`) | Invocation popup opens; click Invoke; popup shows output values |
| 7 | Double-click an action with inputs (e.g., `RenderingControl::GetVolume` with `InstanceID=0`, `Channel=Master`) | Invocation popup opens with editable inputs; Invoke returns the value |
| 8 | Right-click a service → **Subscribe** (e.g., `AVTransport`) | Subscription popup opens; trigger playback on the device; event rows appear |
| 9 | Close the subscription popup | App sends UNSUBSCRIBE (verify in a packet capture or via the device's logs) |
| 10 | **View > Rescan** | After ~4 s, devices that did not respond to this rescan are pruned |
| 11 | **View > Diagnostics** | Window opens listing recorded diagnostic entries with timestamps and categories |
| 12 | Power off a UPnP device (graceful) | Its tree entry disappears within ~2 s (byebye received) |

Steps 1–3 satisfy the user-story-3 acceptance criteria; steps 6–9 satisfy user stories 7–8; step 10 satisfies user story 6; step 11 satisfies FR-041.

---

## 6. Repository layout (developer's mental map)

```text
UpnpSpy.sln
Directory.Build.props          # Nullable, warnings-as-errors, language version
Directory.Packages.props       # Central package versions
.editorconfig                  # Style rules
NuGet.config                   # Restore sources

src/
├── UpnpSpy.Core/              # No UI deps; models, view-models, networking, diagnostics, abstractions
└── UpnpSpy.App/               # WinUI 3 host (XAML views, MSIX manifest, platform adapters)

tests/
└── UpnpSpy.Tests/             # xUnit; no network, no admin, no real devices

specs/
└── 001-upnp-spy-discovery/    # This feature's spec, plan, research, data model, contracts, tasks

docs/
└── specs/
    └── UPnP-arch-DeviceArchitecture-v1.0-20080424.pdf   # Authoritative protocol source (Constitution II)
```

---

## 7. Where to start reading

1. `specs/001-upnp-spy-discovery/spec.md` — what we are building and why.
2. `specs/001-upnp-spy-discovery/plan.md` — how the solution is structured.
3. `specs/001-upnp-spy-discovery/research.md` — every non-obvious technical decision and its UDA 1.0 citation.
4. `specs/001-upnp-spy-discovery/data-model.md` — the in-memory types you will see passed around.
5. `specs/001-upnp-spy-discovery/contracts/` — the abstraction seams; read these to understand where a given Windows API hides.
6. `specs/001-upnp-spy-discovery/tasks.md` (after `/speckit-tasks` runs) — the actionable task list to implement.

---

## 8. Common developer commands

```powershell
# Format / lint check
dotnet format UpnpSpy.sln --verify-no-changes

# Run only a single test class
dotnet test tests\UpnpSpy.Tests\UpnpSpy.Tests.csproj --filter FullyQualifiedName~SsdpMessageParserTests

# Watch + run the app (rebuild on file change)
dotnet watch --project src\UpnpSpy.App\UpnpSpy.App.csproj run

# Clean
dotnet clean UpnpSpy.sln
```

---

## 9. Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Empty tree, no SSDP rows in right pane | Multicast blocked (Windows Defender Firewall, hardened virtual switch, hotel Wi-Fi isolation) | Add an inbound rule for UDP 1900 on the active profile, or test on a different network. The app does not crash — the empty state is expected. |
| `Eventing.Callback bind failure` diagnostic on unpackaged developer build | `HttpListener` URL ACL not granted to the developer account | Run the `netsh http add urlacl` command from §3 once. |
| `dotnet build` fails with "treating warning as error" | A new warning was introduced | Fix the warning. The Constitution forbids relaxing the rule. |
| MSIX install fails with signature/trust error | Developer build signed with an unsigned/self-signed certificate | Trust the developer certificate or use `dotnet run` instead. |
| Tests randomly fail when run on a developer machine | A test is hitting real network despite the abstractions — bug | Open an issue; the test contract is that every default test runs offline. |
