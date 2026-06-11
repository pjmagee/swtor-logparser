---
phase: 02-correctness-bugs
plan: 02
subsystem: api
tags: [csharp, dotnet8, concurrentdictionary, span-parsing, thread-safety, tryparse]

# Dependency graph
requires:
  - phase: 01-parser-safety-net
    provides: characterization tests locking EAGER/LAZY parse-site throw behavior (GameObject eager, Ability lazy, Action graceful)
  - phase: 02-correctness-bugs (plan 01)
    provides: BUG-05 leaf-parse hardening (Threat/Actor/Value getters null-on-bad-input via TryParse)
provides:
  - GameObject.Parse returns null (not throws) on non-numeric brace id (BUG-05 GameObject/Ability subset)
  - Ability.Id returns null transitively via inherited GameObject.GetId
  - ConcurrentDictionary-backed ActionCache and GameObjectCache with first-writer-wins TryAdd (BUG-06)
  - Concurrency smoke test locking single-instance convergence under parallel parse
affects: [02-03 static-ctor-and-monitor-wiring, 03-refactor-caches RFCT-03 cache key redesign]

# Tech tracking
tech-stack:
  added: [System.Collections.Concurrent.ConcurrentDictionary]
  patterns:
    - "First-writer-wins cache write: TryGetValue fast-path -> construct -> TryAdd -> on false return cached instance"
    - "ulong.TryParse(span, out var v) ? v : (ulong?)null ternary for nullable id getters"

key-files:
  created: []
  modified:
    - SwtorLogParser/Monitor/CombatLogs.cs
    - SwtorLogParser/Model/GameObject.cs
    - SwtorLogParser/Model/Ability.cs
    - SwtorLogParser/Model/Action.cs
    - SwtorLogParser.Tests/GameObjectTests.cs
    - SwtorLogParser.Tests/AbilityTests.cs

key-decisions:
  - "Used conditional first-writer-wins TryAdd (NOT blind GetOrAdd with a new-value factory) so failed/null parses are never cached and the Action ctor exception stays inside the existing try/catch"
  - "Kept the existing Rom.GetHashCode() int cache key and the shared GameObject/Ability cache (key redesign is RFCT-03/Phase 3)"

patterns-established:
  - "First-writer-wins ConcurrentDictionary write: construct, validate, TryAdd, fall back to cached instance on race loss"
  - "Nullable id parse via ulong.TryParse ternary mirroring Phase-1/02-01 leaf TryParse style"

requirements-completed: [BUG-05, BUG-06]

# Metrics
duration: 9min
completed: 2026-06-11
---

# Phase 02 Plan 02: Graceful GameObject Id Parsing + Thread-Safe Parse Caches Summary

**GameObject/Ability non-numeric ids now skip (null) instead of crashing the reader (BUG-05), and the two shared static parse caches are ConcurrentDictionary with first-writer-wins TryAdd (BUG-06) — no Dictionary.Add race.**

## Performance

- **Duration:** 9 min
- **Started:** 2026-06-11T20:53:16Z
- **Completed:** 2026-06-11
- **Tasks:** 3
- **Files modified:** 6

## Accomplishments
- `GameObject.cs` GetId/GetParentId (3 sites) converted from bare `ulong.Parse` to `ulong.TryParse` ternary; `GameObject.Parse("...{abc}")` now returns null via the existing eager `if (Id == null) return null` cascade — no try/catch added.
- `Ability.Id` returns null instead of throwing FormatException on non-numeric brace content, fixed transitively via the inherited `GameObject.GetId` (Ability.Parse stays lazy / non-null).
- Both `ActionCache` and `GameObjectCache` converted to `ConcurrentDictionary<int,_>`; all three Parse cache writes use conditional first-writer-wins `TryAdd` (no failed/null parse is ever cached; Action ctor exception still caught by the existing try/catch).
- Added a 256-way `Parallel.For` concurrency smoke test confirming parallel `GameObject.Parse` on shared backing memory never throws and converges on a single cached instance.

## Task Commits

Each task was committed atomically:

1. **Task 1: Guard GameObject GetId/GetParentId with ulong.TryParse, flip GameObject + Ability tests** - `5f806f4` (fix)
2. **Task 2: Convert the two shared caches to ConcurrentDictionary with conditional writes** - `6983d6d` (fix)
3. **Task 3: Add a concurrency smoke test + full-suite green gate** - `ca9863f` (test)

**Plan metadata:** committed separately (docs: complete plan)

## Files Created/Modified
- `SwtorLogParser/Monitor/CombatLogs.cs` - Added `using System.Collections.Concurrent;`; both caches now `ConcurrentDictionary<int,_>`.
- `SwtorLogParser/Model/GameObject.cs` - 3 id sites use `ulong.TryParse` ternary; `Parse` uses first-writer-wins `TryAdd` and never caches a null-Id object.
- `SwtorLogParser/Model/Ability.cs` - `Parse` uses first-writer-wins `TryAdd` into the shared GameObjectCache; preserved `(Ability?)` cast on both read and race-loser paths.
- `SwtorLogParser/Model/Action.cs` - `Parse` uses first-writer-wins `TryAdd` inside the existing try/catch; `catch`/`return null` left intact.
- `SwtorLogParser.Tests/GameObjectTests.cs` - Flipped `GameObject_NonNumeric_Id` to `Assert.Null`; added `GameObject_Concurrent_Parse_Same_Memory_Single_Instance`.
- `SwtorLogParser.Tests/AbilityTests.cs` - Flipped `Ability_NonNumeric_Id` to `Assert.NotNull(ability)` + `Assert.Null(ability.Id)`.

## Test Results

- **Baseline (before plan):** 66 passed, 0 skipped.
- **Task 1 filtered (GameObject|Ability):** 16 passed, 0 skipped.
- **Task 2 filtered (GameObject|Ability|Action):** 21 passed, 0 skipped.
- **Task 3 full suite (green gate):** 67 passed, 0 failed, 0 skipped, exit 0.

The net +1 is the new concurrency smoke test. All goldens, the flipped characterization tests, and the Action graceful-null matrix stay green.

## Verification

- `ConcurrentDictionary` present in `CombatLogs.cs`: confirmed (both cache declarations).
- `GameObjectCache.Add(` / `ActionCache.Add(`: 0 hits (grep).
- Bare `ulong.Parse` in `GameObject.cs`: 0 hits (grep).
- Blind `GetOrAdd(`: 0 hits (grep).
- Full `dotnet test`: exit 0, zero skips.

## Decisions Made
- Chose conditional first-writer-wins `TryAdd` over a blind `GetOrAdd(key, _ => new T(rom))` exactly as the plan/RESEARCH mandated: the factories return null on parse failure and the Action ctor can throw, both of which a blind GetOrAdd would mishandle (cache a null or leak the ctor exception). On race loss, the code falls back to the cached instance via `TryGetValue`, preserving the shared-cache identity contract that `Game_Objects_Equality_Reflects_Backing_Memory` locks.
- Kept the existing `Rom.GetHashCode()` int key and the shared GameObject/Ability cache untouched (key redesign is RFCT-03/Phase 3).

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None. Pre-existing CS8618 warnings in `CombatLogsMonitor.cs` are out of scope (plan 02-03) and were left untouched per the critical constraints.

## User Setup Required
None - no external service configuration required. All APIs (`ulong.TryParse`, `ConcurrentDictionary`) are .NET 8 BCL in-box; no package installs.

## Next Phase Readiness
- BUG-05 (GameObject/Ability subset) and BUG-06 (cache thread-safety) closed. The reader task can no longer corrupt the shared dictionaries or crash on a non-numeric brace id.
- Plan 02-03 (CombatLogsMonitor cancellation wiring, Stop()-before-Start() guard, static-ctor `Split('_')` hardening) is unblocked and untouched by this plan.
- The shared GameObject/Ability cache key remains `Rom.GetHashCode()` — RFCT-03/Phase 3 owns the content-based key redesign; this plan deliberately did not regress the cross-type cache behavior.

## Self-Check: PASSED

---
*Phase: 02-correctness-bugs*
*Completed: 2026-06-11*
