---
phase: 01-parser-safety-net
plan: 03
subsystem: testing
tags: [xunit, characterization-tests, parser, theory, assert-throws]

# Dependency graph
requires:
  - phase: 01-parser-safety-net (plans 01, 02)
    provides: per-model characterization test conventions (Snake_Case, .AsMemory() bodies, Assert.Throws/Assert.Null strategy, cache-unique literals)
provides:
  - Ability LAZY inherited-Id throw characterization (Parse non-null, .Id throws FormatException)
  - Ability golden lock for apostrophe+spaces name shape with unsigned id
  - Action graceful-null [Theory] for malformed inner GameObject fragments (green via try/catch)
  - Seven-model edge-case + golden coverage complete (CONTEXT.md requirement)
affects: [02-bug-fixes (BUG-05 invert site for Ability.Id), parser-refactor]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Pattern E (LAZY throw): Parse returns non-null, Assert.Throws on property access (Ability.Id)"
    - "Pattern C (graceful-null): Action.Parse try/catch converts a throwing child to null"

key-files:
  created: []
  modified:
    - SwtorLogParser.Tests/AbilityTests.cs
    - SwtorLogParser.Tests/ActionTests.cs

key-decisions:
  - "Ability.Parse is LAZY (does not read .Id) unlike GameObject.Parse (eager) — characterized via Assert.NotNull(Parse) then Assert.Throws on .Id access; Phase 2 BUG-05 inverts to graceful"
  - "Action.Parse is already graceful (try/catch around throwing child GameObject.Parse) — locked with Assert.Null [Theory], no production change needed"

patterns-established:
  - "Cache-unique literals (Zqx* prefix) prevent GameObjectCache/ActionCache hits from masking the parse path under test"

requirements-completed: [TEST-03]

# Metrics
duration: 4min
completed: 2026-06-11
---

# Phase 01 Plan 03: Ability & Action Characterization Summary

**Ability LAZY inherited-Id throw characterized (Parse non-null, .Id throws FormatException) and Action graceful-null on malformed inner fragments locked via [Theory] — completing seven-model edge-case + golden coverage with zero production change.**

## Performance

- **Duration:** ~4 min
- **Started:** 2026-06-11
- **Completed:** 2026-06-11
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments
- `Ability_NonNumeric_Id_Throws_On_Access_Today`: confirms Ability.Parse is LAZY — returns non-null for non-numeric brace content and only throws on `.Id` access via the inherited `GetId` (GameObject.cs:79-99). This is the Phase-2 BUG-05 invert site.
- `Ability_Golden_Name_And_Id`: golden lock for a distinct apostrophe+spaces name shape with a distinct unsigned id (`814792340963328u`).
- `Action_Malformed_Inner_Fragment_Returns_Null` [Theory]: confirms Action.Parse's try/catch converts a throwing child GameObject.Parse (eager non-numeric id) into a graceful null — green today, regression lock.
- Full suite GREEN at 66 tests, zero skips (was 62; +4 new).

## Task Commits

Each task was committed atomically:

1. **Task 1: Ability golden lock + LAZY inherited-Id throw** - `d8f3481` (test)
2. **Task 2: Action graceful-null [Theory]** - `8744a3c` (test)

## Files Created/Modified
- `SwtorLogParser.Tests/AbilityTests.cs` - Added golden lock and LAZY `.Id` throw characterization (cache-unique literal `ZqxAbilityLazyWidget`)
- `SwtorLogParser.Tests/ActionTests.cs` - Added graceful-null [Theory] (2 rows, distinct `Zqx*` literals)

## Test Results

- **Filtered (AbilityTests):** Passed 4, Failed 0, Skipped 0
- **Filtered (ActionTests):** Passed 5, Failed 0, Skipped 0
- **Full suite:** Passed 66, Failed 0, Skipped 0
- `git diff --stat` confirms only `AbilityTests.cs` and `ActionTests.cs` changed — no `SwtorLogParser/Model/*.cs` production change.

## Decisions Made
- None beyond the plan. Both models behaved exactly as predicted by RESEARCH.md (Ability LAZY, Action graceful). No optional golden was skipped — the Ability golden covers a distinct apostrophe+spaces shape; the Action optional golden was skipped per plan guidance (existing flat + nested goldens already cover those shapes; a single-segment action would not exercise a distinct path through Action.Parse since it requires a `:` splitter).

## Deviations from Plan

None - plan executed exactly as written. Observed behavior matched predicted behavior for both models.

## Issues Encountered
None. Pre-existing CS8618 nullable warnings in `SwtorLogParser/Monitor/CombatLogsMonitor.cs` are out of scope (production code, not introduced by this plan) and were not touched.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All seven Parse-factory models now have edge-case + golden coverage (Ability, Action, Actor, CombatLogLine, GameObject, Threat, Value) — Phase 1 safety net complete.
- Phase 2 (BUG-05) can invert the LAZY `.Id`/`.Value`/`.Health` throw characterizations (including Ability.Id from this plan) to graceful-null behavior.

## Self-Check: PASSED

- FOUND: SwtorLogParser.Tests/AbilityTests.cs (modified)
- FOUND: SwtorLogParser.Tests/ActionTests.cs (modified)
- FOUND commit: d8f3481
- FOUND commit: 8744a3c

---
*Phase: 01-parser-safety-net*
*Completed: 2026-06-11*
