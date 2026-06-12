---
phase: 04-performance
plan: 03
subsystem: stats-aggregation
tags: [performance, linq, single-pass, PERF-03, aot, rx]
requires:
  - CombatLogsMonitor.Accumulator (10s sliding-window HashSet<CombatLogLine>)
  - DpsHpsMathTests contract (DPS/HPS/crit%/window facts)
provides:
  - Single-pass CalculateDpsHpsStats (no OrderBy, no multi-pass LINQ); byte-identical DPS/HPS/crit% output
  - Single_Line_Uses_OneSecond_Window edge test locking the count<=1 => 1s branch
affects:
  - SwtorLogParser/Monitor/CombatLogsMonitor.cs (CalculateDpsHpsStats body only)
  - SwtorLogParser.Tests/DpsHpsMathTests.cs (new edge fact)
tech-stack:
  added: []
  patterns:
    - "Single-pass aggregation: one foreach over a HashSet tracking min/max + per-category sum/count/crit"
    - "Min/max tracked by the dropped sort key (.TimeStamp.TimeOfDay) to preserve endpoint selection exactly"
    - "Order-invariant Player via `player ??= line.Source` (GroupBy(Source.Name) upstream guarantees one player per state)"
key-files:
  created: []
  modified:
    - SwtorLogParser/Monitor/CombatLogsMonitor.cs
    - SwtorLogParser.Tests/DpsHpsMathTests.cs
decisions:
  - "Min/max selected by `.TimeStamp.TimeOfDay` (the same key the dropped OrderBy used); timeSpan = delta of the corresponding lines' full TimeStamp — preserves the across-midnight quirk (IN-01, out of scope), not fixed"
  - "Damage and heal use INDEPENDENT ifs (not else-if) — matches the original's independent Where passes (Pitfall 3)"
  - "crit% divides by state.Count (NOT damage/heal count); null-on-zero/infinity crit branches kept verbatim"
  - "timeSpan == TimeSpan.FromSeconds(1) when state.Count <= 1; Player taken from any element"
  - "Accumulator (lock, 10s RemoveWhere, Add) and the DateTime.Now window filter left untouched (IN-01 deferred)"
metrics:
  duration: 4min
  tasks: 2
  files: 2
  completed: 2026-06-12
---

# Phase 4 Plan 03: PERF-03 Single-Pass CalculateDpsHpsStats Summary

Collapsed `CombatLogsMonitor.CalculateDpsHpsStats` from an `OrderBy(x => x.TimeStamp.TimeOfDay)` sort plus six separate `Where`/`Sum`/`Count` LINQ scans into a single `foreach` over `state` (min/max timestamp + per-category sum/count/crit), producing byte-for-byte identical DPS/HPS/crit% output — locked by the `DpsHpsMathTests` contract which passes unchanged.

## What Was Built

### Task 1 — Wave-0 edge test (test-first)

Added `Single_Line_Uses_OneSecond_Window` `[Fact]` to `SwtorLogParser.Tests/DpsHpsMathTests.cs` (the existing 6 facts untouched). It accumulates a single `DamageLine("20:00:00.000", 1500)` and asserts `Assert.Single(state)`, `Assert.NotNull(stats.DPS)`, `Assert.Equal(1500d, stats.DPS!.Value, precision: 3)` (one line => divisor is `TimeSpan.FromSeconds(1)` => DPS == the line's value), and `Assert.Null(stats.HPS)`. This explicitly locks the `state.Count <= 1 => 1s` branch (previously covered only transitively) BEFORE the refactor. Verified GREEN against the current unmodified `CalculateDpsHpsStats` (7/7 DpsHpsMathTests). Committed as `test(04-03)` at **88fc86f**.

### Task 2 — Single-pass rewrite (PERF-03)

Rewrote the body of `internal PlayerStats CalculateDpsHpsStats(HashSet<CombatLogLine> state)` (signature, visibility, and `PlayerStats` return shape unchanged) as one `foreach (var line in state)`:

- **Endpoints:** `TimeSpan minTod/maxTod` track the min/max by `line.TimeStamp.TimeOfDay` — the SAME key the dropped `OrderBy(TimeOfDay)` used — while capturing the corresponding lines' full `TimeStamp` into `minStamp`/`maxStamp`. `timeSpan = state.Count > 1 ? (maxStamp - minStamp) : TimeSpan.FromSeconds(1)`, equivalent to the old `items[^1].TimeStamp - items[0].TimeStamp`. The pre-existing across-midnight quirk is preserved, not corrected (IN-01, out of scope; Pitfall 4).
- **Categories:** two INDEPENDENT `if`s (NOT else-if) — `if (line.IsPlayerDamage()) { damageCount++; damageTotal += line.Value!.Total; if (line.Value!.IsCritical) damageCrit++; }` and a separate `if (line.IsPlayerHeal()) { ... }` — matching the original's independent `Where` passes (Pitfall 3).
- **Player:** `player ??= line.Source;` (any element; order-invariant because `GroupBy(Source.Name)` upstream guarantees one player per `state`), replacing `state.ElementAt(0).Source`.
- **Output formulas kept verbatim:** `dpsCrit = (double)damageCrit / state.Count * 100` (divisor is `state.Count`, NOT damage/heal count); `dps = damageCount > 0 ? damageTotal / timeSpan.TotalSeconds : null`; `dpsCritP = double.IsInfinity(dpsCrit) || dpsCrit == 0.0d ? null : dpsCrit` (and the heal analogs).

The `Accumulator` (`static readonly object Lock`, the 10s `RemoveWhere(line => line.TimeStamp < combatLog.TimeStamp.AddSeconds(-10))`, the `Add`) and the `.Where(x => x.TimeStamp > DateTime.Now.AddSeconds(-10))` filter in `ConfigureObservables` are untouched. No new `using`, no reflection — core lib stays `IsAotCompatible=true`. Committed as `perf(04-03)` at **f18a743**.

## Verification

| Check | Result |
|-------|--------|
| Baseline (pre-work) `dotnet test` | Passed: 105, Failed: 0, Skipped: 0 |
| Task 1 `dotnet test --filter FullyQualifiedName~DpsHpsMathTests` | Passed: 7, Failed: 0, Skipped: 0 (GREEN against unmodified code) |
| Task 2 `dotnet build SwtorLogParser.slnx -c Debug --nologo` | Build succeeded, 0 errors (1 pre-existing unrelated CS0108 warning in Overlay — out of scope) |
| Task 2 `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj --nologo` | Passed: 106, Failed: 0, Skipped: 0 (105 baseline + 1 new edge test) |
| DpsHpsMathTests pass UNCHANGED | Confirmed — all 6 original facts + the new edge fact green; no numbers moved |
| Grep guard: no `OrderBy(...)` call in CalculateDpsHpsStats | Confirmed — the only 2 `OrderBy` hits are in explanatory comments, no LINQ call |
| Grep guard: `foreach (var line in state)` present | Confirmed (1) |
| Grep guard: Accumulator `AddSeconds(-10)` RemoveWhere + DateTime.Now filter unchanged | Confirmed (`AddSeconds(-10)` x2; `DateTime.Now.AddSeconds(-10)` x1) |

## Deviations from Plan

None - plan executed exactly as written. Both tasks committed individually; output is identical and all DpsHpsMathTests pass unchanged.

## Threat Surface

T-04-05 (output integrity) is mitigated as planned: the behavior-preserving rewrite is verified by the 7-fact `DpsHpsMathTests` on every commit; no numeric drift occurred. No new trust boundaries, IO sinks, network/auth/crypto, or AOT-incompatible patterns introduced.

## Known Stubs

None.

## Self-Check: PASSED

- FOUND: SwtorLogParser/Monitor/CombatLogsMonitor.cs (modified, builds; contains `foreach`, no `OrderBy` call)
- FOUND: SwtorLogParser.Tests/DpsHpsMathTests.cs (contains `Single_Line_Uses_OneSecond_Window`)
- FOUND: commit 88fc86f (test(04-03) edge test)
- FOUND: commit f18a743 (perf(04-03) single-pass rewrite)
- FOUND: full suite 106/106, 0 skipped
