---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: completed
stopped_at: Completed 05-01-PLAN.md
last_updated: "2026-06-12T00:57:43.557Z"
last_activity: 2026-06-11 -- Phase 02 Plan 01 executed
progress:
  total_phases: 6
  completed_phases: 5
  total_plans: 16
  completed_plans: 16
  percent: 83
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-06-11)

**Core value:** The live DPS/HPS stats pipeline stays correct and reliable while the codebase becomes safe to maintain and extend — no regressions.
**Current focus:** Phase 01 — Parser Safety Net

## Current Position

Phase: 02 (Correctness Bugs) — EXECUTING
Plan: 3 of 3 complete
Status: 02-01 complete (BUG-05 leaf-parse hardening); ready for 02-02
Last activity: 2026-06-11 -- Phase 02 Plan 01 executed

Progress: [███████░░░] 67%

## Performance Metrics

**Velocity:**

- Total plans completed: 0
- Average duration: —
- Total execution time: 0 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| - | - | - | - |

**Recent Trend:**

- Last 5 plans: —
- Trend: —

*Updated after each plan completion*
| Phase 01 P01 | 4min | 2 tasks | 2 files |
| Phase 01 P02 | 6min | 2 tasks | 3 files |
| Phase 01 P03 | 4min | 2 tasks | 2 files |
| Phase 02 P01 | 12min | 3 tasks | 6 files |
| Phase 02 P02 | 9min | 3 tasks | 6 files |
| Phase 02 P03 | 3min | 4 tasks | 8 files |
| Phase 03 P01 | 9min | 2 tasks | 2 files |
| Phase 03 P02 | 2min | 2 tasks | 7 files |
| Phase 03 P03 | 8min | 2 tasks | 9 files |
| Phase 03 P04 | 7min | 1 tasks | 2 files |
| Phase 03 P05 | 6min | 2 tasks | 6 files |
| Phase 04 P01 | 9min | 2 tasks | 2 files |
| Phase 04 P02 | 7min | 1 tasks | 1 files |
| Phase 04 P03 | 4min | 2 tasks | 2 files |
| Phase 05 P01 | 3min | 3 tasks | 5 files |
| Phase 05 P02 | 7min | 2 tasks | 4 files |

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- Scope this milestone as "resolve all CONCERNS.md items" — user directive, concerns are concrete and well-cited
- Sequence: safety-net tests first, then bugs, then refactor, then perf, then deps, then CI
- AOT constraint: no reflection-heavy DI in the core library (SwtorLogParser is IsAotCompatible=true)
- System.CommandLine.Rendering alpha will be replaced (Spectre.Console or equivalent) in Phase 5
- [Phase 01]: Phase 1 characterizes EAGER parse-site throws with Assert.Throws (zero skips) rather than [Fact(Skip)] placeholders
- [Phase 01]: Fixed pre-existing RED Game_Objects_Are_Equal test by recharacterizing to the actual ReadOnlyMemory-identity equality contract (no production change)
- [Phase 01]: [Phase 01]: LAZY parse sites (Actor.Health/Id, Threat.Value, Value.Id) characterized via Assert.Throws on property access — never Assert.Null(Parse(...)) — to avoid the lazy-null trap; Phase 2 BUG-05 will invert these to graceful
- [Phase ?]: [Phase 01]: Ability.Parse is LAZY (does not read .Id) unlike eager GameObject.Parse — characterized via Assert.NotNull(Parse) then Assert.Throws on .Id; Action.Parse already graceful (try/catch) locked via Assert.Null [Theory]; seven-model coverage complete
- [Phase ?]: [Phase 02]: BUG-05 leaf-parse fixed via BCL TryParse (Threat.Value now int?, Actor/Value getters null-on-bad-input)
- [Phase 02]: BUG-05 GameObject/Ability subset fixed via ulong.TryParse — GameObject.Parse returns null eagerly, Ability.Id null transitively via inherited GetId
- [Phase 02]: BUG-06 caches are ConcurrentDictionary with first-writer-wins TryAdd (not blind GetOrAdd); never cache null/failed parse; Rom.GetHashCode key kept for RFCT-03/Phase 3
- [Phase ?]: BUG-03 timestamp validated via InvariantCulture TryParseExact in static factory; bad timestamp returns null
- [Phase ?]: BUG-01 Start/Stop lifecycle test deferred to Phase-3 TEST-01; verified by inspection + BUG-02 no-op test
- [Phase ?]: Added InternalsVisibleTo(SwtorLogParser.Tests) to unit-test the BUG-04 internal helper
- [Phase 03]: RFCT-02 collapsed #if RELEASE/#elif DEBUG into unconditional NullLogger-backed Instance; ILogger ctor made public; console/debug providers stay host-side (core lib AOT-safe)
- [Phase 03]: TEST-01 Stop_Halts_Delivery asserts IsRunning==false; Stop() does NOT complete the Rx Subject by design; PublishForTest pushes now-relative timestamps to clear the 10s DpsHps window
- [Phase 03]: RFCT-03 caches re-keyed to string content (rom.ToString), bounded at 4096 with FIFO eviction via in-repo AOT-safe BoundedCache; separate per-type caches (GameObjectCache/AbilityCache/ActionCache) fix the shared-cache (Ability?) cast bug
- [Phase 03]: Action.GetHashCode left unchanged per Pitfall 3 (RFCT-03 scope is the cache KEY, not GetHashCode)
- [Phase ?]: [Phase 03]: TEST-02 tests Accumulator + CalculateDpsHpsStats directly via InternalsVisibleTo (made internal) to bypass the DateTime.Now Where filter for deterministic DPS/HPS/crit% + 10s window assertions; no IClock/TimeProvider, no behavior change (PERF-03 stays Phase 4)
- [Phase ?]: [Phase 03]: TEST-01/02 CI-readiness via injectable ICombatLogSource seam behind the static CombatLogs facade; deferred tests rewritten hermetic over in-memory fixtures (no real SWTOR folders, no TypeInitializationException)
- [Phase ?]: PERF-01: ToString() count semantics locked to non-empty lines (diagnostic-only, not test-pinned)
- [Phase ?]: PERF-01: zero-copy line slices via string.AsMemory into the single ReadToEnd() backing string; one shared offset-tracking splitter for count + slices
- [Phase ?]: [Phase 04]: PERF-02 removed Console.Clear() from Native CLI monitor render; rows overwritten in place via SetCursorPosition+pad-to-width with IsOutputRedirected fallback, width/height clamps, and _lastRowCount vacated-row clearing; rendered text byte-identical
- [Phase 04]: [Phase 04]: PERF-03 single-pass CalculateDpsHpsStats — dropped OrderBy(TimeOfDay)+6 LINQ scans for one foreach; min/max by .TimeStamp.TimeOfDay (preserves across-midnight quirk, IN-01); independent damage/heal ifs; crit% over state.Count; Accumulator + DateTime.Now filter untouched; DpsHpsMathTests pass unchanged (106/106)
- [Phase ?]: [Phase 05]: CPM introduced via root Directory.Packages.props; all 7 managed packages on GA versions (no preview/alpha/beta)
- [Phase ?]: [Phase 05]: System.CommandLine + System.CommandLine.Rendering pinned via VersionOverride (no central PackageVersion) to satisfy CPM without NU1008/NU1010, pending deletion in 05-02
- [Phase ?]: [Phase 05]: DEP-03 removed System.CommandLine + System.CommandLine.Rendering from both CLI hosts; hand-rolled switch(args[0]) dispatch + Console.CancelKeyPress->CancellationTokenSource Ctrl+C bridge; managed CLI 5-column live table ported to Spectre.Console 0.57.0 (Native AOT host Spectre-free); INFRA-02 dropped DockerDefaultTargetOS from both csproj

### Pending Todos

None yet.

### Blockers/Concerns

- Phase 3 (RFCT-02) must not break AOT compatibility — DI path must be AOT-safe or confined to non-AOT hosts
- Phase 5 (DEP-03) System.CommandLine GA reshaped its API; CLI rendering rework scope TBD until Phase 5 planning

## Deferred Items

| Category | Item | Status | Deferred At |
|----------|------|--------|-------------|
| *(none)* | | | |

## Session Continuity

Last session: 2026-06-12T00:14:49.504Z
Stopped at: Completed 05-01-PLAN.md
Resume file: None
