---
phase: 05-dependency-upgrades
plan: 01
subsystem: infra
tags: [nuget, central-package-management, directory.packages.props, ga-versions, dotnet8, cpm]

# Dependency graph
requires:
  - phase: 03-refactor
    provides: "WR-04/RFCT-02 moved DI + logging providers host-side, leaving the three core-lib provider/DI refs dead"
provides:
  - "Root Directory.Packages.props enabling central package management (ManagePackageVersionsCentrally=true)"
  - "All seven managed NuGet packages pinned to GA versions (no preview/alpha/beta)"
  - "Core lib with dead provider/DI refs removed and explicit Microsoft.Extensions.Logging.Abstractions added"
  - "All managed PackageReference entries converted to bare (version-less); System.CommandLine refs pinned via VersionOverride pending 05-02 removal"
affects: [05-02 (System.CommandLine removal + Spectre.Console wiring), 06-ci]

# Tech tracking
tech-stack:
  added: [Central Package Management (Directory.Packages.props), Spectre.Console 0.57.0 (declared centrally, not yet consumed)]
  patterns: [CPM bare PackageReference + central PackageVersion, VersionOverride escape hatch for refs slated for removal]

key-files:
  created: [Directory.Packages.props]
  modified:
    - SwtorLogParser/SwtorLogParser.csproj
    - SwtorLogParser.Native.Cli/SwtorLogParser.Native.Cli.csproj
    - SwtorLogParser.Cli/SwtorLogParser.Cli.csproj
    - SwtorLogParser.Tests/SwtorLogParser.Tests.csproj

key-decisions:
  - "Used VersionOverride (not a pinned Version=) to keep System.CommandLine + System.CommandLine.Rendering at their current versions under CPM without a central PackageVersion entry, avoiding both NU1008 and NU1010"
  - "System.Reactive pinned to 6.0.2 (smallest delta from the prior 6.0.1-preview.1) per research recommendation"
  - "Spectre.Console 0.57.0 declared in props now so 05-02 can reference it bare; no csproj consumes it in this plan"

patterns-established:
  - "Central Package Management: every csproj uses bare <PackageReference Include=.../>; versions live only in Directory.Packages.props <PackageVersion>"
  - "VersionOverride: packages with no central PackageVersion entry (slated for deletion) pin their own version on the reference without tripping NU1008/NU1010"

requirements-completed: [DEP-01, DEP-02]

# Metrics
duration: 3min
completed: 2026-06-12
---

# Phase 05 Plan 01: Central Package Management + GA Version Pinning Summary

**Introduced a root Directory.Packages.props (CPM) pinning all seven managed NuGet packages to GA versions, stripped every Version= attribute across the solution, removed three dead core-lib refs, and added explicit Logging.Abstractions — solution restores/builds and all 106 tests stay green.**

## Performance

- **Duration:** 3 min
- **Started:** 2026-06-12T00:04:56Z
- **Completed:** 2026-06-12T00:07:34Z
- **Tasks:** 3
- **Files modified:** 5 (1 created, 4 edited)

## Accomplishments
- Created `Directory.Packages.props` at the repo root with `ManagePackageVersionsCentrally=true` and seven GA `<PackageVersion>` entries — zero preview/alpha/beta.
- Converted the core lib to CPM: removed the three dead provider/DI refs (DependencyInjection, Logging.Console, Logging.Debug) and added an explicit `Microsoft.Extensions.Logging.Abstractions` ref (previously only transitive); `IsAotCompatible=true` retained.
- Stripped `Version=` from the Native CLI + Tests references (now resolve centrally); preserved the test child `<IncludeAssets>`/`<PrivateAssets>` metadata.
- Pinned `System.CommandLine` (Native CLI) and `System.CommandLine.Rendering` (managed CLI) via `VersionOverride` so CPM is satisfied without a central PackageVersion (those packages are deleted in 05-02).
- Gates green: `dotnet restore SwtorLogParser.slnx` (no NU1008/NU1010), `dotnet build SwtorLogParser.slnx`, and `dotnet test` → 106 passed, 0 skipped, 0 failed.

## Task Commits

Each task was committed atomically:

1. **Task 1: Create Directory.Packages.props with all GA versions** - `503e8e1` (feat)
2. **Task 2: Convert core lib + remove dead refs + add explicit Logging.Abstractions** - `b38b6e8` (feat)
3. **Task 3: Strip Version= from remaining csproj + verify restore/build/test** - `36451fc` (feat)

## Files Created/Modified
- `Directory.Packages.props` - NEW; central package version source (ManagePackageVersionsCentrally=true + 7 GA PackageVersion entries).
- `SwtorLogParser/SwtorLogParser.csproj` - Removed 3 dead provider/DI refs; added explicit Logging.Abstractions; System.Reactive made bare.
- `SwtorLogParser.Native.Cli/SwtorLogParser.Native.Cli.csproj` - Logging.Abstractions made bare; System.CommandLine pinned via VersionOverride.
- `SwtorLogParser.Cli/SwtorLogParser.Cli.csproj` - System.CommandLine.Rendering pinned via VersionOverride (no central PackageVersion).
- `SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` - All 4 test refs made bare; IncludeAssets/PrivateAssets children preserved on xunit.runner.visualstudio + coverlet.collector.

## Decisions Made
- **VersionOverride instead of leaving a bare `Version=`** on the two System.CommandLine refs (see Deviation 1 below). This is the CPM-correct way to keep a per-project pin for a package with no central PackageVersion entry.
- **System.Reactive 6.0.2** over 6.1.0 — smallest behavioral delta from the prior preview pin.
- **coverlet.collector 6.0.4** and **xunit 2.9.3** — conservative GA picks within the existing lines, per research.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] System.CommandLine refs pinned via `VersionOverride` instead of a bare `Version=`**
- **Found during:** Task 3 (restore gate)
- **Issue:** The plan's strategy was to leave `System.CommandLine` (Native CLI) and `System.CommandLine.Rendering` (managed CLI) with their existing `Version=` attributes intact, on the premise that "NU1008 only fires when BOTH a `<PackageVersion>` and a `Version=` exist." On this SDK (10.0.301), CPM raises **NU1008 for ANY `Version=` on a PackageReference**, regardless of whether a matching `<PackageVersion>` exists. The first `dotnet restore` failed with NU1008 on both refs. The plan's two forbidden alternatives (add a `<PackageVersion>` → NU1010 trap per 05-02; strip the Version entirely → NU1010, no central version) both remained invalid.
- **Fix:** Replaced `Version="..."` with `VersionOverride="..."` on both refs. `VersionOverride` is the documented CPM escape hatch that pins a per-project version for a package with no central `<PackageVersion>` entry — it satisfies CPM without tripping NU1008 or NU1010. The refs keep their exact current versions (`2.0.0-beta4.22272.1`, `0.4.0-alpha.22272.1`) and remain slated for deletion in 05-02. The net effect matches the plan's intent (these two refs are unchanged in substance; only the attribute name changed from `Version` to `VersionOverride`).
- **Files modified:** SwtorLogParser.Native.Cli/SwtorLogParser.Native.Cli.csproj, SwtorLogParser.Cli/SwtorLogParser.Cli.csproj
- **Verification:** `dotnet restore SwtorLogParser.slnx --force` succeeds with no NU1008/NU1010; full build green; 106 tests green.
- **Committed in:** 36451fc (Task 3 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** The deviation was required because the plan's NU1008-tolerance assumption did not hold on the installed SDK. `VersionOverride` achieves the plan's exact goal (keep the two soon-to-be-deleted refs pinned, no central version, restore green) with no scope creep. No other behavior changed.

## Issues Encountered
- **Overlay PackageReference false alarm:** `grep` surfaced a `Microsoft.Windows.SDK.Contracts` `PackageReference` with a `Version=` in `SwtorLogParser.Overlay.csproj:17`. Inspection confirmed it is inside an XML comment block (`<!-- ... -->`), so it is inert — no NU1008, nothing to change. Matches the plan's note that the Overlay has only a commented-out block.
- **Pre-existing warning (out of scope):** `dotnet build` emits one CS0108 warning in `SwtorLogParser.Overlay/ParserForm.cs:140` (`MouseDown` hides inherited member). Unrelated to package changes — not fixed (scope boundary).

## User Setup Required
None - no external service configuration required. (Versions resolve from nuget.org on restore.)

## Next Phase Readiness
- Foundation for 05-02 is in place: `Spectre.Console 0.57.0` is already declared centrally (bare reference works in 05-02), and both `System.CommandLine` refs are isolated via `VersionOverride` so 05-02 can delete them by simply removing the `<PackageReference>` lines (no props change needed for that removal).
- All managed versions are GA and centrally auditable; ROADMAP Phase 5 criteria 1 and 2 satisfied.
- No blockers. Native AOT publishability untouched (no Spectre.Console added to the AOT host; `PublishAot=true` retained).

## Self-Check: PASSED

- Directory.Packages.props — FOUND
- .planning/phases/05-dependency-upgrades/05-01-SUMMARY.md — FOUND
- Commit 503e8e1 (Task 1) — FOUND
- Commit b38b6e8 (Task 2) — FOUND
- Commit 36451fc (Task 3) — FOUND

---
*Phase: 05-dependency-upgrades*
*Completed: 2026-06-12*
