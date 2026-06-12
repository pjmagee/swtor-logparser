---
phase: 06-dotnet-10-upgrade
plan: 01
subsystem: infra
tags: [dotnet10, net10, tfm-upgrade, central-package-management, native-aot, winforms, xunit, lts]

# Dependency graph
requires:
  - phase: 05-dependency-hardening
    provides: Central Package Management (Directory.Packages.props) with all packages on GA versions; System.CommandLine removed from both CLI hosts
provides:
  - All 5 projects target net10.0 / net10.0-windows (LTS) — no net8.0 TargetFramework remains
  - Microsoft.Extensions.Logging.Abstractions pinned to 10.0.9 GA
  - LangVersion=preview removed from the managed CLI (C# 14 is the net10 default)
  - Verified green build/test chain on .NET 10; Native AOT code-gen confirmed IL-warning-clean
affects: [07-ci-pipeline, dotnet-10, native-aot]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Framework-tied package (Logging.Abstractions) aligned to the runtime major (10.0.x) to avoid NU1605 transitive downgrade; framework-agnostic GA packages kept as-is"
    - "AOT gate split: code-gen IL2xxx/IL3xxx cleanliness is the in-scope local gate; the MSVC native link is deferred to Phase 7 CI"

key-files:
  created: []
  modified:
    - Directory.Packages.props
    - SwtorLogParser/SwtorLogParser.csproj
    - SwtorLogParser.Cli/SwtorLogParser.Cli.csproj
    - SwtorLogParser.Native.Cli/SwtorLogParser.Native.Cli.csproj
    - SwtorLogParser.Overlay/SwtorLogParser.Overlay.csproj
    - SwtorLogParser.Tests/SwtorLogParser.Tests.csproj

key-decisions:
  - "Bumped only Microsoft.Extensions.Logging.Abstractions (8.0.3 -> 10.0.9); kept System.Reactive 6.0.2, xunit 2.9.3, xunit.runner.visualstudio 3.1.5, Microsoft.NET.Test.Sdk 18.6.0, coverlet.collector 6.0.4, Spectre.Console 0.57.0 unchanged (research VERIFIED all net10-compatible GA; no churn warranted)"
  - "Dropped LangVersion=preview from the managed CLI — net10 defaults to C# 14 (superset of preview), removing a moving target"
  - "Native AOT native link (link.exe/vswhere) is env-gated locally; the in-scope gate is zero IL2xxx/IL3xxx code-gen warnings — which passed. Full link deferred to Phase 7 CI on windows-latest"
  - "Did NOT migrate xunit 2.x -> v3 (explicitly out of scope); did NOT add a global.json"

patterns-established:
  - "Pattern: net10 LTS TFM baseline — net10.0 for core/CLI/Native CLI/Tests, net10.0-windows for the WinForms overlay"

requirements-completed: [PLAT-01]

# Metrics
duration: 6min
completed: 2026-06-12
---

# Phase 6 Plan 01: .NET 10 Upgrade Summary

**Mechanical net8.0 -> net10.0 LTS upgrade across all 5 projects with Logging.Abstractions bumped to 10.0.9 GA; restore + Release build green, 106/106 tests pass with zero skips, and Native AOT code-gen is IL2xxx/IL3xxx-clean — behavior preserved, no code changes.**

## Performance

- **Duration:** ~6 min
- **Started:** 2026-06-12T00:55:00Z
- **Completed:** 2026-06-12T01:01:00Z
- **Tasks:** 3 (1 edit task + 2 verification gates)
- **Files modified:** 6

## Accomplishments
- All 5 `.csproj` now target `net10.0` (core, managed CLI, Native CLI, Tests) / `net10.0-windows` (Overlay) — zero `net8.0` substrings remain in any csproj
- `Microsoft.Extensions.Logging.Abstractions` bumped 8.0.3 -> 10.0.9 GA; all six other package versions byte-for-byte unchanged; no preview/alpha/beta anywhere
- `LangVersion=preview` removed from the managed CLI (C# 14 default on net10)
- AOT/WinForms invariants retained: core `IsAotCompatible=true`, Native CLI `PublishAot=true`, Overlay `UseWindowsForms=true`
- All four phase gates exercised and green/accounted-for; GitHub issue #1 (Upgrade to .NET 10 LTS) closed referencing commit `c00311c`

## Gate Results (PLAT-01)

| Gate | Command | Result |
|------|---------|--------|
| Grep assertions | net8 / 10.0.9 / no-preview check | PASS — no net8.0 in any csproj; Logging.Abstractions at 10.0.9; no preview/alpha/beta |
| Restore | `dotnet restore SwtorLogParser.slnx --force` | PASS — all 5 projects restored; no NU1605 / NU1008; no NU1901-1904 transitive advisories printed |
| Build | `dotnet build SwtorLogParser.slnx -c Release` | PASS — all 5 projects compiled on net10.0 / net10.0-windows (0 errors; 5 pre-existing Overlay CS warnings, out of scope) |
| Test | `dotnet test SwtorLogParser.Tests/... -c Release` | PASS — 106 passed, 0 failed, 0 skipped (108 ms) on the net10.0 test host |
| Native AOT | `dotnet publish SwtorLogParser.Native.Cli -c Release` | CODE-GEN PASS — "Generating native code" reached; ZERO IL2xxx/IL3xxx warnings. Native link step env-gated (see below) |

### NU1901-1904 transitive advisories
None printed by `dotnet restore` (net10 transitive NuGetAudit ran clean). Nothing to review for T-06-01.

### Native AOT link step (Pitfall 4 / Open Question 2 — KNOWN ENV GAP, not a failure)
The AOT publish completed the in-scope managed -> IL -> code-gen stage cleanly (zero `IL2xxx`/`IL3xxx` warnings — confirmed via grep, `GREP_EXIT=1`). It then failed at the **final native link step** (`MSB3073`, exit code 123): `vswhere.exe` not found and the MSVC `link.exe` invocation could not resolve. This is the documented local environment gap — the Visual C++ build tools / `link.exe` are not invocable on this dev box. Per 06-CONTEXT.md and 06-RESEARCH.md Pitfall 4, the in-scope gate is AOT-cleanliness (passed); Phase 7 CI on `windows-latest` exercises the full native link.

## Task Commits

1. **Task 1: Bump 5 TFMs + Logging.Abstractions 10.0.9 + drop LangVersion=preview** - `c00311c` (chore)
2. **Task 2: Re-verify 106-test regression suite on net10.0** - verification only (no file edits, no commit) — 106/106 green, 0 skips
3. **Task 3: Re-verify Native AOT code-gen IL-warning-clean** - verification only (no file edits, no commit) — zero IL2xxx/IL3xxx; native link env-gated

**Plan metadata:** committed separately (docs: complete plan — SUMMARY.md + STATE.md + ROADMAP.md + REQUIREMENTS.md)

## Files Created/Modified
- `Directory.Packages.props` - Microsoft.Extensions.Logging.Abstractions 8.0.3 -> 10.0.9; six other PackageVersion lines unchanged
- `SwtorLogParser/SwtorLogParser.csproj` - net8.0 -> net10.0; IsAotCompatible=true retained
- `SwtorLogParser.Cli/SwtorLogParser.Cli.csproj` - net8.0 -> net10.0; LangVersion=preview line removed; PublishAot=false retained
- `SwtorLogParser.Native.Cli/SwtorLogParser.Native.Cli.csproj` - net8.0 -> net10.0; PublishAot=true retained
- `SwtorLogParser.Overlay/SwtorLogParser.Overlay.csproj` - net8.0-windows -> net10.0-windows; UseWindowsForms=true retained
- `SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` - net8.0 -> net10.0

## Decisions Made
- Bumped only the framework-tied package (Logging.Abstractions -> 10.0.9 GA); kept all framework-agnostic GA packages (System.Reactive 6.0.2, xunit 2.9.3, xunit.runner.visualstudio 3.1.5, Microsoft.NET.Test.Sdk 18.6.0, coverlet.collector 6.0.4, Spectre.Console 0.57.0) to minimize churn per research
- Dropped LangVersion=preview from the managed CLI (Claude's discretion, recommended by research — C# 14 default)
- Treated the native-link failure as a known env gap, not a gate failure, per the AOT code-gen vs. native-link distinction in 06-RESEARCH.md Pitfall 4

## Deviations from Plan

None - plan executed exactly as written. All six edits and four gates ran as specified; the native-link env gap was anticipated by the plan and recorded as acceptable.

## Issues Encountered
- **Native AOT link step failed locally (anticipated):** `vswhere.exe`/MSVC `link.exe` not invocable (MSB3073, exit 123). Resolved per plan: AOT-cleanliness gate (zero IL2xxx/IL3xxx) is the in-scope criterion and passed; full native link deferred to Phase 7 CI on windows-latest. Not a failure.
- **CLAUDE.md "stay on .NET 8" constraint:** The generated project CLAUDE.md (stack/constraints sections, sourced from pre-upgrade project docs) still says ".NET 8 ... stay on .NET 8". This is precisely the planned, approved scope of Phase 6 (PLAT-01, closes issue #1), which supersedes that now-stale text. Proceeded with the upgrade; CLAUDE.md stack docs will be refreshed by normal GSD doc-sync as the project advances.

## Out-of-Scope Observations (not fixed — pre-existing)
- `SwtorLogParser.Overlay/ParserForm.cs` emits 5 compiler warnings (1x CS0108 method-hiding at line 140; 4x CS8602 possible-null-deref at lines 120/121/126/127). These exist in the WinForms overlay source and are **not caused by the TFM change** (no code was touched). Logged here as informational; not in scope for a mechanical framework upgrade.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Codebase is fully on .NET 10 LTS; ready for Phase 7 (CI pipeline) to stand up a single-SDK (.NET 10) build/test/publish pipeline on windows-latest, which will also exercise the full Native AOT native link that is env-gated locally.
- Issue #1 (Upgrade to .NET 10 LTS) closed.
- No blockers.

## Self-Check: PASSED

- FOUND: `.planning/phases/06-dotnet-10-upgrade/06-01-SUMMARY.md`
- FOUND commit: `c00311c` (Task 1 TFM upgrade)

---
*Phase: 06-dotnet-10-upgrade*
*Completed: 2026-06-12*
