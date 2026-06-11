---
phase: 03-monitor-refactor-coverage
reviewed: 2026-06-11T00:00:00Z
depth: deep
files_reviewed: 11
files_reviewed_list:
  - SwtorLogParser/Monitor/CombatLogsMonitor.cs
  - SwtorLogParser/Monitor/CombatLogs.cs
  - SwtorLogParser/Monitor/ICombatLogSource.cs
  - SwtorLogParser/Caching/BoundedCache.cs
  - SwtorLogParser/Model/GameObject.cs
  - SwtorLogParser/Model/Ability.cs
  - SwtorLogParser/Model/Action.cs
  - SwtorLogParser/Model/Actor.cs
  - SwtorLogParser/View/Entry.cs
  - SwtorLogParser/View/SlidingExpirationList.cs
  - SwtorLogParser.Overlay/View/SlidingExpirationList.cs
findings:
  critical: 1
  warning: 5
  info: 4
  total: 10
status: partially_remediated
remediation:
  remediated_at: 2026-06-11T00:00:00Z
  fixed:
    - id: CR-01
      commit: 1a16b09
      summary: >-
        Guarded Actor.GetId NPC branch against a brace-less actor section
        (missing/inverted '{'/'}' -> null) to stop the ArgumentOutOfRangeException
        reachable via the public Actor.Id surface. Added brace-less (null, no throw)
        and brace-bearing (still parses) NPC id tests.
    - id: WR-03
      commit: 4d334c5
      summary: >-
        Replaced the unsynchronized '??=' lazy PlayerNames init in
        DefaultCombatLogSource with Lazy<ISet<string>>(LoadPlayerNames,
        ExecutionAndPublication), restoring thread-safe one-time init without the
        type-load TypeInitializationException risk and without disturbing the
        ICombatLogSource seam. Serialized the three source-swapping test classes into
        one xUnit collection so the hermetic seam tests stay deterministic after the
        timing change (verified green 10/10 consecutive full runs).
  deferred:
    - id: WR-01
      reason: Soft/eventually-consistent cache bound is acceptable and self-correcting; left for its natural phase.
    - id: WR-02
      reason: Test-robustness / cached-instance identity note; out of scope for this fix pass.
    - id: WR-04
      reason: Dead AOT-unfriendly DI/logging package refs are a Phase 5 dependency-graph cleanup.
    - id: WR-05
      reason: Overlay timer-disposal lifecycle belongs to the overlay/backlog work.
    - id: IN-01
      reason: DateTime.Now monotonicity slated for Phase 4.
    - id: IN-02
      reason: Console.Error vs ILogger routing is pre-existing; informational only.
    - id: IN-03
      reason: out value! null-forgiveness annotation; informational only.
    - id: IN-04
      reason: Source-swap discarding cached PlayerNames is intended behavior; doc-only note.
  test_results:
    passing: 102
    failing: 0
    skipped: 0
    notes: 102 passing / 0 failing / 0 skipped; confirmed stable across 10 consecutive full runs (WR-03 stability).
---

# Phase 3: Code Review Report

**Reviewed:** 2026-06-11T00:00:00Z
**Depth:** deep (cross-file, diff vs f2a0925..249ac08)
**Files Reviewed:** 11
**Status:** issues_found

## Summary

Phase 3 is a well-scoped no-regression hardening pass. The high-risk refactors are sound:

- **RFCT-02 (monitor):** The `Instance`/public-ctor/DpsHps wiring is **byte-identical to the base commit** — the only change in `CombatLogsMonitor.cs` across this range is the visibility bump of `Accumulator`/`CalculateDpsHpsStats` from `private` to `internal`. The Rx expression is unchanged and remains single-subscription-correct (cold observable; each `Subscribe` builds its own chain). No regression.
- **RFCT-03 (per-type caches):** The content-keying (`rom.ToString()`) plus separate `BoundedCache<GameObject>`/`<Ability>`/`<Action>` genuinely eliminates the prior cross-type cast bug (old `Ability.Parse` cast a `GameObject` instance out of the shared `GameObjectCache` via `(Ability?)value`, an `InvalidCastException` waiting to happen). Confirmed fixed.
- **View dedup (RFCT-01):** The Overlay still owns its display-projection `Entry` (`Name`/`DPS`/`DCrit`/...) bound by `ParserForm`; the core `Entry` is a separate minimal type. The DataGridView binding is preserved. No regression.
- **Filesystem seam:** `Directory.Exists` guards make type-load CI-safe; behavior for hosts with the real folders present is preserved.

Concerns: one **BLOCKER** — the flagged `Actor.GetId()` NPC unguarded-slice crash is real, reachable through the public `Actor.Id` surface (NPC targets), and uncovered by tests. Several **WARNINGs** around `BoundedCache` being a best-effort (not hard) bound under concurrency, the lazy-`PlayerNames` data race replacing a previously thread-safe eager init, and stale AOT-incompatible package references the Phase-3 comments claim were moved host-side.

---

## Remediation (2026-06-11)

**Status:** partially remediated — the **BLOCKER** and the thread-safety regression are fixed; the remaining warnings/info are deferred to their natural phases.

**Fixed:**

- **CR-01** (commit `1a16b09`): `Actor.GetId()` NPC branch now guards `openIndex`/`closeIndex` (missing or inverted `{`/`}` -> `null`), eliminating the `ArgumentOutOfRangeException` reachable via `Actor.Id` on a brace-less NPC. Tests added: brace-less NPC -> `Id == null` and no throw; brace-bearing NPC still parses its id. Valid-actor behavior unchanged.
- **WR-03** (commit `4d334c5`): `DefaultCombatLogSource.PlayerNames` lazy init moved from an unsynchronized `??=` to `Lazy<ISet<string>>(LoadPlayerNames, LazyThreadSafetyMode.ExecutionAndPublication)`, restoring thread-safe one-time init without the static-ctor `TypeInitializationException` risk and without disturbing the injectable `ICombatLogSource` seam (`SetSource`/`ResetSource`) or `InMemoryCombatLogSource`. `Directory.Exists` guarding preserved. The `Lazy` ctor-time allocation shifted timing enough to expose a pre-existing parallelism race in the three test classes that mutate the shared static `CombatLogs._source`; they are now serialized into one xUnit collection (`CombatLogsSourceCollection`) so the hermetic seam tests stay deterministic. Core lib remains `IsAotCompatible=true` (no reflection/DI/WinForms/new package). A focused concurrency unit test was considered but not added: `Lazy<T>` thread-safety is a framework guarantee, and a hand-rolled race test would be non-deterministic — the collection serialization plus 10 consecutive green full runs cover the regression deterministically.

**Deferred (left for their natural phases):** WR-01 (soft cache bound — acceptable/self-correcting), WR-02 (test robustness / cached-instance identity), WR-04 (dead AOT-unfriendly package refs — Phase 5), WR-05 (overlay timer disposal — overlay/backlog), IN-01 (`DateTime.Now` monotonicity — Phase 4), IN-02/IN-03/IN-04 (informational).

**Test suite after remediation:** 102 passing / 0 failing / 0 skipped, stable across 10 consecutive full `dotnet test` runs.

---

## Critical Issues

### CR-01: `Actor.GetId()` NPC branch throws ArgumentOutOfRangeException on a brace-less NPC actor

**File:** `SwtorLogParser/Model/Actor.cs:109-114`
**Issue:**
```csharp
if (IsNpc)
{
    var openIndex  = Roms[0].Span.IndexOf('{');  // -1 when no '{'
    var closeIndex = Roms[0].Span.IndexOf('}');  // -1 when no '}'
    return long.TryParse(Roms[0].Span.Slice(openIndex + 1, closeIndex - openIndex - 1), out var id) ? id : (long?)null;
}
```
For a brace-less NPC actor section, `openIndex == closeIndex == -1`, so the call becomes `Slice(0, -1)` — a negative length — which throws `ArgumentOutOfRangeException`. Unlike `GetName()` (lines 43-60), `GetId()` is **not** wrapped in try/catch, so the exception propagates to the caller of `Actor.Id`.

Reachability:
- **Not** reachable on the live DPS/HPS Rx path: that path only forces `.Id` on `Player`, which is always `IsPlayer == true` (the `IsPlayerDamage`/`IsPlayerHeal` filters in `CombatLogLineExtensions.cs` gate on `Source.IsPlayer`), so the NPC branch is never taken there. This is why live overlay parsing does not currently crash.
- **Reachable** through the public `Actor.Id` surface for any NPC actor — most directly `CombatLogLine.Target.Id`, since NPC targets occur on essentially every player-damages-NPC line. Any host/consumer that reads `Target.Id` (or `Source.Id` for an NPC source line) on a brace-less NPC will crash. `Actor.Id` is public core-library API, so this is a latent defect in the shipped surface, not a private detail.
- The contrast with the Companion (97-99) and Player (104-106) branches is that those formats reliably contain `{`/`#`; the NPC format does not guarantee a `{id}`.

Existing `ActorTests` cover NPC ids **with** braces and with non-numeric brace content (returns null), but **no** test covers a brace-less NPC line, so the crash is uncovered.

**Severity rationale:** BLOCKER — unhandled exception / crash reachable from public API on common real-world input (NPC target without an id), and it is a latent regression-class defect the phase explicitly flagged for assessment.

**Fix:** Guard the indices before slicing (mirror the `GameObject.GetId` pattern, which already checks `startIndex != -1 && endIndex != -1`):
```csharp
if (IsNpc)
{
    var openIndex  = Roms[0].Span.IndexOf('{');
    var closeIndex = Roms[0].Span.IndexOf('}');
    if (openIndex == -1 || closeIndex == -1 || closeIndex <= openIndex) return null;
    return long.TryParse(Roms[0].Span.Slice(openIndex + 1, closeIndex - openIndex - 1), out var id) ? id : (long?)null;
}
```

---

## Warnings

### WR-01: BoundedCache is a best-effort (soft) bound, not a hard cap, under concurrency

**File:** `SwtorLogParser/Caching/BoundedCache.cs:37-50`
**Issue:** `GetOrAdd` performs `_map.TryAdd(key, value)` and then, in a separate non-atomic step, `_order.Enqueue(key)` followed by the eviction `while`. A thread can succeed at `TryAdd` and be preempted before `Enqueue`. If many threads are in that window simultaneously, `_map.Count` stays above `_capacity` while `_order` can be drained empty by the eviction loops of other threads — the `while (_map.Count > _capacity && _order.TryDequeue(...))` then exits because `TryDequeue` returns `false`, leaving `_map.Count > _capacity` until later inserts re-trigger eviction. So the cap is eventually-consistent, not guaranteed. The `Cache_Is_Bounded` test only proves the bound single-threaded (`Parallel` is used only in `Concurrent_Parse_Is_Safe`, which shares one key and never floods the cap). For the production cap of 4096 under realistic log-tail rates this is unlikely to be a memory problem, but the "Bounded ... never exceeds cap" guarantee in the XML doc and the DoS-mitigation framing (T-03-02) overstate the actual contract.
**Severity rationale:** WARNING — bound is honored in steady state; transient overshoot does not leak permanently and is not a correctness bug, but the documented invariant is stronger than the code provides.
**Fix:** Either (a) document it as a soft/eventually-consistent bound, or (b) serialize the add+enqueue+evict trio under a lock (the per-call cost is negligible vs. the surrounding parse work), making the cap strict.

### WR-02: BoundedCache eviction can drop a still-live (recently-used) entry — instance identity is not lifetime-stable

**File:** `SwtorLogParser/Caching/BoundedCache.cs:44-45`
**Issue:** FIFO eviction removes the oldest *inserted* key regardless of whether it is actively in use, so a hot key can be evicted and then re-`Parse`d into a brand-new instance. The XML doc and the `Content_Key_Dedups_Identical_Content` test assert `Assert.Same` identity, but that identity only holds until the key is evicted. Any caller relying on reference identity across the process lifetime (rather than `Equals`/`GetHashCode`) would observe a behavior change vs. the old unbounded `ConcurrentDictionary`, which never evicted. Today's consumers compare via `Equals`/`GetHashCode` (content/Rom based), so this is not a live bug, but it is a semantic narrowing worth flagging.
**Severity rationale:** WARNING — acknowledged FIFO-not-LRU design, but the identity guarantee implied by the tests/docs does not survive eviction; a future consumer could be surprised.
**Fix:** Document explicitly that cached-instance identity is only stable while the key remains resident (not evicted), and that consumers must not depend on reference identity.

### WR-03: Lazy `PlayerNames` initialization is an unsynchronized data race and a behavior change from the prior thread-safe eager init

**File:** `SwtorLogParser/Monitor/CombatLogs.cs:76,85`
**Issue:** `public ISet<string> PlayerNames => _playerNames ??= LoadPlayerNames();` uses a non-volatile instance field with a non-atomic `??=`. Under concurrent first access two threads can both observe `null`, both run `LoadPlayerNames()`, and produce two distinct `HashSet` instances; callers in that window may receive different set instances (equal content, so `IsLocalPlayer` results are consistent, but the object identity differs and the file IO runs twice). The prior implementation (base commit) computed `PlayerNames` in the **static constructor**, which the CLR serializes under the type-initialization lock — i.e., it was thread-safe by construction. This refactor trades that guarantee for a benign-but-real race.
**Severity rationale:** WARNING — content is identical so no incorrect parsing result, but it is a concurrency regression vs. the prior thread-safe init and causes redundant filesystem enumeration.
**Fix:** Back it with `Lazy<ISet<string>>(LoadPlayerNames, LazyThreadSafetyMode.ExecutionAndPublication)`, or guard the assignment with a lock / `Interlocked.CompareExchange`.

### WR-04: Core library still references AOT/trim-unfriendly DI + logging-provider packages the Phase-3 comments claim were moved host-side

**File:** `SwtorLogParser/SwtorLogParser.csproj:15-17`
**Issue:** The csproj sets `IsAotCompatible=true` and the new comments in `CombatLogsMonitor.cs:14-17` and `ICombatLogSource.cs:9` assert "console/debug logging providers move host-side ... no reflection/DI container here." But the project still `PackageReference`s `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging.Console`, and `Microsoft.Extensions.Logging.Debug`. A grep confirms **no core code uses them** (only `Microsoft.Extensions.Logging.Abstractions` / `NullLogger` are used). These providers rely on runtime reflection/config binding and are exactly the trim/AOT-warning sources the comments claim were removed. Leaving them referenced contradicts the stated AOT-hardening intent and inflates the dependency graph (and any future AOT publish would surface their trim warnings).
**Severity rationale:** WARNING — AOT-hygiene/quality defect; the package references are dead and contradict the documented invariant, though they cause no runtime bug today.
**Fix:** Remove the three unused package references from `SwtorLogParser.csproj`, leaving only `Logging.Abstractions` and `System.Reactive`. Move Console/Debug logging providers and DI wiring to the host projects (Overlay/CLI) as the comments claim.

### WR-05: Overlay render `Redraw` -> `_control.Invoke` can throw on a disposed control during teardown; no timer disposal / no host-thread guard

**File:** `SwtorLogParser.Overlay/View/SlidingExpirationList.cs:28,31-33`
**Issue:** `_renderTimer` fires `Redraw` on a threadpool thread which calls `_control.Invoke(Refresh)`. Neither the adapter's `_renderTimer` nor the core list's `_expirationTimer` is ever disposed (the class is not `IDisposable`), so both keep firing after the form/control is disposed. `Control.Invoke` on a disposed/handle-destroyed control throws `ObjectDisposedException`/`InvalidOperationException` on the timer thread (unobserved). The old Overlay had the same un-disposed-timer shape, so this is a pre-existing smell rather than a new regression, but the refactor added a second always-on timer (core `_expirationTimer`) without lifecycle ownership, widening the window.
**Severity rationale:** WARNING — pre-existing pattern, no crash during normal running, but unobserved teardown exceptions and two leaked `Timer`s per list instance.
**Fix:** Make both `SlidingExpirationList` types `IDisposable`, dispose the timers in `Dispose()`, and have `ParserForm` dispose `_list` on form close. In `Redraw`, guard with `if (_control.IsDisposed || !_control.IsHandleCreated) return;` and swallow `ObjectDisposedException` from `Invoke`.

---

## Info

### IN-01: Core `SlidingExpirationList` uses `DateTime.Now` for expiry timestamps (locale/DST sensitivity)

**File:** `SwtorLogParser/View/SlidingExpirationList.cs:53,64`
**Issue:** Expiration uses `DateTime.Now` (wall clock). A DST transition or clock adjustment can make entries expire early/late or stick. `Stopwatch`/`Environment.TickCount64`/`DateTime.UtcNow` would be monotonic-safe. This matches the prior overlay behavior (no regression), so it is informational.
**Fix:** Prefer a monotonic time source for sliding-window bookkeeping.

### IN-02: `Action.Parse` swallows constructor exceptions to `Console.Error` instead of logging

**File:** `SwtorLogParser/Model/Action.cs:58-61`
**Issue:** A malformed action with a `:` that fails `Event`/`Effect` parsing throws inside the ctor, is caught, written to `Console.Error`, and `null` returned. In a WinForms/AOT host `Console.Error` may be unattached, silently dropping the diagnostic. Pre-existing behavior, not introduced this phase.
**Fix:** Route through the injected `ILogger` (or surface a structured parse-failure) rather than `Console.Error`.

### IN-03: `BoundedCache.TryGetValue` exposes `out value!` null-forgiveness for reference TValue

**File:** `SwtorLogParser/Caching/BoundedCache.cs:31`
**Issue:** On a miss, `out value!` yields `default(TValue)` (null for reference types) while the `!` suppresses the nullable warning for callers. Callers (`GameObject`/`Ability`/`Action` Parse) only read `value` after the method returns `true`, so this is safe today, but the suppressed annotation could mask a future caller that reads on `false`.
**Fix:** Consider `[MaybeNullWhen(false)] out TValue value` to encode the contract instead of `!`.

### IN-04: `CombatLogs.ResetSource()` allocates a fresh `DefaultCombatLogSource`, discarding the cached `PlayerNames`

**File:** `SwtorLogParser/Monitor/CombatLogs.cs:28`
**Issue:** Test-seam `ResetSource()` replaces `_source` with a new instance, so the lazily-loaded `_playerNames` cache is dropped and re-enumerated on next access. Harmless for tests, but worth noting that source swapping resets all per-source lazy state — intended, just undocumented.
**Fix:** None required; optionally note in the XML doc that swapping the source discards cached player names.

---

_Reviewed: 2026-06-11T00:00:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: deep_
