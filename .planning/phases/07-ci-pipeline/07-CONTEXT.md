# Phase 6: CI Pipeline - Context

**Gathered:** 2026-06-12
**Status:** Ready for planning

<domain>
## Phase Boundary

Add a GitHub Actions workflow so every push and pull request automatically builds the full solution and runs the tests, with a test failure failing the run — a regression gate for all future changes. The build must be green on `main` at delivery. Requirement: INFRA-01.

**In scope:** A `.github/workflows/*.yml` file. No source/behavior changes.

**Out of scope:** Releases/packaging, deployment, code-coverage gating/upload services, badge wiring (optional only). Everything from Phases 1-5 is done.

</domain>

<decisions>
## Implementation Decisions

### Runner & SDK
- Runner: **`windows-latest`**. The Overlay targets `net8.0-windows` + WinForms (`UseWindowsForms`) and won't build on Linux; the full `.slnx` solution must build. Windows runners also have the MSVC toolchain needed for the Native AOT publish.
- SDK: use `actions/setup-dotnet@v4` to install **.NET 8.0.x** (targeting packs / runtime for `net8.0` + `net8.0-windows`) AND a **.slnx-capable SDK (10.0.x)** — the solution was migrated to `.slnx`, which the .NET 8 SDK cannot parse (needs SDK 9+/10). Install both (multi-line `dotnet-version`).
- Steps: `dotnet restore SwtorLogParser.slnx` → `dotnet build SwtorLogParser.slnx -c Release --no-restore` → `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj -c Release --no-build` (a test failure → non-zero exit → red run = the regression gate).

### Scope & extras
- Add a **Native AOT publish job** (windows-latest, MSVC present): `dotnet publish SwtorLogParser.Native.Cli -c Release` — validates the AOT link step that could not be linked on the dev machine (closes the Phase 5 human-verify item). This job may be a separate job in the same workflow (can run in parallel with build+test or depend on it).
- Triggers: `push` and `pull_request` (the criterion requires both). Target the default branch (`main`).
- Coverage: collect via the existing `coverlet.collector` (`--collect:"XPlat Code Coverage"`) but do NOT gate or upload to an external service — keep it simple (optionally upload as a build artifact).

### Claude's Discretion
- Workflow file name (`ci.yml` / `build.yml` / `dotnet.yml`), exact job layout (one job vs build+test and AOT split), whether AOT is a separate job or appended, NuGet caching (`actions/setup-dotnet` cache or `actions/cache`), and concurrency/cancel-in-progress settings are at Claude's discretion — guided by a green run on current `main` and the 3 success criteria.

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets
- No CI exists yet (`.github/workflows/` absent) — this is greenfield.
- Solution: `SwtorLogParser.slnx` (migrated from `.sln` earlier this milestone). 5 projects: core lib (`net8.0`, AOT-compatible), `SwtorLogParser.Cli` (`net8.0`), `SwtorLogParser.Native.Cli` (`net8.0`, `PublishAot=true`), `SwtorLogParser.Overlay` (`net8.0-windows`, WinForms), `SwtorLogParser.Tests` (`net8.0`, xUnit).
- `Directory.Packages.props` (Phase 5) centralizes all GA package versions — `dotnet restore` resolves from it.
- Test command verified locally: `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` → 106 passed / 0 / 0.

### Established Patterns
- Local dev machine runs SDK 10.x only (no SDK 8) — building `net8.0` works via SDK 10 with the .NET 8 targeting packs. CI must replicate: install both 8.0.x and 10.0.x.
- A handful of tests touch the static `CombatLogs` source; Phase 3 serialized them into an xUnit `[Collection]` so the suite is deterministic in CI (no parallel races).
- `Encoding.Latin1` log reading + the hermetic `ICombatLogSource` seam mean tests pass on CI with NO real SWTOR folder present (Phase 3) — important: the suite must not require the game's log directory.

### Integration Points
- Repo: `pjmagee/swtor-logparser` (GitHub, default branch `main`). `gh` is authenticated locally.
- The workflow validates the whole milestone's regression contract (106 tests) on every push/PR going forward.

</code_context>

<specifics>
## Specific Ideas

- `.slnx` + CI: confirm `actions/setup-dotnet@v4` + an SDK that parses `.slnx` (10.0.x). If `.slnx` proves troublesome in CI, the fallback is to build the test project + its references directly (`dotnet test SwtorLogParser.Tests/...` pulls in the core lib), but prefer building the full `.slnx` to also compile the Overlay/CLIs.
- The hermetic-test work (Phase 3) is what makes this CI viable — `All_Logs_Are_Not_Null`/`Player_Is_Local_Is_True` no longer need a real SWTOR install or throw `TypeInitializationException` on a clean runner.
- AOT job: `dotnet publish SwtorLogParser.Native.Cli -c Release` on `windows-latest` should find `link.exe` (VS C++ build tools are pre-installed on GitHub windows runners). If it still fails, mark that job `continue-on-error` so it doesn't block the required build+test gate (criterion 2/3 is about build+test green).

</specifics>

<deferred>
## Deferred Ideas

- Release/packaging workflow, NuGet publish, badges, coverage upload (Codecov) — future, not INFRA-01.
- Updating CLAUDE.md/STACK.md to drop the now-removed System.CommandLine from the documented stack (doc staleness flagged in Phase 5 verification) — handle at milestone docs-update, not here.
- Backlog BL-01 (overlay topmost), issues #1-4 (next-milestone).

</deferred>
