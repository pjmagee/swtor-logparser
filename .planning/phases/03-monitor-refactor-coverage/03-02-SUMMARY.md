---
phase: 03-monitor-refactor-coverage
plan: 02
subsystem: parse-caches
tags: [RFCT-03, caching, thread-safety, aot, dos-mitigation]
requires:
  - "SwtorLogParser.Monitor.CombatLogs static caches"
  - "GameObject/Ability/Action Parse factories"
provides:
  - "SwtorLogParser.Caching.BoundedCache<TValue> — content-keyed, bounded, thread-safe (AOT-safe, no package)"
  - "Three separate per-type content-keyed bounded caches (GameObjectCache, AbilityCache, ActionCache)"
  - "Fixed latent Ability/GameObject shared-cache cast bug"
affects:
  - "SwtorLogParser/Monitor/CombatLogs.cs"
  - "SwtorLogParser/Model/GameObject.cs"
  - "SwtorLogParser/Model/Ability.cs"
  - "SwtorLogParser/Model/Action.cs"
tech-stack:
  added: []
  patterns:
    - "In-repo BoundedCache: ConcurrentDictionary<string,T> + ConcurrentQueue<string> FIFO eviction"
    - "Content keys via rom.ToString() (not ReadOnlyMemory.GetHashCode identity)"
key-files:
  created:
    - "SwtorLogParser/Caching/BoundedCache.cs"
    - "SwtorLogParser.Tests/ParseCacheTests.cs"
  modified:
    - "SwtorLogParser/Monitor/CombatLogs.cs"
    - "SwtorLogParser/Model/GameObject.cs"
    - "SwtorLogParser/Model/Ability.cs"
    - "SwtorLogParser/Model/Action.cs"
    - "SwtorLogParser.Tests/GameObjectTests.cs"
decisions:
  - "Cap 4096 entries per cache, FIFO insertion-order eviction (not strict LRU) — locked Phase 3 decision"
  - "Separate per-concrete-type caches (Option A) to eliminate cross-type cast collision"
  - "Recharacterized Game_Objects_Equality test to the new content-key dedup contract (Rule 1)"
  - "Action.GetHashCode left unchanged (Pitfall 3 flag-don't-fix; scope is cache KEY only)"
metrics:
  duration: 2min
  completed: 2026-06-11
---

# Phase 3 Plan 02: Content-Keyed Bounded Parse Caches Summary

Re-keyed the static parse caches to string content keys, bounded them at 4096 entries with FIFO eviction via a new AOT-safe in-repo `BoundedCache<TValue>`, and fixed the latent `Ability`/`GameObject` shared-cache cast bug by giving each concrete type its own cache.

## What Was Built

- **`SwtorLogParser/Caching/BoundedCache.cs`** — `internal sealed class BoundedCache<TValue>` keyed by `string` content. Backed by `ConcurrentDictionary<string,TValue>` plus a `ConcurrentQueue<string>` for insertion-order (FIFO) eviction. Exposes `TryGetValue`, `GetOrAdd(string, value)`, and an internal `Count`. On add: `TryAdd` → enqueue → `while (Count > cap) TryRemove(oldest)`. Preserves the Phase 2 "another thread won the race → return cached instance" semantics. BCL-only — no reflection, no WinForms, no new NuGet package (stays AOT-clean).
- **`CombatLogs.cs`** — replaced the two `ConcurrentDictionary<int,...>` fields with THREE separate per-type caches: `GameObjectCache`, NEW `AbilityCache`, and `ActionCache`, each `BoundedCache<T>(4096)`.
- **`GameObject.Parse` / `Ability.Parse` / `Action.Parse`** — compute `var key = rom.ToString();` once; cache lookups/adds use the content key. `Ability.Parse` now uses `AbilityCache` and the `(Ability?)` downcast is deleted. Race idiom preserved inside `BoundedCache.GetOrAdd`.
- **`ParseCacheTests.cs`** — four behaviors: content-key dedup (distinct backings → same instance), cross-type no-collision (no `InvalidCastException`, correct concrete types both directions), concurrency (1000-way parallel parse → single instance, no throw), and cap bound (`BoundedCache<int>` `Count <= cap`).

## Tasks Completed

| Task | Name | Commit | Files |
| ---- | ---- | ------ | ----- |
| 1 | BoundedCache + re-key caches to content keys, separate per-type | c3050b2 | BoundedCache.cs, CombatLogs.cs, GameObject.cs, Ability.cs, Action.cs, GameObjectTests.cs |
| 2 | ParseCacheTests (dedup, cross-type, concurrency, bound) | 794d107 | ParseCacheTests.cs |

## Verification

- `dotnet build SwtorLogParser/SwtorLogParser.csproj -c Debug` — succeeds, 0 warnings, 0 errors. Core lib stays `IsAotCompatible=true` (no reflection/WinForms/new package).
- `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` — **86 passed, 0 failed, 0 skipped** (82 baseline + 4 new cache tests).
- No `ConcurrentDictionary<int,` and no `rom.GetHashCode()` cache key remain in `CombatLogs.cs` / `GameObject.cs` / `Ability.cs` / `Action.cs`.
- No `InvalidCastException` on any cross-type parse path.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Recharacterized `Game_Objects_Equality_Reflects_Backing_Memory` test**
- **Found during:** Task 1 (full-suite run after the production change)
- **Issue:** This Phase-1 characterization test locked the OLD identity-based contract — two distinct backing strings with identical content were asserted NOT equal because `ReadOnlyMemory<char>.GetHashCode()` is identity-based. RFCT-03 intentionally inverts that contract: under content keys, identical-content roms with different backings now resolve to the SAME cached instance (the ME-02 dedup fix, an explicit must-have). The old `Assert.NotEqual(a, b)` therefore correctly failed.
- **Fix:** Renamed to `Game_Objects_Equality_Reflects_Content_Key` and re-characterized to lock the new contract: same content → same cached instance (`Assert.Same`) regardless of backing memory. No production change driven by this; the test now matches the planned RFCT-03 behavior.
- **Files modified:** SwtorLogParser.Tests/GameObjectTests.cs
- **Commit:** c3050b2

## Known Stubs

None.

## Threat Flags

None — the only new surface (`BoundedCache`) directly mitigates the planned T-03-02 DoS (unbounded cache growth) and introduces no network/auth/file/schema surface.

## Self-Check: PASSED
- FOUND: SwtorLogParser/Caching/BoundedCache.cs
- FOUND: SwtorLogParser.Tests/ParseCacheTests.cs
- FOUND commit c3050b2
- FOUND commit 794d107
