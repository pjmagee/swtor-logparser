# Phase 7: CI Pipeline - Context

**Gathered:** 2026-06-12 (revised for .NET 10 after Phase 6)
**Status:** Ready for planning

<domain>
## Phase Boundary

Add a GitHub Actions workflow so every push and pull request builds the full `.slnx` solution and runs the 106-test suite, failing the run on any test failure — a regression gate for all future changes. Green on `main` at delivery. Requirement: INFRA-01.

**Revised for .NET 10 (Phase 6):** the solution now targets `net10.0` / `net10.0-windows`, so CI installs a SINGLE .NET 10 SDK (10.0.x) — no dual 8+10 install, and `.slnx` is native to SDK 10. This simplifies the workflow the original (net8) research produced.

**In scope:** A `.github/workflows/*.yml` file. No source/behavior changes.

**Out of scope:** Releases/packaging, deployment, coverage gating/upload services. Next-milestone items (#2 MSTest, #3 CsWin32, #4 new UI, BL-01).

</domain>

<decisions>
## Implementation Decisions

### Runner & SDK
- Runner: **`windows-latest`** — the Overlay is `net10.0-windows` + WinForms (won't build on Linux); windows runners also carry the MSVC toolchain for the Native AOT link.
- SDK: `actions/setup-dotnet@v4` installing **.NET 10.0.x** ONLY (`.slnx` is native to SDK 9+/10, all projects target net10.0 — no .NET 8 SDK needed).
- Steps: `dotnet restore SwtorLogParser.slnx` → `dotnet build SwtorLogParser.slnx -c Release --no-restore` → `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj -c Release --no-build` (test failure → non-zero exit → red run = the gate).

### Scope & extras
- **Native AOT publish job** (windows-latest, MSVC present): `dotnet publish SwtorLogParser.Native.Cli -c Release` — exercises the full AOT link that could NOT be linked on the dev machine (closes the Phase 6 AOT human-verify item). `continue-on-error: true` so a transient AOT/link hiccup never blocks the required build+test gate.
- Triggers: `push` + `pull_request` targeting `main`.
- Coverage: collect via `coverlet.collector` (`--collect:"XPlat Code Coverage"`) and upload as a build artifact; do NOT gate or push to an external service.
- NuGet caching: `actions/cache` keyed on the csproj files + `Directory.Packages.props` (no `packages.lock.json` in the repo). `concurrency` with cancel-in-progress.

### Claude's Discretion
- Workflow filename (`ci.yml`), job split (build-test required + aot optional), exact cache key, and whether to add a status badge are at Claude's discretion — guided by a green run on `main` and the 3 success criteria.

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets
- No CI exists yet (`.github/workflows/` absent) — greenfield. The original net8 research (`07-RESEARCH.md`) provides a near-complete `ci.yml`; only the SDK install simplifies to a single 10.0.x.
- Solution `SwtorLogParser.slnx` (5 projects, all net10.0 / net10.0-windows after Phase 6). `Directory.Packages.props` centralizes GA versions.
- Test command verified on net10: `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` → 106 passed / 0 / 0.
- Suite is hermetic (Phase 3 `ICombatLogSource` seam) + deterministic (`[Collection]`) — no real SWTOR folder needed on a clean runner; no `TypeInitializationException`.

### Established Patterns
- `windows-latest` (Windows Server 2022 + VS 2022) ships the Native Desktop C++ workload (MSVC `link.exe`) and the WindowsDesktop targeting pack — no extra setup steps.
- The whole milestone's commits are currently LOCAL on `main` (origin `git@github.com:pjmagee/swtor-logparser.git`). "Green on main" requires a push so the workflow actually runs — a user-gated step.

### Integration Points
- Repo `pjmagee/swtor-logparser`, default branch `main`, `gh` authenticated locally.
- The workflow guards the 106-test regression contract on every future push/PR.

</code_context>

<specifics>
## Specific Ideas

- Single SDK 10.0.x is the key simplification vs the net8 plan — `.slnx` builds natively, no targeting-pack juggling.
- AOT job validates the one outstanding Phase 6 item (the MSVC link step) on a runner that has the toolchain; `continue-on-error` keeps it non-blocking for the INFRA-01 gate.
- "Green on main" (criterion 2) needs the local milestone commits PUSHED to GitHub so Actions runs — a user decision (push to main directly, or push a branch + PR). The workflow file is the deliverable; the green run is confirmed after push.

</specifics>

<deferred>
## Deferred Ideas

- Release/packaging workflow, NuGet publish, Codecov upload, README badge — future, not INFRA-01.
- Refreshing CLAUDE.md/STACK.md to reflect .NET 10 + dropped System.CommandLine — milestone docs-update.
- Next-milestone issues #2/#3/#4, BL-01.

</deferred>
