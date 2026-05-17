<!--
SYNC IMPACT REPORT
==================
Version change: (template, unfilled) → 1.0.0
Bump rationale: Initial ratification — first concrete fill of placeholder template establishes
  four core principles plus governance. Treated as MAJOR baseline (1.0.0) per semver convention
  for the first authored revision of a previously empty/templated document.

Modified principles:
  - [PRINCIPLE_1_NAME] → I. Specification First (NON-NEGOTIABLE)
  - [PRINCIPLE_2_NAME] → II. UPnP Device Architecture v1.0 Is the Authoritative Source
  - [PRINCIPLE_3_NAME] → III. Windows Desktop Best Practices
  - [PRINCIPLE_4_NAME] → IV. Clean, Focused, Testable Source Files
  - [PRINCIPLE_5_NAME] → REMOVED (user requested 4 principles)

Added sections:
  - Technology & Platform Constraints (replaces [SECTION_2_NAME])
  - Development Workflow & Quality Gates (replaces [SECTION_3_NAME])
  - Governance (filled)

Removed sections: none beyond the unused fifth principle slot.

Templates requiring updates:
  - .specify/templates/plan-template.md — ✅ no edit needed (Constitution Check gate is generic;
    the principles below are referenced at plan time without template restructuring).
  - .specify/templates/spec-template.md — ✅ no edit needed (existing User Scenarios, Requirements,
    Success Criteria sections satisfy Principle I traceability).
  - .specify/templates/tasks-template.md — ✅ no edit needed (task IDs already carry [Story]
    labels supporting requirement traceability; tests remain opt-in per existing template).
  - .specify/templates/commands/*.md — n/a (directory not present).
  - README.md / docs/quickstart.md — n/a (not yet authored; will reference this constitution
    once created).

Follow-up TODOs: none. RATIFICATION_DATE set to today (2026-05-09) as this is the first
  ratified version.
-->

# UpnpSpy Constitution

## Core Principles

### I. Specification First (NON-NEGOTIABLE)

No implementation code is written before a specification exists for the behaviour it delivers.
Every task, commit, and source file MUST trace to a numbered requirement (FR-### or SC-###)
captured in `specs/<feature>/spec.md`. The flow is fixed: `/speckit-specify` → `/speckit-clarify`
(if ambiguity remains) → `/speckit-plan` → `/speckit-tasks` → `/speckit-implement`. Skipping
forward is not permitted; if a coding need surfaces mid-implementation that is not covered by
an existing requirement, work pauses and the spec is amended first.

**Rationale**: UPnP device control surfaces many subtle protocol and UX edge cases (transient
discovery, multicast reliability, vendor quirks). Without a written requirement, code drifts
toward ad-hoc behaviour that cannot be reviewed, tested, or re-derived. Traceability from
requirement → task → file → test is the mechanism that keeps the project honest.

### II. UPnP Device Architecture v1.0 Is the Authoritative Source

The document `docs/specs/UPnP-arch-DeviceArchitecture-v1.0-20080424.pdf` is the **single
authoritative reference** for every question about the UPnP protocol — addressing, discovery
(SSDP), description, control (SOAP), eventing (GENA), presentation, and message formats.

- Specs (`spec.md`), plans (`plan.md`), and code comments that touch protocol behaviour MUST
  cite the relevant section/page of that PDF when establishing required behaviour.
- Where any other source — vendor documentation, blog posts, Stack Overflow answers, third-party
  libraries, language-model recall, or later UPnP versions — contradicts this PDF, the PDF
  wins and the contradicting source is ignored for the purpose of defining required
  behaviour.
- Observed real-world deviations from the PDF (vendor quirks) are permitted only as
  explicitly scoped, requirement-tracked workarounds. Each such workaround MUST cite both the
  PDF section it deviates from and the device/firmware that necessitates the deviation.
- If a behavioural question is genuinely not covered by the PDF, the spec MUST mark the gap
  as `NEEDS CLARIFICATION` rather than silently adopting an external answer.

**Rationale**: UPnP has a long history of forks, revisions, and informal vendor practices.
Treating one document as the ground truth removes whole classes of disagreement during
review, makes correctness arguments auditable, and prevents subtle behavioural drift caused
by mixing sources.

### III. Windows Desktop Best Practices

The application MUST follow current Microsoft-recommended practices for C# desktop
development:

- Target a supported .NET runtime (.NET 7 or later); no .NET Framework 4.x.
- Enable nullable reference types (`<Nullable>enable</Nullable>`) and treat warnings as
  errors (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`) at the project level.
- Use `async`/`await` for all I/O (network discovery, SOAP control, event subscriptions);
  the UI thread MUST never be blocked. `.Result` and `.Wait()` on tasks are forbidden.
- Adopt the MVVM pattern for any XAML-based UI (WinUI 3 or WPF) using
  `CommunityToolkit.Mvvm` source generators. Code-behind is reserved for view-only concerns.
- Resolve dependencies via `Microsoft.Extensions.DependencyInjection`; configuration via
  `Microsoft.Extensions.Configuration` and `Options<T>`.
- Cancellation MUST be plumbed end-to-end through `CancellationToken` on every async API.
- Logging via `Microsoft.Extensions.Logging` with structured events; no `Console.WriteLine`
  diagnostics in shipping code paths.
- Central package management (`Directory.Packages.props`) and a checked-in `.editorconfig`
  govern style and versions.

**Rationale**: A network-facing desktop tool that talks to consumer devices will run on
arbitrary user machines for long sessions. The above practices are the difference between an
app that hangs the UI on a slow router and one that stays responsive, observable, and
diagnosable in the field.

### IV. Clean, Focused, Testable Source Files

Each C# source file MUST have a single, clearly named responsibility and MUST be
unit-testable in isolation:

- One public type per file; the file name matches the type.
- Files SHOULD be under ~200 lines; any file over 400 lines requires a justification comment
  or a refactor task.
- Public types depend on abstractions (interfaces) for any out-of-process collaborator
  (network, filesystem, clock, UI dialog). Concrete dependencies are injected, never
  `new`-ed inside business logic.
- No static mutable state. Singletons are registered through DI, not via `static` fields.
- Each non-trivial behaviour has at least one xUnit test covering the happy path and a
  representative failure path. Tests MUST run without network access, real devices, or admin
  privileges (use fakes/stubs at the abstraction boundary).
- View-models are tested without instantiating views; services are tested without
  instantiating view-models.

**Rationale**: Small focused files keep code reviews tractable, isolate risk when devices
behave unexpectedly, and let the team add UPnP vendor workarounds without destabilising the
core. Testability at the file level is the property that makes the spec-traceability of
Principle I provable rather than aspirational.

## Technology & Platform Constraints

- **Language/runtime**: C# (latest stable language version) on .NET 7 or later.
- **UI framework**: WinUI 3 (Windows App SDK) preferred for new work; WPF acceptable if a
  blocking WinUI 3 limitation is documented in `plan.md`. WinForms and UWP are out of scope.
- **Target OS**: Windows 10 22H2 and Windows 11 (x64, ARM64). Cross-platform is non-goal.
- **Networking**: SSDP discovery via `System.Net.Sockets` and HTTP/SOAP via `HttpClient`
  (single shared instance per service, configured through DI). No third-party UPnP library
  is adopted without an ADR justifying the dependency *and* a citation showing it conforms
  to the PDF named in Principle II.
- **Packaging**: MSIX for distribution; code-signed binaries. Single-file self-contained
  publish acceptable for developer/test builds.
- **Persistence**: Local user state (window layout, last-seen devices) stored under
  `%LOCALAPPDATA%\UpnpSpy\` as JSON; no database engine without an ADR.
- **Privilege**: The app MUST run as a standard (non-elevated) user. Any feature that would
  require elevation is explicitly out of scope until an ADR justifies it.

## Development Workflow & Quality Gates

1. **Specification gate** (Principle I): A feature begins by running `/speckit-specify`. No
   branch may contain implementation commits before `specs/<feature>/spec.md` exists on that
   branch with at least one FR-### and one SC-###.
2. **Protocol-source gate** (Principle II): Any FR-### that mandates protocol behaviour MUST
   cite the relevant section/page of
   `docs/specs/UPnP-arch-DeviceArchitecture-v1.0-20080424.pdf`. Reviewers reject PRs that
   introduce protocol behaviour without such a citation, or that rely on a contradicting
   source.
3. **Plan gate**: `/speckit-plan` MUST produce a Constitution Check section that explicitly
   confirms compliance with each of the four principles, or records a justified violation
   in the Complexity Tracking table.
4. **Task gate**: Every task in `tasks.md` carries a `[Story]` (or equivalent FR-###) tag so
   that requirement → task → file traceability can be audited.
5. **Build gate**: `dotnet build` MUST succeed with zero warnings (warnings-as-errors is on).
6. **Test gate**: `dotnet test` MUST pass with no excluded or skipped tests on the default
   CI configuration. Tests requiring real devices live under a separate, opt-in test project.
7. **Review gate**: Code review verifies that (a) every changed file maps to a task in
   `tasks.md`, (b) Principle II citations are present where applicable, (c) Principle III
   practices are followed, and (d) Principle IV file-level constraints (size, single
   responsibility, testability) hold for new and modified files.

Any gate failure blocks merge; gates may not be silently bypassed.

## Governance

This Constitution supersedes ad-hoc preferences and prior conventions. All pull requests,
spec amendments, and plan documents MUST verify compliance with the principles above.

Amendments follow this procedure:

1. Open a PR that edits this file and updates the Sync Impact Report comment at the top.
2. Bump `CONSTITUTION_VERSION` per semver:
   - **MAJOR**: a principle is removed, redefined incompatibly, or governance rules change
     in a way that breaks existing specs/plans.
   - **MINOR**: a principle or section is added, or guidance is materially expanded.
   - **PATCH**: clarifications, wording, or non-semantic refinements.
3. Update `LAST_AMENDED_DATE` to the merge date (ISO `YYYY-MM-DD`). `RATIFICATION_DATE`
   never changes after initial adoption.
4. Update dependent templates (`.specify/templates/*.md`) in the same PR if the amendment
   affects them, and record their status in the Sync Impact Report.

Complexity that violates a principle is acceptable only when a `plan.md` Complexity Tracking
row documents the violation, the alternative considered, and why it was rejected. Reviewers
MUST challenge unjustified entries.

Runtime developer guidance (build commands, repo layout conventions, contribution flow)
lives in `CLAUDE.md` and `README.md`; those documents reference this Constitution and MUST
not contradict it.

**Version**: 1.0.0 | **Ratified**: 2026-05-09 | **Last Amended**: 2026-05-09
