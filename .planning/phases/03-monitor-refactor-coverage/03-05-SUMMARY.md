---
phase: 03-monitor-refactor-coverage
plan: 05
subsystem: testing
tags: [filesystem-seam, hermetic-tests, aot, dependency-injection-by-hand, xunit, ci]

# Dependency graph
requires:
  - phase: 03-monitor-refactor-coverage (Plan 02)
    provides: BoundedCache fields in CombatLogs.cs (preserved by this plan)
provides:
  - ICombatLogSource injectable filesystem seam (Directory.Exists-guarded default impl)
  - Static CombatLogs facade preserved for hosts; internal SetSource/Source/ResetSource test seam
  - Hermetic, CI-safe All_Logs_Are_Not_Null and Player_Is_Local_Is_True (no real SWTOR folders)
affects: [phase-06-ci, monitor, testing]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Filesystem seam via plain interface + hand-injected default (no DI container, AOT-safe)"
    - "Directory.Exists-guarded reads + lazy resolution to avoid TypeInitializationException at type-load"
    - "Per-test source injection with try/finally ResetSource to eliminate ambient-state leakage"

key-files:
  created:
    - SwtorLogParser/Monitor/ICombatLogSource.cs
    - SwtorLogParser.Tests/Fixtures/InMemoryCombatLogSource.cs
    - SwtorLogParser.Tests/CombatLogSourceTests.cs
  modified:
    - SwtorLogParser/Monitor/CombatLogs.cs
    - SwtorLogParser.Tests/CombatLogLineTests.cs
    - SwtorLogParser.Tests/ActorTests.cs

key-decisions:
  - "Seam interface exposes DirectoryInfo CombatLogsDirectory so CombatLogsMonitor.MonitorAsync (Refresh()/LastWriteTime) compiles unchanged"
  - "PlayerNames moved out of the static ctor into a lazy, Directory.Exists-guarded property on the default source — type-load no longer touches the filesystem"
  - "PlayerNames return type widened from HashSet<string> to ISet<string> (only consumer is Actor.IsLocalPlayer via .Contains — unaffected)"
  - "DefaultCombatLogSource is a private nested class inside CombatLogs (keeps the seam internal, no new public surface beyond the interface)"

patterns-established:
  - "Hand-injected filesystem seam: interface + private default impl + internal swap hooks behind an unchanged static facade"
  - "Hermetic test pattern: each test installs its own InMemoryCombatLogSource and restores the default in finally"

requirements-completed: [TEST-01, TEST-02]

# Metrics
duration: 6min
completed: 2026-06-11
---

# Phase 3 Plan 05: Filesystem Hermeticity Seam Summary

**Injectable `ICombatLogSource` seam (Directory.Exists-guarded) behind the static `CombatLogs` facade, making the two deferred Phase 1 tests `All_Logs_Are_Not_Null` and `Player_Is_Local_Is_True` hermetic and CI-safe — no real SWTOR folders, no TypeInitializationException.**

## Performance

- **Duration:** ~6 min
- **Started:** 2026-06-11T22:19Z
- **Completed:** 2026-06-11T22:26Z
- **Tasks:** 2 (TDD: RED + GREEN per task)
- **Files modified:** 6 (3 created, 3 modified)

## Accomplishments
- `ICombatLogSource` seam abstracts combat-log enumeration + `PlayerNames` + the logs `DirectoryInfo`.
- `DefaultCombatLogSource` wraps the real `%Documents%`/`%LocalAppData%` SWTOR paths, guarding every read with `Directory.Exists`; `PlayerNames` is now lazy + guarded (moved out of the static ctor), so touching any `CombatLogs` member no longer throws `TypeInitializationException` when the SWTOR folders are absent.
- Static `CombatLogs` facade preserved for the 3 hosts (`PlayerNames`, `EnumerateCombatLogs`, `GetLatestCombatLog`, `CombatLogsDirectory` all route through the current source); `CombatLogsMonitor.MonitorAsync` and `Actor.IsLocalPlayer` compile unchanged.
- `internal SetSource`/`Source` + `ResetSource` test seam (via existing `InternalsVisibleTo`) lets tests inject fixtures without cross-test leakage.
- Both previously non-hermetic tests rewritten to install their own `InMemoryCombatLogSource` and restore the default in `finally`. Full suite green 3x consecutively, 0 skips, with NO real SWTOR folders required.
- Core library stays `IsAotCompatible=true` — no reflection, no DI container, no new package.

## Task Commits

Each task committed atomically (TDD):

1. **Task 1 (RED): seam tests** - `d89f13b` (test)
2. **Task 1 (GREEN): ICombatLogSource seam** - `4cd3a36` (feat)
3. **Task 2 (RED+GREEN): hermetic All_Logs_Are_Not_Null + Player_Is_Local_Is_True** - `1c531a4` (test)

_Task 2's RED state was a compile/assert failure surfaced during the full-suite run; the same commit lands the GREEN hermetic rewrite (test-only files)._

## Files Created/Modified
- `SwtorLogParser/Monitor/ICombatLogSource.cs` (created) - Injectable filesystem seam interface: `CombatLogsDirectory`, `PlayerNames`, `EnumerateCombatLogs()`, `GetLatestCombatLog()`.
- `SwtorLogParser/Monitor/CombatLogs.cs` (modified) - Routes the static facade through a swappable `ICombatLogSource`; `DefaultCombatLogSource` (private nested) with `Directory.Exists` guards + lazy `PlayerNames`; `SetSource`/`Source`/`ResetSource`; Plan 02 `BoundedCache` fields and `ReadOnlyMemory` lookup tables preserved.
- `SwtorLogParser.Tests/Fixtures/InMemoryCombatLogSource.cs` (created) - Hermetic temp-dir-backed source with injectable `PlayerNames` and `AddLogFile`; `IDisposable` cleanup.
- `SwtorLogParser.Tests/CombatLogSourceTests.cs` (created) - 4 seam tests (no-throw-when-absent, default-wraps-real-paths, injectable, reset-restores-default).
- `SwtorLogParser.Tests/CombatLogLineTests.cs` (modified) - `All_Logs_Are_Not_Null` now iterates an injected fixture of golden lines.
- `SwtorLogParser.Tests/ActorTests.cs` (modified) - `Player_Is_Local_Is_True` now uses an injected `PlayerNames` set.

## Decisions Made
- Exposed `DirectoryInfo CombatLogsDirectory` on the interface so `MonitorAsync`'s `Refresh()`/`LastWriteTime` usage stays byte-identical (no monitor changes needed).
- Moved the `PlayerNames` enumeration from the static ctor into a lazy, `Directory.Exists`-guarded property — this is the direct mitigation for T-03-06 (TypeInitializationException DoS).
- Widened `PlayerNames` from `HashSet<string>` to `ISet<string>`; the only production consumer (`Actor.IsLocalPlayer`) uses `.Contains`, so behavior is unchanged.
- Kept `DefaultCombatLogSource` private-nested inside `CombatLogs` to avoid adding new public surface beyond the interface.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Adjusted hermetic fixture lines to avoid an unrelated latent Actor.GetId NPC-branch crash**
- **Found during:** Task 2 (hermetic `All_Logs_Are_Not_Null`)
- **Issue:** An initial fixture line used the brace-less NPC actor form `[Powerful Subscriber 688623358308676 (1/401177)]`. `Actor.GetId()`'s NPC branch (`Actor.cs:109-113`) slices between `{` and `}` without bounds-checking; with no braces `IndexOf('{')` returns -1 and `Slice` throws `ArgumentOutOfRangeException` (surfaced via `CombatLogLine.ToString()` → `Actor.ToString()` → `Actor.Id`).
- **Fix:** Replaced the fixture line with a brace-bearing NPC line (`Yozusk Mauler {3158140992356352}:...`) so the hermetic test exercises well-formed goldens only. The underlying `Actor.GetId` NPC-branch guard is OUT OF THIS PLAN'S SCOPE (this plan owns `CombatLogs.cs` + the two test files, not `Actor.cs` parse hardening) — logged to deferred items, NOT fixed here.
- **Files modified:** `SwtorLogParser.Tests/CombatLogLineTests.cs`
- **Verification:** Full suite green 3x consecutively.
- **Committed in:** `1c531a4` (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 test-fixture correction). No production-code deviations.
**Impact on plan:** No scope creep. The seam + facade were implemented exactly as specified; the fixture-data adjustment kept the hermetic test focused on well-formed goldens.

## Issues Encountered
- **Order-dependent flake confirmed and eliminated:** `All_Logs_Are_Not_Null` passed in isolation but failed under the full suite (ambient `ICombatLogSource` / real-folder state). Making both deferred tests install their own fixture and `ResetSource()` in `finally` removed the order dependency — verified by 3 consecutive green full-suite runs.

## Deferred Issues
- `Actor.GetId()` NPC branch (`Actor.cs:109-113`) and the parallel companion/player branches slice between `{`/`}` (and `#`) without bounds-checking; a brace-less NPC actor throws `ArgumentOutOfRangeException`. Out of scope for this plan (owns `CombatLogs.cs` + 2 test files). Candidate for a future Actor-parse hardening item.
- Pre-existing Overlay warning `CS0108` in `ParserForm.cs:140` (`MouseDown` hides inherited member) — unrelated to this plan, left untouched per scope boundary.

## Test Stability Verification (per critical constraint)
- **Build:** `dotnet build SwtorLogParser.slnx -c Debug` → Build succeeded (all hosts compile unchanged; only the pre-existing Overlay CS0108 warning).
- **Run 1:** `Passed! - Failed: 0, Passed: 100, Skipped: 0, Total: 100`
- **Run 2:** `Passed! - Failed: 0, Passed: 100, Skipped: 0, Total: 100`
- **Run 3:** `Passed! - Failed: 0, Passed: 100, Skipped: 0, Total: 100`
- Suite is STABLE across repeated runs; the `All_Logs_Are_Not_Null` flake is gone. (Baseline was 96; +4 seam tests = 100.)

## Next Phase Readiness
- Filesystem hermeticity unblocks the Phase 6 CI gate — the full suite is deterministic with no real SWTOR folders and no `TypeInitializationException`.
- The `ICombatLogSource` seam is available for any future monitor/log tests that need injected filesystem fixtures.

## Self-Check: PASSED

- All created files present (ICombatLogSource.cs, InMemoryCombatLogSource.cs, CombatLogSourceTests.cs, 03-05-SUMMARY.md).
- All task commits present (d89f13b, 4cd3a36, 1c531a4).

---
*Phase: 03-monitor-refactor-coverage*
*Completed: 2026-06-11*
