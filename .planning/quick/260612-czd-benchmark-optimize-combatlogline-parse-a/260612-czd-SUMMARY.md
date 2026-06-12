---
phase: quick-260612-czd
plan: 01
subsystem: parser-hotpath
tags: [perf, benchmark, allocation, cache, lazy-parse, aot]
requires: [SwtorLogParser core library, .NET 10 ConcurrentDictionary alternate lookup]
provides: [BenchmarkDotNet regression harness, span-keyed cache lookup, lazy CombatLogLine sub-parsing]
affects: [SwtorLogParser/Caching/BoundedCache.cs, SwtorLogParser/Model/CombatLogLine.cs, hot parse path]
tech-stack:
  added: [BenchmarkDotNet 0.15.8]
  patterns: [ConcurrentDictionary.GetAlternateLookup<ReadOnlySpan<char>>, per-member parsed-flag memoization]
key-files:
  created:
    - SwtorLogParser.Benchmarks/SwtorLogParser.Benchmarks.csproj
    - SwtorLogParser.Benchmarks/Program.cs
    - SwtorLogParser.Benchmarks/CombatLogLineParseBenchmarks.cs
    - SwtorLogParser.Benchmarks/Fixtures/sample-combat.log
  modified:
    - Directory.Packages.props
    - SwtorLogParser.slnx
    - .gitignore
    - SwtorLogParser/Caching/BoundedCache.cs
    - SwtorLogParser/Model/GameObject.cs
    - SwtorLogParser/Model/Ability.cs
    - SwtorLogParser/Model/Action.cs
    - SwtorLogParser/Model/CombatLogLine.cs
    - SwtorLogParser.Tests/ParseCacheTests.cs
decisions:
  - "Embed the benchmark fixture as an EmbeddedResource (not CopyToOutputDirectory Content) — BenchmarkDotNet relocates the runner to a nested bin dir where copied content is absent"
  - "Negate *.log in .gitignore for SwtorLogParser.Benchmarks/Fixtures so the sanitized fixture commits"
metrics:
  duration: ~40min
  completed: 2026-06-12
---

# Phase quick-260612-czd Plan 01: Benchmark + Optimize CombatLogLine.Parse Allocation Summary

Stood up a BenchmarkDotNet [MemoryDiagnoser] harness over `CombatLogLine.Parse`, then landed two allocation optimizations — span-keyed cache lookup (kills the per-line `rom.ToString()` cache-key alloc on HITs) and lazy `CombatLogLine` sub-parsing (dropped lines skip Source/Target/Ability/Action/Value/Threat) — cutting pure-parse allocation 55% with zero behavior change.

## What Was Built

- **`SwtorLogParser.Benchmarks`** — net10.0 console project, `[MemoryDiagnoser]` + `[ShortRunJob]`, three benchmarks (pure-parse, touch-all, hot-cache) over a 2600-line sanitized fixture (`@Player#000000000000000` placeholder, no real handle). Registered in `.slnx`; BenchmarkDotNet 0.15.8 pinned in CPM. The fixture is an EmbeddedResource so it resolves from BenchmarkDotNet's relocated runner.
- **Optimization 1 — span-keyed cache lookup (PERF-CACHE-01):** `BoundedCache<TValue>` now keys its `ConcurrentDictionary` with `StringComparer.Ordinal` and exposes `GetAlternateLookup<ReadOnlySpan<char>>()`. New `TryGetValue(ReadOnlySpan<char>)` and `GetOrAdd(ReadOnlySpan<char>, Func<TValue>)`. `GameObject`/`Ability`/`Action` `Parse` hit the span lookup FIRST and only materialize the string key on a MISS. FIFO eviction (cap 4096), lost-add-race semantics, and the string `GetOrAdd` overload are byte-identical.
- **Optimization 2 — lazy CombatLogLine sub-parsing (PERF-LAZY-01):** the six nullable sub-properties parse on first access via per-member `bool _xParsed` flags (null memoized too). `TimeStamp` stays eager (it gates `Parse` returning null). `GetHashCode`/`Equals`/`ToString` unchanged.

## Before / After (BenchmarkDotNet, MemoryDiagnoser, ShortRunJob, Release)

13th Gen Intel Core i9-13900K, .NET SDK 10.0.301, runtime .NET 10.0.9. 2600-line fixture, allocation is per-op over the full fixture pass. BEFORE = unmodified parser (Task 1); AFTER = both optimizations (Task 3). Allocations normalized to KB (BEFORE reported 2.07 MB = 2119.68 KB).

| Benchmark | Metric | BEFORE | AFTER | Delta |
|-----------|--------|-------:|------:|------:|
| **ParseAllLines** (pure parse, TimeStamp only) | Allocated | 2119.68 KB | 954.69 KB | **−55.0%** |
| | Gen0 | 113.28 | 50.78 | −55.2% |
| | Mean | 1.485 ms | 1.456 ms | −2.0% |
| **ParseAllLines_TouchAll** (Source/Action/Value) | Allocated | 2119.68 KB | 1559.88 KB | **−26.4%** |
| | Gen0 | 113.28 | 83.98 | −25.9% |
| | Mean | 1.611 ms | 1.905 ms | +18.2%* |
| **ParseAllLines_HotCache** (all sub-objects, warm cache) | Allocated | 2119.68 KB | 1621.54 KB | **−23.5%** |
| | Gen0 | 113.28 | 87.89 | −22.4% |
| | Mean | 1.639 ms | 1.520 ms | −7.3% |

\* The TouchAll mean delta is within the noisy ShortRun confidence interval (Error ±0.31 ms on the AFTER run, ±0.18 ms BEFORE) — the deterministic allocation/Gen0 numbers are the load-bearing metric and both dropped ~26%. ns/op is reported but not the headline; re-run with a full job for tighter timing if needed.

**Optimization attribution:**
- **Lazy sub-parsing (Opt 2)** dominates `ParseAllLines`: BEFORE the ctor eagerly parsed all six sub-objects (so pure-parse == touch-all at 2.07 MB); AFTER, a line touched only for TimeStamp pays no sub-parse cost → −55%.
- **Span-keyed cache (Opt 1)** drives the `TouchAll`/`HotCache` deltas: every sub-object cache lookup used to allocate a string key via `rom.ToString()` even on HITs; the span alternate lookup removes that → −23% to −26% on the property-touching benchmarks.

## Tests

- **110 / 110 passing** in Release (106 pre-existing + 4 new span-lookup tests). No existing test weakened or deleted.
- New `ParseCacheTests`: `Span_Lookup_Returns_Same_Instance_As_String_Lookup`, `Span_Lookup_Hit_Does_Not_Grow_Cache`, `Span_GetOrAdd_Inserts_Once_On_Miss_Then_Hits`, `GameObject_Span_Hot_Path_Dedups_Identical_Content`.
- The existing `CombatLogLineTests` (exact null/non-null assertions on all six sub-properties) are the regression gate proving the lazy refactor is byte-identical — all green.

## AOT / Behavior Invariants

- Core library `SwtorLogParser` builds Release with **0 warnings** — `IsAotCompatible=true` preserved. `GetAlternateLookup` is reflection-free; no new reflection, no new package in the core lib.
- Native AOT CLI (`SwtorLogParser.Native.Cli`) builds Release with **0 IL2xxx/IL3xxx warnings** — AOT-contamination boundary holds (BenchmarkDotNet lives only in the benchmark project, never referenced by core or hosts).
- Parser model and the `CombatLogsMonitor.Instance.DpsHps` stream behavior are unchanged (FROZEN per milestone constraint) — same parse calls, same slices, same null contract.

## Regression Gate

The harness is the documented allocation-regression gate. Re-run:

```
dotnet run --project SwtorLogParser.Benchmarks -c Release
```

`BenchmarkDotNet.Artifacts/` stays gitignored (the committed fixture is the only `.log` un-ignored, scoped to `SwtorLogParser.Benchmarks/Fixtures/`).

## Deviations from Plan

**1. [Rule 3 - Blocking] Fixture embedded as resource instead of CopyToOutputDirectory Content**
- **Found during:** Task 1 (first benchmark run errored with `DirectoryNotFoundException`).
- **Issue:** BenchmarkDotNet generates and relocates the runner into a nested `bin\...\<proj>-1\bin\...` directory where `CopyToOutputDirectory` content is not present, so `AppContext.BaseDirectory\Fixtures\sample-combat.log` did not exist and `[GlobalSetup]` threw → all three benchmarks reported NA.
- **Fix:** Changed the csproj `<Content>` to `<EmbeddedResource>` and load the fixture from the assembly manifest (`GetManifestResourceStream`), which is location-independent. The fixture is still committed to `Fixtures/sample-combat.log`.
- **Files:** SwtorLogParser.Benchmarks/SwtorLogParser.Benchmarks.csproj, CombatLogLineParseBenchmarks.cs
- **Commit:** 4d2286f

**2. [Rule 3 - Blocking] .gitignore negation for the committed fixture**
- **Found during:** Task 1 (`git add` refused the fixture).
- **Issue:** `.gitignore` line 95 `*.log` ignored the intentional committed fixture (`sample-combat.log` uses the real combat-log extension on purpose). Not in the plan's files list.
- **Fix:** Added `!SwtorLogParser.Benchmarks/Fixtures/*.log` negation directly under the `*.log` rule. `BenchmarkDotNet.Artifacts/` remains ignored (separate rule).
- **Files:** .gitignore
- **Commit:** 4d2286f

**Note (not a deviation):** `SwtorLogParser/Monitor/CombatLogs.cs` carried a pre-existing, unstaged, whitespace-only reformatting in the working tree at execution start (cosmetic line-wrapping, no behavior change). Per the scope boundary it was left untouched and excluded from all task commits.

## Self-Check: PASSED

- Created files verified present: benchmark csproj, Program.cs, CombatLogLineParseBenchmarks.cs, Fixtures/sample-combat.log (2600 lines).
- Commits verified in git log: 4d2286f (T1), 3bbcee4 (T2), 72811c8 (T3).
- Fixture contains NO real handle — only `@Player#000000000000000`.
