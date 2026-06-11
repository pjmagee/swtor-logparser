---
phase: 02-correctness-bugs
plan: 03
subsystem: parsing
tags: [datetime, invariantculture, tryparseexact, cancellationtoken, fileshare, monitor, xunit]

# Dependency graph
requires:
  - phase: 02-correctness-bugs (plan 02)
    provides: ConcurrentDictionary caches + System.Collections.Concurrent using in CombatLogs.cs (preserved here)
provides:
  - Locale-stable CombatLogLine timestamp gate (TryParseExact + InvariantCulture) — bad timestamp returns null
  - Nullable monitor CancellationTokenSource with null-guarded Stop() (safe no-op before Start)
  - Linked-CTS token wired to both worker tasks so Stop() actually cancels them
  - SecondSegmentOrNull guard for the CombatLogs static-ctor Split('_') (no TypeInitializationException)
  - Least-privilege FileAccess.Read open for combat-log files (FileShare.ReadWrite preserved)
  - InternalsVisibleTo for the test project (enables internal-helper unit tests)
affects: [03-test-infrastructure, monitor lifecycle, parsing correctness]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Validate-then-construct: static Parse factory validates input (TryParseExact) and passes the parsed value into a private ctor, so an invalid input returns null instead of a ctor throwing"
    - "Pure internal helper extraction: filesystem-coupled static-ctor logic extracted to an internal static helper that is unit-tested in isolation"
    - "Nullable lifecycle state + ?.-guarded teardown for safe Stop()-before-Start()"

key-files:
  created:
    - SwtorLogParser.Tests/CombatLogsHelperTests.cs
    - SwtorLogParser.Tests/CombatLogsMonitorTests.cs
  modified:
    - SwtorLogParser/Model/CombatLogLine.cs
    - SwtorLogParser/Monitor/CombatLogsMonitor.cs
    - SwtorLogParser/Monitor/CombatLog.cs
    - SwtorLogParser/Monitor/CombatLogs.cs
    - SwtorLogParser/SwtorLogParser.csproj
    - SwtorLogParser.Tests/CombatLogLineTests.cs

key-decisions:
  - "Timestamp validated as time-only HH:mm:ss[.fff] via InvariantCulture TryParseExact in the static factory (SWTOR emits time-only stamps); DateTime.Parse fully removed"
  - "BUG-01 Start/Stop lifecycle assertion intentionally omitted-and-documented (deferred to Phase-3 TEST-01) — the singleton's shared static state + real-directory access make it flaky/order-dependent; BUG-01 verified by inspection + BUG-02 no-op test instead"
  - "Added InternalsVisibleTo(SwtorLogParser.Tests) so the BUG-04 internal helper could be unit-tested in isolation"
  - "BUG-07 uses FileAccess.Read but KEEPS FileShare.ReadWrite so the live game writer is never blocked"

patterns-established:
  - "Validate-then-construct in static Parse factories"
  - "Extract filesystem-coupled static-ctor logic into pure internal helpers for hermetic testing"

requirements-completed: [BUG-01, BUG-02, BUG-03, BUG-04, BUG-07]

# Metrics
duration: 3min
completed: 2026-06-11
---

# Phase 2 Plan 03: Monitor + Parse Correctness Fixes Summary

**Locale-stable timestamp gate (TryParseExact/InvariantCulture), null-safe monitor Start/Stop with linked-token cancellation, guarded static-ctor filename split, and least-privilege read-only log opens — all five remaining correctness bugs landed with the suite green at 72/0/0.**

## Performance

- **Duration:** 3 min
- **Started:** 2026-06-11T21:01:21Z
- **Completed:** 2026-06-11T21:04:46Z
- **Tasks:** 4 (3 code tasks + 1 full-suite green gate)
- **Files modified:** 8 (6 modified, 2 created)

## Accomplishments
- BUG-03: `CombatLogLine.Parse` now gates the timestamp with `DateTime.TryParseExact` + `InvariantCulture` over explicit `HH:mm:ss[.fff]` formats in the static factory; a bad/locale-variant timestamp returns `null` (line skipped) instead of throwing `FormatException`. `DateTime.Parse` fully removed. This also resolves the previously-flaky `All_Logs_Are_Not_Null`.
- BUG-02: `_cancellationTokenSource` is nullable; `Stop()` uses `?.Cancel()` with null-safe logging, so `Stop()`-before-`Start()` is a safe no-op (no NRE).
- BUG-01: `Start()` now passes the linked CTS `.Token` to BOTH worker tasks (`MonitorAsync`/`ReadAsync`), so `Stop()`'s `Cancel()` actually reaches the worker loops.
- BUG-04: extracted `internal static SecondSegmentOrNull` that length-guards the `Split('_')` index; a settings filename without `'_'` is skipped (returns null), not a startup `TypeInitializationException`. Plan-02 ConcurrentDictionary cache fields preserved.
- BUG-07: `CombatLog.GetLogLines()` opens with `FileAccess.Read` (least privilege) while keeping `FileShare.ReadWrite` so the live SWTOR client writer is never blocked.

## Task Commits

Each task was committed atomically:

1. **Task 1: Timestamp gate + flipped characterization test (BUG-03)** - `e5cc4a7` (fix)
2. **Task 2: Static-ctor Split('_') guard + helper test (BUG-04)** - `3e44d8a` (fix)
3. **Task 3: Monitor cancellation wiring + Stop() null-guard + read-only open (BUG-01/02/07)** - `d2742f2` (fix)
4. **Task 4: Full-suite green gate** - verification-only, no commit (72 passed, 0 skipped, exit 0)

**Plan metadata:** see final docs commit.

## Files Created/Modified
- `SwtorLogParser/Model/CombatLogLine.cs` - TimeFormats array; TryParseExact/InvariantCulture gate in static Parse; ctor takes pre-parsed DateTime
- `SwtorLogParser/Monitor/CombatLogsMonitor.cs` - nullable CTS; null-guarded Stop(); linked `.Token` to both workers
- `SwtorLogParser/Monitor/CombatLog.cs` - FileAccess.Read (FileShare.ReadWrite preserved)
- `SwtorLogParser/Monitor/CombatLogs.cs` - SecondSegmentOrNull helper; null-dropping static-ctor projection
- `SwtorLogParser/SwtorLogParser.csproj` - InternalsVisibleTo(SwtorLogParser.Tests)
- `SwtorLogParser.Tests/CombatLogLineTests.cs` - flipped timestamp test to Assert.Null (BUG-03)
- `SwtorLogParser.Tests/CombatLogsHelperTests.cs` - new; hermetic SecondSegmentOrNull theory (BUG-04)
- `SwtorLogParser.Tests/CombatLogsMonitorTests.cs` - new; Stop_Before_Start_Does_Not_Throw (BUG-02) + GetLogLines_Opens_ReadOnly_And_Reads (BUG-07)

## Decisions Made
- Timestamp parsed as time-only `HH:mm:ss[.fff]` via InvariantCulture (SWTOR golden lines are all time-only); kept as a format array for trivial future extension.
- BUG-01 Start/Stop lifecycle test intentionally omitted-and-documented (deferred to Phase-3 TEST-01) — the `CombatLogsMonitor.Instance` singleton shares process-wide static state and `Start()` touches the real combat-logs directory, making a polled IsRunning assertion flaky/order-dependent. BUG-01 is verified by code inspection plus the BUG-02 no-op test. The hard contract (green-every-commit, zero skips) is preserved — no flaky or skipped test was shipped.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added InternalsVisibleTo for the test project**
- **Found during:** Task 2 (BUG-04 helper test)
- **Issue:** The plan specifies `internal static SecondSegmentOrNull`, but the test project had no access to internal members (no `InternalsVisibleTo` existed), so the helper test failed to compile (CS0117).
- **Fix:** Added `<InternalsVisibleTo Include="SwtorLogParser.Tests" />` to `SwtorLogParser.csproj`.
- **Files modified:** SwtorLogParser/SwtorLogParser.csproj
- **Verification:** Helper test compiles and passes (3/3).
- **Committed in:** `3e44d8a` (Task 2 commit)

**2. [Rule 1 - Bug] Corrected BUG-04 helper test InlineData expectations**
- **Found during:** Task 2 (BUG-04 helper test)
- **Issue:** The plan's behavior row `SecondSegmentOrNull("abc_def.ini") => "def"` contradicts the plan's own documented implementation (`parts[1]`), which yields `"def.ini"` (extension included — matching the original `Split('_')[1]` production semantics). Asserting `"def"` would have either failed or forced a semantics change that drops the extension.
- **Fix:** Changed the first InlineData row to `("abc_def", "def")`, preserving the original verbatim-second-segment production behavior. The null-on-no-underscore row and the multi-segment row are unchanged and still cover the BUG-04 guard.
- **Files modified:** SwtorLogParser.Tests/CombatLogsHelperTests.cs
- **Verification:** All three theory rows pass (3/3); guard behavior (null when no '_') proven.
- **Committed in:** `3e44d8a` (Task 2 commit)

---

**Total deviations:** 2 auto-fixed (1 blocking, 1 bug). Both confined to test wiring/data; no production-behavior change beyond the planned fixes. No scope creep.

## Issues Encountered
- None beyond the two deviations above.

## TDD Gate Compliance
Tasks 1-3 carry `tdd="true"`. Per the locked critical constraint, the BUG-03 timestamp test flip had to land in the SAME commit as the fix (a pre-flip RED commit would have left the suite red between commits, violating the green-every-commit hard contract). For Tasks 2 and 3 the new tests and their production code were likewise committed together to keep every commit green. RED was observed locally during execution (test failures surfaced before the fixes/data corrections), but the green-every-commit contract took precedence over separate RED commits — consistent with the plan's hard constraint.

## Verification

- Per-task filtered runs all exit 0, zero skips: CombatLogLineTests 8/0, CombatLogsHelperTests 3/0, CombatLogsMonitorTests 2/0.
- Full suite (Task 4 gate): **72 passed, 0 failed, 0 skipped, exit 0** (67 baseline + 5 new).
- Grep confirmations: `TryParseExact` present in `CombatLogLine.cs`, no `DateTime.Parse(`; `CancellationTokenSource?` + `_cancellationTokenSource?.Cancel()` + linked `token` to both workers in `CombatLogsMonitor.cs`; `FileAccess.Read` + `FileShare.ReadWrite` at `CombatLog.cs:24`; `SecondSegmentOrNull` defined and used in `CombatLogs.cs`.

### Manual / human-check (deferred, non-blocking — human_verify_mode=end-of-phase)
BUG-07 live-tailing and BUG-01 Stop() require the running SWTOR client and are recorded as manual-only:
- With the game running and actively writing a combat log, start the monitor and confirm new lines still appear AND the game is not blocked from writing (validates FileAccess.Read + FileShare.ReadWrite).
- Start the monitor, call Stop(), confirm background CPU/file activity ceases (or rely on the Phase-3 TEST-01 lifecycle test, since the Start/Stop unit test was deferred).

## Next Phase Readiness
- Phase 2 (correctness bugs) is complete: BUG-01..07 resolved across plans 01-03; suite green with zero skips.
- Phase 3 should pick up: TEST-01 monitor-lifecycle test (deferred BUG-01 Start/Stop assertion); the `CombatLogsMonitor.ReadAsync` open already uses FileAccess.Read (noted, no change needed); the `CombatLogs` static-ctor real-directory coupling remains for the planned Phase-3 abstraction work.

## Self-Check: PASSED

- Files verified on disk: CombatLogsHelperTests.cs, CombatLogsMonitorTests.cs, 02-03-SUMMARY.md
- Commits verified in git log: e5cc4a7, 3e44d8a, d2742f2

---
*Phase: 02-correctness-bugs*
*Completed: 2026-06-11*
