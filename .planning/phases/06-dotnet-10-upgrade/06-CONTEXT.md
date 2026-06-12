# Phase 6: .NET 10 Upgrade - Context

**Gathered:** 2026-06-12
**Status:** Ready for planning

<domain>
## Phase Boundary

Move every project from .NET 8 to **.NET 10 (LTS)** — `net8.0` → `net10.0`, `net8.0-windows` → `net10.0-windows` — update framework-tied packages to their .NET 10 GA versions, and re-verify the full solution builds, all 106 tests pass, and the Native AOT host still compiles AOT-clean. Requirement: PLAT-01 (closes issue #1). This phase runs BEFORE Phase 7 (CI) so the pipeline targets .NET 10 with a single SDK.

**Why now:** User directive ("on .NET 10 ASAP"). The dev machine already runs SDK 10.x + WindowsDesktop 10.x runtime; .NET 10 is LTS GA; and it simplifies the upcoming CI (single SDK, native `.slnx`).

**In scope:** All `*.csproj` `TargetFramework`(s); `Directory.Packages.props` framework-tied package versions; re-verification (build/test/AOT).

**Out of scope:** Any behavior/feature change; rewriting code to use new .NET 10 APIs; the CI pipeline (Phase 7); the deferred next-milestone items (#2 MSTest, #3 CsWin32, #4 new UI, BL-01).

</domain>

<decisions>
## Implementation Decisions

### Target frameworks
- `SwtorLogParser` (core), `SwtorLogParser.Cli`, `SwtorLogParser.Native.Cli`, `SwtorLogParser.Tests`: `net8.0` → `net10.0`.
- `SwtorLogParser.Overlay`: `net8.0-windows` → `net10.0-windows` (WinForms `UseWindowsForms` retained).
- Keep `IsAotCompatible=true` (core) and `PublishAot=true` (Native CLI) — re-verify they still hold on net10.0.

### Packages (Directory.Packages.props)
- Bump framework-tied packages to their **.NET 10 GA** versions — primarily `Microsoft.Extensions.Logging.Abstractions` 8.0.3 → 10.0.x (GA). Research confirms the exact current GA.
- Packages that are framework-agnostic and already GA stay unless a newer GA is warranted: `System.Reactive` 6.0.2 (works on net10), `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.5, `Microsoft.NET.Test.Sdk` 18.6.0, `coverlet.collector` 6.0.4, `Spectre.Console` 0.57.0. Research checks each for a .NET-10-recommended bump (e.g. Test SDK / runner versions that explicitly support net10.0), but no preview/alpha/beta.
- No NU1605 downgrades; restore must resolve cleanly under central package management.

### Verification (the gates)
- `dotnet restore SwtorLogParser.slnx` → `dotnet build SwtorLogParser.slnx -c Release` → `dotnet test SwtorLogParser.Tests/...` (106 green, zero skips) — all on net10.0.
- `dotnet publish SwtorLogParser.Native.Cli -c Release` (PublishAot) — the managed/ILCompiler code-gen must be AOT-clean (zero IL2xxx/IL3xxx); the MSVC link step may be unavailable locally (known env gap — Phase 7 CI on windows-latest will exercise the full link).
- LangVersion: the managed CLI used `<LangVersion>preview</LangVersion>` — on net10.0 this can move to the default latest (C# 14) or stay; not required, Claude's discretion.

### Claude's Discretion
- Whether to also bump test tooling (Test SDK / xunit) to a newer GA that explicitly targets net10.0, the exact `Microsoft.Extensions.Logging.Abstractions` 10.0.x patch, and whether to drop `LangVersion=preview` are at Claude's discretion — guided by green tests, AOT-clean compile, and no preview/alpha/beta packages.

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets
- 5 csproj currently target net8.0 / net8.0-windows. `Directory.Packages.props` (Phase 5) centralizes versions — bump there + the TFMs in each csproj.
- Dev machine SDKs: 10.0.108/204/300/301 (no SDK 8); runtimes include Microsoft.NETCore.App 8.0.28 + 10.0.9 and WindowsDesktop.App 8.0.28 + 10.0.x — so net10.0 + net10.0-windows build/run natively.
- Suite is hermetic (Phase 3 `ICombatLogSource` seam) and deterministic (`[Collection]`) — re-running on net10.0 needs no fixture changes.

### Established Patterns
- Solution is `.slnx` (native to SDK 9+/10). `dotnet build SwtorLogParser.slnx` already works on the SDK-10 dev machine.
- Build/test commands unchanged; only the TFM/packages change.

### Integration Points
- AOT: core `IsAotCompatible`, Native CLI `PublishAot` — the net8→net10 bump must keep both; Spectre.Console stays confined to the managed CLI.
- After this phase, Phase 7 CI installs a single .NET 10 SDK (simpler than the dual 8+10 the CI research assumed under net8.0 — the CI CONTEXT/RESEARCH will be revised for net10 in Phase 7).

</code_context>

<specifics>
## Specific Ideas

- This is a mechanical, well-bounded upgrade: TFM strings + a few framework-package version bumps + re-verify. The risk is a package that has no net10-compatible GA or an AOT regression — research must confirm `Microsoft.Extensions.*` 10.0.x GA and that `System.Reactive` 6.0.2 / the test stack work on net10.0.
- `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio`: confirm the installed versions support running on the net10.0 test host; bump if a net10-targeting GA is recommended.
- Close issue #1 (referencing the upgrade commit) at phase completion.

</specifics>

<deferred>
## Deferred Ideas

- CI pipeline targeting .NET 10 → Phase 7 (revise the moved 07-CONTEXT/07-RESEARCH for single-SDK net10).
- Next-milestone issues #2 (MSTest), #3 (CsWin32), #4 (new UI), BL-01 (overlay topmost).
- Adopting new .NET 10 language/runtime APIs in code — not part of a pure TFM upgrade.

</deferred>
