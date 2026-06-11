---
phase: 03-monitor-refactor-coverage
plan: 04
subsystem: testing
tags: [xunit, rx, dps-hps, internalsvisibleto, deterministic-tests]

# Dependency graph
requires:
  - phase: 03-monitor-refactor-coverage (plan 01)
    provides: public CombatLogsMonitor(ILogger) ctor used to construct the monitor and reach the internal math methods
provides:
  - Deterministic DPS/HPS/crit% + 10s sliding-window expiry tests calling the internal math methods directly (TEST-02)
  - Accumulator + CalculateDpsHpsStats promoted from private to internal (visibility-only; no behavior change)
affects: [phase-04-perf, PERF-03, accumulator-rewrite]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Direct internal-method math test bypasses the DateTime.Now Where filter for deterministic exact-number assertions"

key-files:
  created:
    - SwtorLogParser.Tests/DpsHpsMathTests.cs
  modified:
    - SwtorLogParser/Monitor/CombatLogsMonitor.cs

key-decisions:
  - "TEST-02: test Accumulator + CalculateDpsHpsStats DIRECTLY (via InternalsVisibleTo) to bypass the DateTime.Now Where filter — deterministic exact DPS/HPS/crit% assertions, no IClock/TimeProvider introduced (Phase 4 overlap avoided)"
  - "Crit is encoded as a '*' inside the value parens (CombatLogs.Critical = \"*\"), e.g. (1000*) — NOT a <threat> token; corrected the test crit marker accordingly"

patterns-established:
  - "Direct-method math tests: construct known CombatLogLine inputs, call internal Accumulator/CalculateDpsHpsStats, assert with precision tolerance — locks current behavior without touching the time-dependent Rx pipeline"

requirements-completed: [TEST-02]

# Metrics
duration: 7min
completed: 2026-06-11
---

# Phase 3 Plan 04: DPS/HPS Math Coverage (TEST-02) Summary

**Deterministic DPS/HPS/crit% and 10s sliding-window expiry tests calling the now-internal Accumulator + CalculateDpsHpsStats directly, bypassing the DateTime.Now pipeline filter — visibility-only production change, zero behavior change.**

## Performance

- **Duration:** ~7 min
- **Started:** 2026-06-11T22:14Z
- **Completed:** 2026-06-11
- **Tasks:** 1 (TDD: RED + GREEN)
- **Files modified:** 2 (1 created, 1 modified)

## Accomplishments
- Promoted `Accumulator` and `CalculateDpsHpsStats` from `private` to `internal` (visibility-only — the `lock`, the 10s `RemoveWhere`, the crit% formula, order-by-TimeOfDay, and null-on-zero/infinity logic are byte-identical).
- Added `DpsHpsMathTests` (6 facts) asserting DPS, HPS, crit%, zero-crit-maps-to-null, and the 10s window expiry/keep behavior against known `CombatLogLine` inputs.
- Tests bypass the `.Where(x => x.TimeStamp > DateTime.Now.AddSeconds(-10))` pipeline filter by calling the math methods directly, so exact numbers are asserted deterministically (no wall-clock flakiness, no IClock/TimeProvider introduced).

## Task Commits

TDD task — RED then GREEN:

1. **Task 1 (RED): failing deterministic DPS/HPS math tests** - `8ddbdc8` (test)
2. **Task 1 (GREEN): make Accumulator + CalculateDpsHpsStats internal** - `d97a679` (feat)

No REFACTOR commit was needed — the tests were clean and the production change is minimal.

## Files Created/Modified
- `SwtorLogParser.Tests/DpsHpsMathTests.cs` (created) - 6 `[Fact]`s: `Dps_Computed_From_Known_Damage`, `Hps_Computed_From_Known_Heals`, `Crit_Percent_Computed`, `Zero_Crit_Maps_To_Null`, `Window_Expiry_Removes_Old_Lines`, `Window_Keeps_Recent_Lines`. Builds known lines via `CombatLogLine.Parse` and calls the internal methods directly.
- `SwtorLogParser/Monitor/CombatLogsMonitor.cs` (modified) - `Accumulator` (line ~67) and `CalculateDpsHpsStats` (line ~84) changed `private` → `internal`; explanatory comments added. No method body changes.

## Test Results
- **Baseline (before plan):** 90 passed, 0 skipped.
- **After plan:** `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` → **96 passed, 0 failed, 0 skipped** (90 baseline + 6 new), duration ~332 ms.
- Math-only filter (`FullyQualifiedName~DpsHpsMathTests`) → 6 passed, 0 skipped.
- DPS assertion: two damage lines 1.0s apart (1000 + 2000) → DPS = 3000 (precision 3). HPS analog: (500 + 1500) → 2000. Crit: 1 of 2 critical → 50%. Window: line 11s older is evicted, line 9s older is kept.

## Decisions Made
- Test the math methods DIRECTLY rather than through the Rx pipeline, to bypass the `DateTime.Now` `Where` filter (the central determinism landmine from 03-RESEARCH Pitfall 1, strategy 1). This locks the CURRENT product behavior; PERF-03 (accumulator rewrite) remains Phase 4.
- Did NOT introduce `IClock`/`TimeProvider` and did NOT alter the Rx pipeline or DpsHps semantics (locked constraint / Phase 4 overlap).
- Encoded crit as `*` inside the value parentheses (`CombatLogs.Critical = "*"`) — the actual product encoding — not as a `<threat>` token.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Corrected the test's critical-marker encoding**
- **Found during:** Task 1 (GREEN run — `Crit_Percent_Computed` failed: `DPSCritP` was null, not 50)
- **Issue:** The RED test encoded crit as a trailing `<3880>` token (the Threat section), but `Value.IsCritical` checks for `*` inside the value parentheses (`CombatLogs.Critical = "*"`). With the wrong marker, no line was counted as critical, so crit% was 0 → mapped to null by the current code.
- **Fix:** Encode crit as `*` inside the value, e.g. `(1000*)`; added an in-helper `Assert.Equal(critical, line.Value!.IsCritical)` guard so the test inputs self-verify their crit flag.
- **Files modified:** SwtorLogParser.Tests/DpsHpsMathTests.cs
- **Verification:** `Crit_Percent_Computed` and `Zero_Crit_Maps_To_Null` pass; full suite 96/0/0.
- **Committed in:** `d97a679` (GREEN commit)

---

**Total deviations:** 1 auto-fixed (1 test-correctness bug). No production behavior changed beyond method visibility.
**Impact on plan:** The fix was confined to the test fixture (correct crit encoding). No scope creep; the locked DPS/HPS behavior is unchanged.

## Issues Encountered
- Initial GREEN run had 1 failing crit test due to the wrong crit-marker encoding in the test inputs — resolved by using the actual `*`-in-parens encoding (see Deviation 1).

## User Setup Required
None - no external service configuration required.

## Known Stubs
None.

## Threat Flags
None - no new network/auth/file/schema surface; only a visibility change and a new test file. Matches the plan's threat register (T-03-05 accept, T-03-SC n/a).

## Next Phase Readiness
- TEST-02 complete: DPS/HPS arithmetic and the 10s sliding-window expiry are locked by deterministic direct-method tests.
- The `internal` visibility of `Accumulator`/`CalculateDpsHpsStats` gives Phase 4 (PERF-03) a test harness to verify the accumulator rewrite preserves behavior.
- Suite green, zero skips. No blockers.

## Self-Check: PASSED
- FOUND: SwtorLogParser.Tests/DpsHpsMathTests.cs
- FOUND: commit 8ddbdc8 (RED)
- FOUND: commit d97a679 (GREEN)

---
*Phase: 03-monitor-refactor-coverage*
*Completed: 2026-06-11*
