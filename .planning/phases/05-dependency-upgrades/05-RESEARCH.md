# Phase 5: Dependency Upgrades - Research

**Researched:** 2026-06-12
**Domain:** .NET 8 NuGet dependency management (central package management), CLI command-dispatch refactor, AOT preservation, Spectre.Console table rendering
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**GA versions + central management + dead refs (DEP-01, DEP-02):**
- Add a root `Directory.Packages.props` with `ManagePackageVersionsCentrally=true`; move ALL package versions there; remove per-csproj `Version=` attributes (bare `<PackageReference Include=.../>` in csproj + `<PackageVersion>` in the props).
- Upgrade every package to its latest STABLE GA release; STAY on .NET 8 (`net8.0` / `net8.0-windows`). Targets: `System.Reactive` (6.0.x GA, drop `-preview.1`), `xunit` + `xunit.runner.visualstudio` (latest GA 2.x), `Microsoft.NET.Test.Sdk` (latest GA, drop `-preview`), `coverlet.collector` (GA), `Microsoft.Extensions.Logging.*` (8.0.x GA, drop `-preview.5`).
- Remove unused core-lib package refs (Phase 3 WR-04): `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging.Console`, `Microsoft.Extensions.Logging.Debug` — the core lib only needs `Microsoft.Extensions.Logging.Abstractions`.

**System.CommandLine + Rendering replacement (DEP-03):**
- DROP `System.CommandLine` (beta) AND `System.CommandLine.Rendering` (alpha) entirely from BOTH CLI hosts.
- Replace command parsing with a small HAND-ROLLED arg dispatch (switch on `args[0]`) — zero dependency, AOT-safe, both hosts.
- The **managed CLI** (`SwtorLogParser.Cli`) replaces its `System.CommandLine.Rendering` `TableView` with **Spectre.Console** (GA) for the live table.
- The **Native AOT CLI** (`SwtorLogParser.Native.Cli`) keeps its PERF-02 in-place console renderer; do NOT add Spectre.Console to the AOT host unless research confirms it is fully AOT/trim-safe there. Confine Spectre.Console to the non-AOT managed CLI by default.
- Preserve each host's behavior: same commands (`list`, `monitor`), same columns (Player, DPS, crit%, HPS, crit%), same cancellation (Ctrl+C → Stop()).

**Docker target (INFRA-02):**
- Remove `DockerDefaultTargetOS=Linux` from all csproj that have it (the CLI projects). No Dockerfile exists; Windows-only app. Remove any other misleading cross-platform properties.

### Claude's Discretion
- Exact GA version numbers (research-confirmed), the precise hand-rolled dispatch shape, the Spectre.Console table styling (match current columns), and whether Spectre.Console can also serve the Native CLI (only if proven AOT-safe) — guided by green tests, a successful `dotnet build SwtorLogParser.slnx`, and a still-AOT-publishable Native CLI.

### Deferred Ideas (OUT OF SCOPE)
- CI pipeline (INFRA-01) → Phase 6.
- BoundedCache library swap (BitFaster) — NOT required; in-repo BoundedCache stays.
- Overlay-topmost (BL-01), DateTime.Now filter (IN-01) — unrelated.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| DEP-01 | All NuGet packages on stable GA versions (no preview/alpha/beta) — all `*.csproj` | Exact GA versions confirmed against NuGet registry (`api.nuget.org` flat-container + package pages); see Standard Stack table. Every current pin is preview/alpha/beta; all have GA replacements that target net8.0. |
| DEP-02 | Package versions centrally managed via `Directory.Packages.props` | CPM mechanics, exact props structure, NU1008 pitfall, and per-project participation (incl. AOT + WinForms) documented in Architecture Patterns + Pitfalls. |
| DEP-03 | CLI rendering no longer depends on abandoned `System.CommandLine.Rendering 0.4.0-alpha`; uses a supported approach — `SwtorLogParser.Cli` | Hand-rolled dispatch pattern + Spectre.Console `Table` example (5 columns mapped) + cancellation-rewiring pattern for BOTH hosts documented in Code Examples. |
| INFRA-02 | Misleading `DockerDefaultTargetOS=Linux` removed from CLI projects — `*.csproj` | Confirmed present only in `SwtorLogParser.Cli.csproj:9` and `SwtorLogParser.Native.Cli.csproj:8`; no Dockerfile in repo. Simple property deletion. |
</phase_requirements>

## Summary

This is a low-architectural-risk, high-attention-to-detail dependency hardening phase: move every NuGet pin from preview/alpha/beta to stable GA, centralize versions in `Directory.Packages.props`, delete dead core-lib refs, replace both `System.CommandLine` *and* `System.CommandLine.Rendering` with a hand-rolled `switch (args[0])` dispatcher, and render the managed CLI's live table with Spectre.Console (GA) while the Native AOT CLI keeps its existing PERF-02 cursor renderer. All target frameworks stay on `net8.0` / `net8.0-windows`.

Every required GA version was confirmed against the live NuGet registry and verified to target `net8.0`. A notable finding: **System.CommandLine reached a true stable GA (2.0.0) in November 2025**, current stable `2.0.9` (released 2026-06-09). This means the "no beta" criterion alone no longer *forces* its removal — but the locked decision drops it regardless, which is the correct call: the `Rendering` companion is still alpha-only and abandoned, the 2.0 GA introduced breaking API changes from the pinned beta4 (`IConsole` removed, `SetHandler` binding model changed), and a two-command surface is trivially hand-rolled with zero dependency and guaranteed AOT-safety. Spectre.Console's *base* package is AOT-friendly, but `Spectre.Console.Cli` is explicitly NOT trim/AOT-safe — we use only the base `Table`, and only in the non-AOT managed host, so this is clean.

The biggest landmines are mechanical: NU1008 if any `Version=` attribute survives on a `PackageReference` under CPM; accidentally leaking Spectre.Console into the AOT host; and the cancellation rewiring — both hosts today rely on `System.CommandLine`'s `context.GetCancellationToken()` to convert Ctrl+C into the token that the `ManualResetEvent` waits on. Removing the framework removes that wiring, so each host must install its own `Console.CancelKeyPress` → `CancellationTokenSource` bridge or the `monitor` command will no longer stop cleanly on Ctrl+C.

**Primary recommendation:** Create one root `Directory.Packages.props`, set the exact GA versions in the table below, strip all `Version=` attributes and the three dead core-lib refs and both Docker properties, hand-roll a shared-shape `switch (args.Length > 0 ? args[0] : "")` dispatcher in both `Program.cs` files with a `Console.CancelKeyPress` → `CancellationTokenSource` cancellation bridge, port the managed CLI's 5-column `TableView` to a Spectre.Console `Table`, then gate on `dotnet restore` → `dotnet build SwtorLogParser.slnx` → `dotnet test` (106 green) → `dotnet publish SwtorLogParser.Native.Cli -c Release` (AOT).

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Package version pinning | Build/Repo root (`Directory.Packages.props`) | each `.csproj` | CPM centralizes versions; csproj only declares *which* packages, not versions. |
| GA package resolution | NuGet restore (build tier) | — | Versions are a build-time concern; no runtime behavior change intended. |
| CLI argument dispatch | Host entry point (`Program.cs` of each CLI) | — | Trivial command routing belongs in the host `Main`, not the core lib (which stays host-agnostic + AOT-compatible). |
| Live table rendering (managed CLI) | Managed CLI host (Spectre.Console) | — | Presentation concern, host-local; non-AOT host can carry a reflection-tolerant rendering lib. |
| Live table rendering (Native AOT CLI) | Native CLI host (existing PERF-02 cursor renderer) | — | AOT host must avoid any reflection-heavy dependency; keeps its hand-written renderer. |
| Cancellation (Ctrl+C → stop monitor) | Host entry point (`CancellationTokenSource` + `Console.CancelKeyPress`) | core lib `Start(token)`/`Stop()` | The token plumbing into the monitor already exists; only the host-side *source* of cancellation (previously System.CommandLine) must be rebuilt. |
| Logging abstractions (core lib) | Core lib (`Microsoft.Extensions.Logging.Abstractions` only) | hosts (no providers needed today) | Verified: core lib only uses `ILogger<T>` + `NullLogger<T>`; no DI container, no Console/Debug providers anywhere. |

## Standard Stack

### Core (exact GA versions — verified against NuGet registry 2026-06-12)

| Library | Current (preview) pin | → GA Version | Targets net8.0? | Purpose | Verified |
|---------|----------------------|--------------|-----------------|---------|----------|
| System.Reactive | 6.0.1-preview.1 | **6.0.2** (recommended) or 6.1.0 | yes | Rx.NET — DPS/HPS/APM stream | `[VERIFIED: nuget.org flat-container]` |
| Microsoft.Extensions.Logging.Abstractions | 8.0.0-preview.5.23280.8 | **8.0.3** | yes (8.0.x line) | `ILogger<T>` / `NullLogger<T>` in core lib | `[VERIFIED: nuget.org flat-container]` |
| xunit | 2.5.0-pre.44 | **2.9.3** | yes | Test framework | `[VERIFIED: nuget.org flat-container]` |
| xunit.runner.visualstudio | 2.5.0-pre.27 | **3.1.5** | yes | Test runner/adapter | `[VERIFIED: nuget.org flat-container]` |
| Microsoft.NET.Test.Sdk | 17.7.0-preview.23280.1 | **18.6.0** | yes | Test SDK / host | `[VERIFIED: nuget.org flat-container]` |
| coverlet.collector | 6.0.0 (already GA) | **6.0.4** (conservative) or 10.0.1 | yes | Coverage collector | `[VERIFIED: nuget.org flat-container]` |
| Spectre.Console | (new) | **0.57.0** | yes (net8.0 + netstandard2.0) | Managed CLI live `Table` | `[VERIFIED: nuget.org/packages/Spectre.Console]` |

### Removed (no replacement — dead refs / abandoned)

| Library | Current pin | Action | Why |
|---------|-------------|--------|-----|
| Microsoft.Extensions.DependencyInjection | 8.0.0-preview.5 | REMOVE from core lib | Unused (Phase 3 WR-04 moved DI host-side); core lib uses no `ServiceCollection`. Verified by grep — zero usages in `SwtorLogParser/`. |
| Microsoft.Extensions.Logging.Console | 8.0.0-preview.5 | REMOVE from core lib | Provider, not used by core lib; no `AddConsole` anywhere. |
| Microsoft.Extensions.Logging.Debug | 8.0.0-preview.5 | REMOVE from core lib | Provider, not used by core lib; no `AddDebug` anywhere. |
| System.CommandLine | 2.0.0-beta4.22272.1 | REMOVE from Native CLI | Replaced by hand-rolled dispatch (DEP-03). |
| System.CommandLine.Rendering | 0.4.0-alpha.22272.1 | REMOVE from managed CLI | Abandoned alpha; replaced by Spectre.Console `Table` (DEP-03). |

> Note: the core lib's `Microsoft.Extensions.Logging` namespace (the `ILogger` interface) is provided by `Microsoft.Extensions.Logging.Abstractions` — **not** by the `.Logging.Console`/`.Debug` provider packages. Removing the two providers does not break the `using Microsoft.Extensions.Logging;` in `CombatLogsMonitor.cs`. `[VERIFIED: codebase grep + package contents]`

### Version-choice rationale (Claude's-discretion picks)

- **System.Reactive 6.0.2** over 6.1.0: 6.0.2 is the latest patch on the same minor the project already pinned a preview of, minimizing behavioral surprise on the critical DPS/HPS stream. 6.1.0 is also GA and net8.0-compatible; either satisfies DEP-01. Recommend 6.0.2 for the smallest blast radius; bump to 6.1.0 only if a 6.0.2 issue surfaces.
- **coverlet.collector 6.0.4** over 10.0.x: 6.0.x is the conservative line matching the existing pin; 10.0.1 exists and is GA. Coverage is a dev-only concern (test project), so either is safe — pick 6.0.4 to minimize change unless a newer collector is needed for the chosen Test.Sdk.
- **xunit.runner.visualstudio 3.1.5** and **Microsoft.NET.Test.Sdk 18.6.0** are major-version jumps from the pinned previews (2.5.0-pre → 3.x; 17.7-preview → 18.x). These are dev/test-host packages — the risk is test discovery/run, fully caught by the `dotnet test` gate. xunit core stays on the **2.x** GA line (2.9.3) per the locked decision (not xunit v3, which is a separate package id `xunit.v3`).

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Hand-rolled dispatch | System.CommandLine 2.0.9 (now GA) | GA exists, but pulls a runtime dep for a 2-command surface, requires rewriting the beta4 handler model anyway, and the abandoned Rendering companion still has to go. Hand-roll is zero-dep + guaranteed AOT-safe. Locked: hand-roll. |
| Spectre.Console (managed CLI) | Keep manual cursor rendering like Native CLI | Possible, but the managed CLI's value is a richer table; Spectre `Table` is the supported, AOT-irrelevant (non-AOT host) choice. Locked: Spectre. |
| xunit 2.9.3 | xunit.v3 (3.x) | v3 is a different package family with a different runner model — out of scope; locked decision says "latest GA 2.x". |

**Installation (after `Directory.Packages.props` exists):**
```bash
# No per-project version flags — versions live in Directory.Packages.props.
# Managed CLI gains Spectre.Console:
dotnet add SwtorLogParser.Cli/SwtorLogParser.Cli.csproj package Spectre.Console
# (then move the emitted Version= into Directory.Packages.props as <PackageVersion>)
```

**Version verification commands (re-run at execution time — versions drift):**
```bash
curl -s https://api.nuget.org/v3-flatcontainer/spectre.console/index.json
curl -s https://api.nuget.org/v3-flatcontainer/system.reactive/index.json
curl -s https://api.nuget.org/v3-flatcontainer/xunit/index.json
curl -s https://api.nuget.org/v3-flatcontainer/microsoft.net.test.sdk/index.json
```

## Package Legitimacy Audit

> The legitimacy seam (`gsd-tools query package-legitimacy check`) supports npm/pypi/crates only — **not NuGet**. Verification was performed manually against the NuGet registry (download counts, age, owner, source repo) per the protocol's intent.

| Package | Registry | Age / Status | Downloads | Source Repo | Verdict | Disposition |
|---------|----------|--------------|-----------|-------------|---------|-------------|
| System.Reactive 6.0.2 | NuGet | mature 6.x line | very high (Rx.NET, dotnet/reactive) | github.com/dotnet/reactive | OK | Approved (upgrade) |
| Microsoft.Extensions.Logging.Abstractions 8.0.3 | NuGet | Microsoft 1st-party, 8.0 line | very high | github.com/dotnet/runtime | OK | Approved (upgrade) |
| xunit 2.9.3 | NuGet | stable 2.x | very high | github.com/xunit/xunit | OK | Approved (upgrade) |
| xunit.runner.visualstudio 3.1.5 | NuGet | current GA | very high | github.com/xunit/visualstudio.xunit | OK | Approved (major bump — dev only) |
| Microsoft.NET.Test.Sdk 18.6.0 | NuGet | Microsoft 1st-party | very high | github.com/microsoft/vstest | OK | Approved (major bump — dev only) |
| coverlet.collector 6.0.4 | NuGet | stable | very high | github.com/coverlet-coverage/coverlet | OK | Approved (upgrade) |
| Spectre.Console 0.57.0 | NuGet | published 2026-06-11 | **44.6M total**, ~949K/day | github.com/spectreconsole/spectre.console | OK | Approved (NEW — managed CLI only) |

**Packages removed due to [SLOP] verdict:** none
**Packages flagged as suspicious [SUS]:** none

All versions were discovered from authoritative sources (NuGet registry + official package pages) and cross-checked. Spectre.Console — the only genuinely new external dependency — is a well-known library (44.6M downloads, named maintainers Patrik Svensson / Phil Scott / Nils Andresen / Cédric Luthi, official GitHub repo). No `[ASSUMED]` package names in this phase.

## Architecture Patterns

### System Architecture Diagram

```
                 ┌─────────────────────────────────────┐
   args[] ──────▶│  Host Program.Main (each CLI)        │
                 │  switch (args[0]):                   │
                 │    "list"    → ListCombatLogs()      │──▶ Console.WriteLine(file)
                 │    "monitor" → MonitorCombatLogs()   │
                 │    _         → PrintUsage()          │
                 └───────────────┬─────────────────────┘
                                 │ monitor
                                 ▼
   Ctrl+C ──▶ Console.CancelKeyPress ──▶ CancellationTokenSource.Cancel()
                                 │ token
                                 ▼
                 CombatLogsMonitor.Instance.Start(token)   (core lib — unchanged)
                                 │  IObservable<PlayerStats> DpsHps
                                 ▼
              ┌──────────────────┴───────────────────┐
              │ managed CLI                           │ Native AOT CLI
              │ Spectre.Console Table (5 cols)        │ existing PERF-02 cursor renderer
              │ (non-AOT host)                        │ (no new dep — AOT-safe)
              └──────────────────────────────────────┘
                                 │ token cancelled
                                 ▼
              token.WaitHandle / CTS → Stop() winds down monitor
```

### Recommended Project Structure (additions only)
```
swtor-logparser/
├── Directory.Packages.props   # NEW — ManagePackageVersionsCentrally + all <PackageVersion>
├── SwtorLogParser.slnx        # unchanged (CPM is auto-discovered by MSBuild walking up)
├── SwtorLogParser/            # core lib — drop 3 dead refs, strip Version=
├── SwtorLogParser.Cli/        # drop SCL.Rendering + Docker prop; add Spectre.Console; hand-roll dispatch
├── SwtorLogParser.Native.Cli/ # drop SCL + Docker prop; hand-roll dispatch; keep renderer
├── SwtorLogParser.Overlay/    # net8.0-windows — no package refs; participates in CPM (none to pin)
└── SwtorLogParser.Tests/      # strip Version=; bump 4 test packages
```

### Pattern 1: Central Package Management (CPM)
**What:** A root `Directory.Packages.props` declares every version once via `<PackageVersion>`; each `.csproj` references packages with **no** `Version=`.
**When to use:** Multi-project repos that must keep versions identical and auditable — exactly this phase's goal (DEP-02).
**Example:**
```xml
<!-- Source: CITED learn.microsoft.com/nuget/consume-packages/central-package-management -->
<!-- Directory.Packages.props at repo root -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="System.Reactive" Version="6.0.2" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.3" />
    <PackageVersion Include="Spectre.Console" Version="0.57.0" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.6.0" />
    <PackageVersion Include="coverlet.collector" Version="6.0.4" />
  </ItemGroup>
</Project>
```
Each consuming csproj then uses bare references:
```xml
<PackageReference Include="System.Reactive" />          <!-- core lib -->
<PackageReference Include="Spectre.Console" />           <!-- managed CLI only -->
```

**CPM participation notes (resolves CONTEXT question 3):**
- MSBuild auto-discovers `Directory.Packages.props` by walking up from each project to the repo root. The `.slnx` solution needs no change.
- **Native AOT csproj** participates identically — `PublishAot=true` is a project property, orthogonal to CPM. After dropping `System.CommandLine`, the Native CLI has **only** `Microsoft.Extensions.Logging.Abstractions` left as a package reference (bare, version from props). AOT compatibility is unaffected by *where* the version is declared.
- **WinForms csproj** (`net8.0-windows`) has **zero** `<PackageReference>` entries today — it only has a `<ProjectReference>`. It silently participates in CPM with nothing to pin. No change needed beyond confirming no stray `Version=` appears.
- Keep `<IncludeAssets>`/`<PrivateAssets>` metadata on the test-project references (xunit.runner.visualstudio, coverlet.collector) — CPM moves only the **version**, not the asset metadata.

### Pattern 2: Hand-rolled command dispatch (replaces RootCommand/Command/SetHandler)
**What:** A `switch` on `args[0]` routing to the existing handler methods; zero dependency; identical in both hosts.
**When to use:** A small, fixed command surface (`list`, `monitor`) where a parsing framework is overkill and (for the AOT host) reflection is forbidden.
**Example:** see Code Examples below (full per-host pattern with cancellation bridge).

### Pattern 3: Cancellation bridge (replaces context.GetCancellationToken())
**What:** A `CancellationTokenSource` cancelled from `Console.CancelKeyPress`, replacing System.CommandLine's built-in Ctrl+C → token wiring.
**Why required:** Both hosts today obtain their stop-token from `context.GetCancellationToken()` and pin a `ManualResetEvent` to `token.WaitHandle`. Remove the framework and that token vanishes — `monitor` would never stop on Ctrl+C. The bridge restores identical behavior.

### Anti-Patterns to Avoid
- **Leaving `Version=` on any `PackageReference` under CPM** → NU1008 build error (see Pitfall 1).
- **Adding Spectre.Console to the Native AOT csproj** → risks AOT trim warnings and defeats the whole reason for hand-rolling. Confine it to `SwtorLogParser.Cli`.
- **Using `Spectre.Console.Cli`** (the `CommandApp`/`CommandSettings` framework) → explicitly marked NOT trim/AOT-safe by the maintainers; we are NOT using it. Only the base `Spectre.Console` `Table`.
- **Letting `e.Cancel = false` (default) on CancelKeyPress** → the process is killed before clean `Stop()`. Set `e.Cancel = true` and cancel the CTS yourself (see example).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Multi-project version sync | Per-csproj manual version edits kept in lockstep | `Directory.Packages.props` (CPM) | First-party MSBuild feature; single source of truth; this *is* DEP-02. |
| Rich console table layout (managed CLI) | Manual column-width/padding math | Spectre.Console `Table` | The alpha `TableView` did exactly this; Spectre is the supported successor with star-column-equivalent sizing. |
| Ctrl+C handling | Polling `Console.KeyAvailable` | `Console.CancelKeyPress` + `CancellationTokenSource` | BCL event fires on the real SIGINT/Ctrl+C; integrates with the existing token plumbing. |

**Key insight:** The *only* thing worth hand-rolling here is the two-command dispatcher — and that's deliberate, because the alternative (System.CommandLine) is either AOT-hostile reflection or an oversized dependency for a fixed surface. Everything else (versions, table, cancellation) uses a supported primitive.

## Runtime State Inventory

> This is a dependency/build refactor, not a data rename. Most categories are N/A, but the inventory is completed explicitly.

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | None — no datastore, DB, or persisted keys reference any package name. Verified: STACK.md says no env/config/db files. | none |
| Live service config | None — no external service, no CI yet (Phase 6). The `DockerDefaultTargetOS=Linux` property points to a Dockerfile that does not exist (verified: no Dockerfile in repo). | Remove the dead property (INFRA-02). |
| OS-registered state | None — no Task Scheduler / service registration referencing these packages. | none |
| Secrets/env vars | None — STACK.md confirms no `.env`, no secrets, no env vars read by the app. | none |
| Build artifacts | `obj/`/`bin/` per project hold restored package graphs from the OLD preview versions and the OLD `project.assets.json`. After the csproj/props edits, a stale `obj/` can mask NU1008 or resolve old versions. | Run `dotnet restore` (or clean `obj/`) before building; the build gate covers this. |

**The canonical question — after every file is updated, what still references the old packages?** Only the per-project `obj/project.assets.json` restore caches. A `dotnet restore` regenerates them; no manual migration needed.

## Common Pitfalls

### Pitfall 1: NU1008 — `Version=` survives under CPM
**What goes wrong:** Build/restore fails: `NU1008: Projects that use central package version management should not define the version on the PackageReference items but on the PackageVersion items: <Package>`.
**Why it happens:** A `<PackageReference Include="X" Version="Y" />` still carries `Version=` after `ManagePackageVersionsCentrally=true` is enabled.
**How to avoid:** Strip the `Version=` attribute from EVERY `PackageReference` in all 5 csproj. Current files with version attributes to clean: `SwtorLogParser.csproj:15-18`, `SwtorLogParser.Cli.csproj:22`, `SwtorLogParser.Native.Cli.csproj:18-19`, `SwtorLogParser.Tests.csproj:13,14,15,19`. (Overlay has none.)
**Warning signs:** Restore fails immediately naming the offending package.

### Pitfall 2: Spectre.Console pulled into the AOT host
**What goes wrong:** AOT publish emits trim/`IL2xxx`/`IL3xxx` warnings (or fails under `TreatWarningsAsErrors`), or binary bloats.
**Why it happens:** Adding the Spectre `<PackageVersion>` to the *shared* props is fine, but adding a `<PackageReference Include="Spectre.Console" />` to `SwtorLogParser.Native.Cli.csproj` brings it into the AOT graph.
**How to avoid:** Add the `<PackageReference>` ONLY to `SwtorLogParser.Cli.csproj`. The base Spectre package is AOT-friendly (PR #1690) but the locked decision keeps it out of the AOT host by default — honor that.
**Warning signs:** `dotnet publish SwtorLogParser.Native.Cli -c Release` shows new trim warnings that weren't there pre-Spectre.

### Pitfall 3: Lost Ctrl+C cancellation after dropping System.CommandLine
**What goes wrong:** `monitor` runs but Ctrl+C no longer stops it cleanly (or kills the process mid-write).
**Why it happens:** `context.GetCancellationToken()` (System.CommandLine) was the *source* of the cancellation token the `ManualResetEvent` waited on. Removing the framework removes that source.
**How to avoid:** Add a `CancellationTokenSource` + `Console.CancelKeyPress` handler (set `e.Cancel = true`, call `cts.Cancel()`), and either keep the `ManualResetEvent`-on-`token.WaitHandle` pattern or block on `cts.Token.WaitHandle.WaitOne()`. See Code Examples.
**Warning signs:** Manual smoke test: `dotnet run -- monitor`, press Ctrl+C → process must exit promptly without an unhandled exception.

### Pitfall 4: Breaking API change in a GA bump
**What goes wrong:** Compile errors after upgrading (most likely the test-host packages or Test.Sdk major jumps).
**Why it happens:** `Microsoft.NET.Test.Sdk` 17.7-preview → 18.6.0 and `xunit.runner.visualstudio` 2.5-pre → 3.1.5 are major bumps; xunit 2.5-pre → 2.9.3 stays in-family. System.Reactive 6.0.x is API-stable.
**How to avoid:** Upgrade, then `dotnet build` + `dotnet test`. xunit 2.x test source (`[Fact]`/`[Theory]`/`Assert`/`Record.Exception`) is unchanged across 2.5→2.9. The runner/Test.Sdk bumps affect *discovery/run*, fully caught by the test gate.
**Warning signs:** Build error referencing a moved type, or `dotnet test` discovering 0 tests (runner mismatch).

### Pitfall 5: net8.0 build under a .NET 10-only SDK
**What goes wrong:** Restore/build/AOT-publish could fail to find the net8.0 targeting/runtime pack.
**Why it happens:** This machine has only .NET 10 SDKs (`10.0.108/204/300/301`) and the .NET 8 **runtime** (`8.0.28`) but **no .NET 8 SDK** and no visible net8.0 host targeting pack. The .NET 10 SDK *can* build/test `net8.0` when the runtime pack is present, and pulls the net8.0 targeting pack + (for AOT) the ILCompiler from NuGet on demand.
**How to avoid:** Treat `dotnet restore` + `dotnet build SwtorLogParser.slnx` + `dotnet publish SwtorLogParser.Native.Cli -c Release` as explicit gates; if the targeting pack is missing, restore will report `NETSDK1045`/missing-pack and the fix is `dotnet workload`/pack acquisition or installing the .NET 8 SDK. Network access for NuGet (ILCompiler, targeting pack) must be available for the first AOT publish.
**Warning signs:** `error NETSDK1045: The current .NET SDK does not support targeting .NET 8.0` or an ILCompiler restore failure on AOT publish.

## Code Examples

### Hand-rolled dispatch — managed CLI (`SwtorLogParser.Cli/Program.cs`)
```csharp
// Replaces RootCommand/Command/SetHandler. No System.CommandLine.* usings remain.
using SwtorLogParser.Monitor;
using SwtorLogParser.View;
using Spectre.Console;

namespace SwtorLogParser.Cli;

public static class Program
{
    private static readonly SlidingExpirationList List = new(TimeSpan.FromSeconds(30));

    public static int Main(string[] args)
    {
        var command = args.Length > 0 ? args[0] : "";
        switch (command)
        {
            case "list":    ListCombatLogs(); return 0;
            case "monitor": MonitorCombatLogs(); return 0;
            default:
                Console.Error.WriteLine("Usage: SwtorLogParser.Cli [list|monitor]");
                return 1;
        }
    }

    private static void MonitorCombatLogs()
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        CombatLogsMonitor.Instance.CombatLogAdded += OnCombatLogAdded;
        using var sub = CombatLogsMonitor.Instance.DpsHps.Subscribe(Update);
        CombatLogsMonitor.Instance.Start(cts.Token);

        cts.Token.WaitHandle.WaitOne();      // blocks until Ctrl+C cancels
        CombatLogsMonitor.Instance.Stop();   // explicit clean wind-down
    }

    private static void Update(CombatLogsMonitor.PlayerStats playerStats)
    {
        List.AddOrUpdate(playerStats);

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Player");
        table.AddColumn("dps");
        table.AddColumn("(crit %)");
        table.AddColumn("hps");
        table.AddColumn("(crit %)");

        foreach (var x in List.Items)
        {
            table.AddRow(
                x.Player.Name ?? "-",
                x.DPS.HasValue     ? x.DPS.Value.ToString("N")     : "-",
                x.DPSCritP.HasValue? x.DPSCritP.Value.ToString("N"): "-",
                x.HPS.HasValue     ? x.HPS.Value.ToString("N")     : "-",
                x.HPSCritP.HasValue? x.HPSCritP.Value.ToString("N"): "-");
        }

        AnsiConsole.Clear();   // live refresh; or use AnsiConsole.Live(table) for flicker-free updates
        AnsiConsole.Write(table);
    }

    private static void OnCombatLogAdded(object? _, CombatLog combatLog)
        => AnsiConsole.MarkupLineInterpolated($"[grey]{combatLog.FileInfo}[/]");

    private static void ListCombatLogs()
    {
        foreach (var combatLog in CombatLogs.EnumerateCombatLogs())
            Console.WriteLine(combatLog);
    }
}
```
Column mapping (resolves CONTEXT question 2): the 5 current `TableView` columns — `Player.Name`, `DPS` (`"dps"`), `DPSCritP` (`"(crit %)"`), `HPS` (`"hps"`), `HPSCritP` (`"(crit %)"`), each `ColumnDefinition.Star(0.2)` — map 1:1 to the five `AddColumn`/`AddRow` cells above. `PlayerStats` shape confirmed: `Actor Player`, `double? DPS/DPSCritP/HPS/HPSCritP`. `[VERIFIED: CombatLogsMonitor.cs:318-329]`

> Live-update note: the alpha `TableView` re-rendered into a fixed `Region`. Spectre's idiomatic flicker-free equivalent is `AnsiConsole.Live(table).Start(ctx => { ... ctx.Refresh(); })`. A simple `AnsiConsole.Clear()` + `Write(table)` per event preserves current behavior most directly; `Live` is a styling improvement at Claude's discretion.

### Hand-rolled dispatch + cancellation — Native AOT CLI (`SwtorLogParser.Native.Cli/Program.cs`)
```csharp
// Replaces RootCommand/Command/SetHandler AND context.GetCancellationToken()/ManualResetEvent.
// Keeps the existing PERF-02 cursor renderer (Update/FormatRow/OnCombatLogAdded) verbatim.
using SwtorLogParser.Monitor;
using SwtorLogParser.View;

namespace SwtorLogParser.Native.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        var command = args.Length > 0 ? args[0] : "";
        switch (command)
        {
            case "list":    ListCombatLogs(); return 0;
            case "monitor": MonitorCombatLogs(); return 0;
            default:
                Console.Error.WriteLine("Usage: SwtorLogParser.Native.Cli [list|monitor]");
                return 1;
        }
    }

    private static void MonitorCombatLogs()
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var list = new SlidingExpirationList(TimeSpan.FromSeconds(30));
        CombatLogsMonitor.Instance.CombatLogAdded += OnCombatLogAdded;
        using var sub = CombatLogsMonitor.Instance.DpsHps.Subscribe(s => Update(list, s));
        CombatLogsMonitor.Instance.Start(cts.Token);

        cts.Token.WaitHandle.WaitOne();      // replaces ManualResetEvent-on-token.WaitHandle
        CombatLogsMonitor.Instance.Stop();
    }

    // Update(...), FormatRow(...), OnCombatLogAdded(...), ListCombatLogs() — UNCHANGED from
    // the existing PERF-02 implementation (Native.Cli/Program.cs:36-121). Only the Main +
    // MonitorCombatLogs signatures change (no InvocationContext parameter).
}
```
Cancellation mapping (resolves CONTEXT question 4): `context.GetCancellationToken()` → `cts.Token`; `ManualResetEvent` pinned to `token.WaitHandle.SafeWaitHandle` → `cts.Token.WaitHandle.WaitOne()`; Ctrl+C (previously framework-handled) → `Console.CancelKeyPress` with `e.Cancel = true; cts.Cancel();`. The existing `Start(token)`/`Stop()` core API is untouched. `[VERIFIED: CombatLogsMonitor.cs:177-211 + current Program.cs]`

### Removing the Docker property (INFRA-02)
```xml
<!-- DELETE this line from SwtorLogParser.Cli.csproj (line 9) and
     SwtorLogParser.Native.Cli.csproj (line 8). No other csproj has it. -->
<DockerDefaultTargetOS>Linux</DockerDefaultTargetOS>
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| System.CommandLine beta4 (`SetHandler`, `IConsole`, `InvocationContext`) | System.CommandLine 2.0 GA (revised handler model, `IConsole` removed) | Nov 2025 (2.0.0 GA; 2.0.9 latest) | Confirms the pinned beta4 is *two* eras stale; we drop it entirely rather than migrate. |
| System.CommandLine.Rendering alpha `TableView` | Spectre.Console `Table` (or `AnsiConsole.Live`) | abandoned alpha since 2022 | Supported, maintained successor for the managed CLI table. |
| Per-csproj `Version=` pins | `Directory.Packages.props` (CPM) | GA since .NET SDK 6+ / NuGet 6 | First-party central version management — this phase's DEP-02. |
| Core lib carries DI + Logging providers | Core lib carries only `Logging.Abstractions`; providers host-side | Phase 3 (WR-04 / RFCT-02) | Removing the 3 dead refs is finishing Phase 3's intent; verified no usages remain. |

**Deprecated/outdated:**
- `System.CommandLine.Rendering 0.4.0-alpha` — abandoned since 2022; no GA ever shipped. Replaced by Spectre.Console.
- All preview/alpha/beta pins in the current csproj set — every one has a GA replacement targeting net8.0.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | The .NET 10 SDK present on this machine can build/test/AOT-publish `net8.0` projects given the net8.0 runtime (8.0.28) is installed and NuGet can fetch the net8.0 targeting pack + ILCompiler. | Pitfall 5 / Environment | If the targeting pack can't be acquired, the build/AOT gate fails with NETSDK1045 until the .NET 8 SDK or pack is installed. Mitigation: gates catch it; install .NET 8 SDK if needed. |
| A2 | xunit 2.5-pre → 2.9.3 requires no test source changes (attribute/assert API stable across 2.x). | Pitfall 4 | If an API moved, a handful of test files fail to compile — caught immediately by `dotnet build`. Low risk (in-family minor bumps). |
| A3 | System.Reactive 6.0.2 is behaviorally identical to the pinned 6.0.1-preview.1 for the DpsHps operators in use. | Standard Stack | A subtle Rx operator change could alter stream timing — caught by the 7 DpsHps math tests + lifecycle tests. Low risk (patch within same minor). |

**Note:** No `[ASSUMED]` package *names* exist — every package was verified against the NuGet registry. The assumptions above concern build-environment and behavioral-equivalence, all gated by build/test.

## Open Questions

1. **Spectre.Console live-table style: `Clear()+Write` vs `AnsiConsole.Live`?**
   - What we know: Both render the 5 columns; `Live` is flicker-free and closer to the old fixed-`Region` behavior.
   - What's unclear: Whether the maintainer prefers exact-behavior parity (`Clear()+Write`) or the nicer `Live` panel.
   - Recommendation: Start with `Clear()+Write` for behavioral parity; upgrade to `Live` at discretion if the refresh flickers. Either satisfies DEP-03.

2. **System.Reactive 6.0.2 vs 6.1.0?**
   - What we know: Both GA, both net8.0.
   - What's unclear: Whether 6.1.0 has a behavioral change touching the DpsHps pipeline.
   - Recommendation: Use 6.0.2 (smallest delta from the pinned preview). Bump only if needed.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK | build / restore / test / publish | ✓ | 10.0.301 (also 10.0.108/204/300) | — |
| .NET 8 SDK | (preferred for net8.0) | ✗ | — | .NET 10 SDK builds net8.0 with runtime pack + on-demand targeting pack |
| .NET 8.0 runtime | net8.0 execution / targeting | ✓ | 8.0.28 | — |
| net8.0 host targeting pack | net8.0 build | ? (not seen on disk) | — | NuGet-acquired on first restore; else install .NET 8 SDK |
| Native AOT toolchain (ILCompiler) | `dotnet publish SwtorLogParser.Native.Cli` | acquired via NuGet | per-RID | requires C/C++ build tools on Windows (MSVC) — verify at gate |
| NuGet network access | restore of GA packages + ILCompiler | assumed ✓ | — | offline cache if previously restored |

**Missing dependencies with no fallback:** none confirmed blocking — but the **net8.0 targeting pack** and **AOT C++ build tools** must be verified at the publish gate (Pitfall 5). For Native AOT on Windows, the Visual Studio C++ workload (MSVC linker) is a documented prerequisite.

**Missing dependencies with fallback:** .NET 8 SDK absent → .NET 10 SDK builds net8.0 (A1).

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.x (current pin 2.5.0-pre.44 → GA 2.9.3) |
| Config file | none (convention-based; `IsTestProject=true` in `SwtorLogParser.Tests.csproj`) |
| Quick run command | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` |
| Full suite command | `dotnet test SwtorLogParser.slnx` (or the test csproj — it's the only test project) |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| DEP-01 | Solution restores + builds with all-GA versions | build | `dotnet build SwtorLogParser.slnx` | ✅ (build gate) |
| DEP-01 | Behavior preserved — 106 tests green | unit/integration | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` | ✅ (existing 16 test files) |
| DEP-02 | CPM resolves; no NU1008 | restore | `dotnet restore SwtorLogParser.slnx` | ✅ (restore gate) |
| DEP-03 | Managed CLI builds without System.CommandLine.Rendering | build | `dotnet build SwtorLogParser.Cli/SwtorLogParser.Cli.csproj` | ✅ |
| DEP-03 | `list`/`monitor` dispatch + Ctrl+C stop | manual smoke | `dotnet run --project SwtorLogParser.Cli -- list` ; `... -- monitor` + Ctrl+C | ❌ manual-only (no host integration test exists) |
| DEP-03 | Native CLI builds + AOT-publishes without System.CommandLine | build/publish | `dotnet publish SwtorLogParser.Native.Cli -c Release` | ✅ (publish gate) |
| INFRA-02 | Docker property gone | grep/build | `grep -r DockerDefaultTargetOS .` returns nothing | ✅ |

### Sampling Rate
- **Per task commit:** `dotnet build SwtorLogParser.slnx` (catches NU1008, missing refs, API breaks fast).
- **Per wave merge:** `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` (106 green).
- **Phase gate:** `dotnet restore` → `dotnet build SwtorLogParser.slnx` → `dotnet test` (106) → `dotnet publish SwtorLogParser.Native.Cli -c Release` (AOT) all succeed, plus manual `list`/`monitor`+Ctrl+C smoke for both CLI hosts.

### Wave 0 Gaps
- None — existing test infrastructure (16 files, ~106 [Fact]/[Theory] rows including `CombatLogsMonitorTests` lifecycle/cancellation + `DpsHpsMathTests`) covers behavior-preservation. No new automated tests are required for a dependency upgrade; the `list`/`monitor`/Ctrl+C path is host-integration and remains a documented manual smoke test (matches the existing project posture — hosts have no automated tests today).

## Security Domain

> `security_enforcement: true`, ASVS L1. This phase changes dependencies and CLI dispatch; no auth/session/crypto/network surface is introduced.

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | No auth surface (local desktop log reader). |
| V3 Session Management | no | No sessions. |
| V4 Access Control | no | No multi-user/access surface. |
| V5 Input Validation | yes (light) | `args[0]` dispatch validates against a fixed allow-list (`list`/`monitor`); unknown → usage + non-zero exit. No arg is interpolated into a shell/SQL/path. |
| V6 Cryptography | no | No crypto. |
| **Supply chain** | **yes** | **Primary security concern of this phase:** moving off preview/alpha/beta to GA reduces exposure to abandoned/unmaintained packages (System.CommandLine.Rendering alpha). All replacements verified 1st-party or high-reputation (see Package Legitimacy Audit). Consider adding `packages.lock.json` (`RestorePackagesWithLockFile`) — out of scope but worth a Phase 6 note. |

### Known Threat Patterns for this stack

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Dependency confusion / typosquat on a new package | Tampering | Spectre.Console verified (44.6M downloads, official repo); restore from nuget.org only. |
| Abandoned-package CVE exposure (alpha Rendering) | Tampering/DoS | Removed entirely; replaced with maintained GA Spectre.Console. |
| Unvalidated CLI arg | Tampering | Fixed allow-list switch; default branch rejects with exit 1. |

## Sources

### Primary (HIGH confidence)
- NuGet flat-container API (`api.nuget.org/v3-flatcontainer/<pkg>/index.json`) — exact stable version lists for System.Reactive, xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, coverlet.collector, Microsoft.Extensions.Logging.Abstractions, Spectre.Console, System.CommandLine. `[VERIFIED]`
- nuget.org/packages/Spectre.Console — latest 0.57.0, net8.0 support, publish 2026-06-11, 44.6M downloads, owners/repo. `[VERIFIED]`
- nuget.org/packages/System.CommandLine/2.0.9 — net8.0 support, publish 2026-06-09. `[VERIFIED]`
- Codebase: all 5 csproj, both Program.cs, CombatLogsMonitor.cs (Start/Stop/PlayerStats), test project — read directly. `[VERIFIED]`
- learn.microsoft.com/nuget/consume-packages/central-package-management — CPM structure, NU1008. `[CITED]`

### Secondary (MEDIUM confidence)
- GitHub dotnet/command-line-api #1537 + NuGet search — System.CommandLine 2.0.0 GA Nov 2025, beta history, breaking changes from beta4. `[CITED]`
- GitHub spectreconsole/spectre.console #1690/#1332 — base package AOT support; Spectre.Console.Cli NOT trim/AOT-safe. `[CITED]`

### Tertiary (LOW confidence)
- Local SDK/runtime probe (`dotnet --list-sdks`, runtime/pack dir listing) — informs A1/Pitfall 5; environment-specific, may differ on CI. `[VERIFIED locally, ASSUMED for other machines]`

## Metadata

**Confidence breakdown:**
- Standard stack (versions): HIGH — every version verified against live NuGet registry + net8.0 compatibility confirmed.
- Architecture (CPM + dispatch + cancellation): HIGH — CPM is first-party documented; dispatch/cancellation patterns map directly onto verified existing code (Start/Stop/PlayerStats read from source).
- Pitfalls: HIGH — NU1008, Spectre-in-AOT, and cancellation-loss are concrete and tied to exact file lines; net8.0-under-SDK10 is MEDIUM (environment-dependent).

**Research date:** 2026-06-12
**Valid until:** 2026-07-12 (30 days; NuGet versions drift — re-run the flat-container checks at execution time).

## RESEARCH COMPLETE

**Phase:** 05 - Dependency Upgrades
**Confidence:** HIGH

### Key Findings
1. All GA versions confirmed against the live NuGet registry and verified net8.0-compatible: System.Reactive 6.0.2, M.E.Logging.Abstractions 8.0.3, xunit 2.9.3, xunit.runner.visualstudio 3.1.5, Microsoft.NET.Test.Sdk 18.6.0, coverlet.collector 6.0.4, Spectre.Console 0.57.0.
2. System.CommandLine reached a TRUE stable GA (2.0.0, Nov 2025; 2.0.9 latest) — so the "no beta" rule alone no longer forces removal, but dropping it is still correct (alpha Rendering companion + breaking API changes from the pinned beta4 + trivial hand-roll). Spectre.Console BASE is AOT-safe; Spectre.Console.Cli is NOT — we use only base `Table` in the non-AOT host.
3. Critical landmine: removing System.CommandLine removes the Ctrl+C→token wiring (`context.GetCancellationToken()`); both hosts need a `Console.CancelKeyPress` → `CancellationTokenSource` bridge to keep `monitor` stoppable. Pattern provided for both hosts.
4. Dead-ref removal verified safe: core lib uses only `ILogger<T>`/`NullLogger<T>` (from Logging.Abstractions) — zero usages of DI container or Console/Debug providers.
5. NU1008 is the top CPM pitfall — strip `Version=` from all references in 4 csproj (Overlay has none); exact line numbers documented.

### File Created
`D:/Projects/pjmagee/swtor-logparser/.planning/phases/05-dependency-upgrades/05-RESEARCH.md`

### Confidence Assessment
| Area | Level | Reason |
|------|-------|--------|
| Standard Stack | HIGH | Every version verified against NuGet registry + net8.0 confirmed |
| Architecture | HIGH | CPM first-party; dispatch/cancellation map onto verified source code |
| Pitfalls | HIGH | Concrete, tied to exact file lines (net8.0-under-SDK10 is MEDIUM, env-dependent) |

### Open Questions
- Spectre `Clear()+Write` vs `AnsiConsole.Live` for the live table (recommend parity-first, discretion).
- System.Reactive 6.0.2 vs 6.1.0 (recommend 6.0.2, smallest delta).
- Build-env: .NET 8 SDK absent locally; .NET 10 SDK must build net8.0 + AOT — verify at gates (A1).

### Ready for Planning
Research complete. Planner can now create PLAN.md files.
