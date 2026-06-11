# Phase 5: Dependency Upgrades - Context

**Gathered:** 2026-06-12
**Status:** Ready for planning

<domain>
## Phase Boundary

Move every NuGet package to a stable GA release managed centrally via `Directory.Packages.props`; eliminate the abandoned `System.CommandLine.Rendering 0.4.0-alpha` AND `System.CommandLine` (which has no GA — beta only) from both CLI hosts; and remove the misleading `DockerDefaultTargetOS=Linux`. Requirements: DEP-01, DEP-02, DEP-03, INFRA-02.

**Critical invariant:** Behavior preserved — the parser, the live DpsHps stream, and all three hosts must work the same after the upgrades. `dotnet test` stays green (106 tests) and `dotnet build SwtorLogParser.slnx` succeeds; the core library stays `IsAotCompatible=true` and the Native AOT CLI still publishes AOT.

**In scope:** All `*.csproj`, a new root `Directory.Packages.props`, the two CLI hosts' command setup (`SwtorLogParser.Cli/Program.cs`, `SwtorLogParser.Native.Cli/Program.cs`), and the managed CLI's table rendering.

**Out of scope:** CI pipeline (Phase 6, DEP/INFRA-01). Behavior/feature changes. Jumping off .NET 8.

</domain>

<decisions>
## Implementation Decisions

### GA versions + central management + dead refs (DEP-01, DEP-02)
- Add a root `Directory.Packages.props` with `ManagePackageVersionsCentrally=true`; move ALL package versions there; remove per-csproj `Version=` attributes (use bare `<PackageReference Include=.../>` / `<PackageVersion>` in the props).
- Upgrade every package to its latest STABLE GA release; STAY on .NET 8 (`net8.0` / `net8.0-windows`). Targets: `System.Reactive` (6.0.x GA, drop the `-preview.1`), `xunit` + `xunit.runner.visualstudio` (latest GA 2.x), `Microsoft.NET.Test.Sdk` (latest GA, drop `-preview`), `coverlet.collector` (GA), `Microsoft.Extensions.Logging.*` (8.0.x GA, drop `-preview.5`). Research confirms exact current GA versions.
- Remove the unused core-lib package refs (Phase 3 WR-04): `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging.Console`, `Microsoft.Extensions.Logging.Debug` — logging providers moved host-side in Phase 3; the core lib only needs `Microsoft.Extensions.Logging.Abstractions`.

### System.CommandLine + Rendering replacement (DEP-03)
- **Drop `System.CommandLine` (beta) AND `System.CommandLine.Rendering` (alpha) entirely** from BOTH CLI hosts — neither has a GA, so keeping them violates the "no preview/alpha/beta" criterion.
- The CLI surface is trivial (`list`, `monitor`). Replace command parsing with a small HAND-ROLLED arg dispatch (switch on `args[0]`) — zero dependency, AOT-safe, works for both hosts.
- The **managed CLI** (`SwtorLogParser.Cli`) replaces its `System.CommandLine.Rendering` `TableView` with **Spectre.Console** (GA library) for the live table.
- The **Native AOT CLI** (`SwtorLogParser.Native.Cli`) keeps its PERF-02 in-place console renderer (SetCursorPosition + pad) — do NOT add Spectre.Console to the AOT host unless research confirms Spectre.Console is fully AOT/trim-safe there. Confine Spectre.Console to the non-AOT managed CLI by default.
- Preserve each host's behavior: same commands, same displayed columns (Player, DPS, crit%, HPS, crit%), same cancellation (Ctrl+C → Stop()).

### Docker target (INFRA-02)
- Remove `DockerDefaultTargetOS=Linux` from all csproj that have it (the CLI projects). No Dockerfile exists; this is a Windows-only app. Remove any other misleading cross-platform properties.

### Claude's Discretion
- Exact GA version numbers (research-confirmed), the precise hand-rolled dispatch shape, the Spectre.Console table styling (match current columns), and whether Spectre.Console can also serve the Native CLI (only if proven AOT-safe) are at Claude's discretion — guided by green tests, a successful `dotnet build SwtorLogParser.slnx`, and a still-AOT-publishable Native CLI.

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets
- Current pinned versions (from STACK.md): `Microsoft.Extensions.* 8.0.0-preview.5.23280.8`, `System.Reactive 6.0.1-preview.1`, `System.CommandLine 2.0.0-beta4.22272.1`, `System.CommandLine.Rendering 0.4.0-alpha.22272.1`, `xunit 2.5.0-pre.44`, `xunit.runner.visualstudio 2.5.0-pre.27`, `Microsoft.NET.Test.Sdk 17.7.0-preview.23280.1`, `coverlet.collector 6.0.0`.
- Both CLI Program.cs files use the same `RootCommand` + `list`/`monitor` `Command` + `SetHandler` pattern (Native.Cli/Program.cs and Cli/Program.cs) — replace identically.
- The managed CLI's `Update` builds a `System.CommandLine.Rendering.Views.TableView<PlayerStats>` with 5 columns — port these columns to a Spectre.Console `Table`.
- Native CLI's renderer was rewritten in PERF-02 (no System.CommandLine.Rendering dependency there — it only uses System.CommandLine for command parsing).

### Established Patterns
- Core lib `IsAotCompatible=true`; Native CLI `PublishAot=true`. Spectre.Console.Cli's reflection-based command model is the AOT risk — that's WHY we hand-roll dispatch instead.
- `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` (106 tests) + `dotnet build SwtorLogParser.slnx`. Also verify `dotnet publish SwtorLogParser.Native.Cli -c Release` AOT still works.
- Solution is now `.slnx` (migrated earlier).

### Integration Points
- Both hosts subscribe to `CombatLogsMonitor.Instance.DpsHps` and call `Start(token)`/`Stop()` — the command-framework swap must keep the Ctrl+C → cancellation token → Stop() wiring intact.
- `SlidingExpirationList` (core lib, from Phase 3) feeds both renderers — unchanged.

</code_context>

<specifics>
## Specific Ideas

- System.CommandLine has historically had no GA (long-running beta) — research must CONFIRM the current (2026) state; if a true stable GA shipped, keeping it is an option, but the recommended path is to drop it regardless since the rendering add-on must go and hand-rolled dispatch is trivial + AOT-safe.
- Spectre.Console AOT: recent Spectre.Console versions document NativeAOT support, but `Spectre.Console.Cli` (the command framework) is the reflection-heavy part — we are NOT using the Cli framework, only the `Table`/rendering of the base `Spectre.Console` package, and only in the non-AOT managed CLI. Research should confirm the base package version + AOT note.
- Central package management: a `<PackageReference>` with a `Version=` attribute alongside `ManagePackageVersionsCentrally=true` causes NU1008 — ensure all version attributes move to `Directory.Packages.props`.

</specifics>

<deferred>
## Deferred Ideas

- CI pipeline (INFRA-01) → Phase 6 (it depends on this phase producing a green GA build).
- BoundedCache library swap (BitFaster) — considered in Phase 3 research, NOT required; the in-repo BoundedCache stays.
- Overlay-topmost (BL-01), DateTime.Now filter (IN-01) — unrelated.

</deferred>
