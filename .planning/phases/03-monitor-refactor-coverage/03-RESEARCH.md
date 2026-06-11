# Phase 3: Monitor Refactor + Coverage - Research

**Researched:** 2026-06-11
**Domain:** .NET 8 refactor (DI/AOT, concurrent caching, Rx.NET testing, WinForms/UI-agnostic dedup, hermetic filesystem tests)
**Confidence:** HIGH (all claims grounded in the current post-Phase-2 source; 77 tests verified green this session)

## Summary

This phase is a behavior-preserving refactor of the core library (`SwtorLogParser`, `net8.0`, `IsAotCompatible=true`) plus new test coverage. Every claim below is verified against the actual source as it stands today (Phase 2 complete). The codebase is small and the four refactors are largely independent, which makes them safe to sequence as separate waves with `dotnet test` (77 tests) as the regression gate after each.

The four refactors: (RFCT-02) replace the `#if RELEASE/#elif DEBUG` `Instance` property — which leaves `Instance` *undefined* in any build configuration not literally named `RELEASE` or `DEBUG` — with an unconditional `NullLogger`-backed singleton plus a public `ILogger`-taking constructor for tests; (RFCT-03) re-key the two static parse caches from `ReadOnlyMemory<char>.GetHashCode()` (reference+index+length, NOT content) to content keys, bound them, and fix the latent `Ability`-vs-`GameObject` shared-cache type collision; (RFCT-01) collapse three duplicated `View/Entry.cs` + `View/SlidingExpirationList.cs` into one UI-agnostic core type, leaving only the Overlay's `BindingList`/`DataGridView` adapter host-side; (TEST-01/02) add monitor-lifecycle/Rx and DPS-HPS math tests using the existing `InternalsVisibleTo(SwtorLogParser.Tests)` seam, and make two non-hermetic filesystem tests deterministic.

**Primary recommendation:** Sequence as RFCT-02 (smallest, unblocks DI-construction for tests) → RFCT-03 (cache, isolated to `Model/*` + `CombatLogs`) → RFCT-01 (view dedup, host-facing) → TEST-01/02 (depends on the RFCT-02 ctor + a filesystem seam). The single biggest landmine is the `DpsHps` pipeline's dependence on `DateTime.Now` (line 51 `Where`, line 71 accumulator) — Rx/math tests must inject or tolerate wall-clock time WITHOUT pulling Phase 4's accumulator perf rewrite (PERF-03) forward.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Monitor construction / lifecycle (RFCT-02) | Core lib (`SwtorLogParser.Monitor`) | Hosts (consume `Instance`) | Singleton producer lives in core; hosts only consume the `DpsHps` seam |
| Parse caches (RFCT-03) | Core lib (`Model/*` + `CombatLogs`) | — | Pure parsing concern; no host or UI involvement |
| Sliding-expiration list, UI-agnostic core (RFCT-01) | Core lib (new view namespace) | — | Pure data/time logic; shared by all 3 hosts |
| `DataGridView`/`BindingList` binding (RFCT-01) | Overlay host (`net8.0-windows`) | — | WinForms types MUST NOT enter the AOT core lib |
| Monitor/Rx tests (TEST-01) | Test project | Core lib (internal seam) | Uses `InternalsVisibleTo` + public ctor |
| DPS/HPS math tests (TEST-02) | Test project | Core lib (internal seam) | Tests `Accumulator` + `CalculateDpsHpsStats` |
| Filesystem abstraction (TEST seam) | Core lib (`CombatLogs`) | Test project (fixtures) | `PlayerNames`/log enumeration must become injectable |

## User Constraints (from CONTEXT.md)

### Locked Decisions

**DI & construction (RFCT-02)**
- Add a public constructor to `CombatLogsMonitor` taking `ILogger<CombatLogsMonitor>` (constructor injection) for DI/testing. Keep a lazily-initialized static `Instance` DEFINED IN ALL BUILD CONFIGURATIONS — remove the `#if RELEASE/#elif DEBUG` gap that currently leaves `Instance` undefined in other configs; add a default branch. Hosts keep using `Instance`; tests use the public ctor.
- Plain constructor injection only — NO reflection and NO DI container inside the core library. The core lib stays `IsAotCompatible=true`. (Hosts MAY use MS.DI if they want, but the core lib must not require it.)
- The default `Instance` uses `NullLogger<CombatLogsMonitor>`; console/debug logger providers stay host-side; behavior of the default singleton is unchanged from today's intent, just defined everywhere.

**Cache redesign (RFCT-03)**
- Key the parse caches by string content (`rom.ToString()`) instead of `ReadOnlyMemory<char>.GetHashCode()`. Accept the span→string allocation as the correctness cost.
- Bound the caches: a size cap with simple eviction (oldest/LRU). Pick a reasonable cap.
- Keep thread-safety via `ConcurrentDictionary` (Phase 2) plus the bound.
- FIX the latent GameObject/Ability shared-cache type bug: make the content key type-correct (or use separate caches per type).

**View dedup (RFCT-01)**
- `Entry` and `SlidingExpirationList` end up in EXACTLY ONE location: a UI-agnostic core in the core library. All three hosts reference that single copy; per-host duplicate `View/` files are removed.
- Extract the UI-AGNOSTIC sliding-expiration core into the lib; keep the WinForms `DataGridView` binding adapter in the Overlay project ONLY.
- AOT constraint: NO WinForms types (DataGridView, BindingList, etc.) may enter the AOT core library.

**Test coverage (TEST-01, TEST-02)**
- Monitor/Rx tests use the new DI constructor plus a testable seam to push `CombatLogLine`s into the internal Subject (enabled by `InternalsVisibleTo(SwtorLogParser.Tests)`). Assert: after Start the Subject delivers lines; after Stop it stops. Cover the Phase 2 cancellation wiring.
- DPS/HPS math: unit-test the accumulator and `CalculateDpsHpsStats` against known `CombatLogLine` inputs — assert DPS, HPS, crit%, and the 10s sliding-window expiry behavior.
- Abstract the filesystem access in `CombatLogs` (a seam/interface for the logs directory + settings files) so `All_Logs_Are_Not_Null` and `Player_Is_Local_Is_True` become hermetic.

### Claude's Discretion
- Exact cache cap size and eviction policy details, the precise shape of the testable seam (internal method vs internal-visible Subject), the filesystem-abstraction interface shape, and the namespace/folder for the shared view types — guided by AOT-safety, keeping the suite green, and not pulling Phase 4/5 work forward.

### Deferred Ideas (OUT OF SCOPE)
- Overlay-topmost fix (BACKLOG BL-01) — only touch if trivially adjacent to RFCT-01 view work.
- Accumulator full re-scan/re-sort perf (PERF-03) → Phase 4. RFCT-03 changes cache design, NOT the accumulator hot path.
- Dependency GA upgrades (Phase 5), CI (Phase 6), INFRA-02 Docker target removal (Phase 5).

## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| RFCT-01 | `Entry`/`SlidingExpirationList` in one shared core location consumed by all 3 hosts | View Dedup Diff section: CLI + Native.Cli variants are byte-identical (UI-free); Overlay variant is a `BindingList<Entry>` adapter. Shared core = data-only sliding list; Overlay keeps its adapter. |
| RFCT-02 | Monitor constructible in any build config; DI over hard-coded singleton | RFCT-02 section: exact `#if` replacement; `_logger` non-null fix; public ctor; AOT-safe (no MS.DI in core). |
| RFCT-03 | Content-keyed, bounded caches; no shared-cache type bug | RFCT-03 section: exact current cache code, the `(Ability?)value` cast collision, bounded-cache options, `rom.ToString()` allocation tradeoff. |
| TEST-01 | Cover monitor lifecycle (start/stop/cancellation) + Rx pipeline | TEST section: internal `Push`/`OnNext` seam, `IsRunning`, Stop cancellation, `DateTime.Now` window landmine. |
| TEST-02 | Cover DPS/HPS math | TEST section: deterministic `Accumulator` + `CalculateDpsHpsStats` testing strategy, known-input lines, 10s window. |

## Standard Stack

No new runtime packages are required for the locked scope. Everything is achievable with the BCL + the packages already referenced.

### Core (already present)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.Reactive | 6.0.1-preview.1 | `Subject<CombatLogLine>`, `IObservable<PlayerStats>` pipeline | Already the core seam; tests subscribe to it [VERIFIED: SwtorLogParser.csproj] |
| Microsoft.Extensions.Logging.Abstractions | (transitive via Console/Debug) | `ILogger<T>`, `NullLogger<T>` for the default `Instance` | `NullLogger<T>.Instance` is AOT-safe, zero-cost [CITED: learn.microsoft.com NullLogger] |
| `System.Collections.Concurrent.ConcurrentDictionary` | BCL | Thread-safe cache backing | Already used (Phase 2); AOT-safe |
| xUnit | 2.5.0-pre.44 | Test framework | Existing 77-test suite [VERIFIED: Tests.csproj] |

### Supporting (decisions, no install)
| Concern | Approach | When to Use |
|---------|----------|-------------|
| Bounded cache | Hand-rolled bounded `ConcurrentDictionary` with insertion-order eviction, OR sharded cap (see RFCT-03 options) | Locked: "simple eviction", cap at Claude's discretion |
| Rx test timing | Subscribe + collect on the real (immediate) scheduler; the `Subject` is synchronous, so `OnNext` delivers on the calling thread | TEST-01/02 — no `TestScheduler` needed for the Subject itself |
| Wall-clock in window filter | See "Pitfall: DateTime.Now" — use timestamps relative to `DateTime.Now` in test inputs, OR test `Accumulator`/`CalculateDpsHpsStats` directly (bypassing the `Where(... DateTime.Now ...)`) | TEST-02 determinism |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Hand-rolled bounded cache | `BitFaster.Caching` `ConcurrentLru` | Adds a dependency (against Phase-5-defers-deps spirit); is AOT-friendly and battle-tested, but the cache here is tiny and bounded growth is the only requirement — a new dep is overkill. **Recommend hand-rolled for this phase.** [CITED: github.com/bitfaster/BitFaster.Caching] |
| Hand-rolled bounded cache | `Microsoft.Extensions.Caching.Memory` `MemoryCache` | Heavier (eviction timers, `object` boxing, size-callback ceremony); designed for app-level caching not hot-path parse interning. Overkill + allocation. |
| `Microsoft.Reactive.Testing` `TestScheduler` | Direct `Subject.OnNext` + `ToList()` subscribe | The pipeline's only time dependency is `DateTime.Now` (not an Rx scheduler), so `TestScheduler` does NOT help with the window filter. Direct subscription is simpler. |

**Installation:** None required. If `BitFaster.Caching` were chosen (NOT recommended this phase): `dotnet add SwtorLogParser package BitFaster.Caching` — but defer to Phase 5 dependency review.

## Package Legitimacy Audit

No new packages are being installed for the locked scope. The only package *mentioned* as an alternative is documented for completeness and is NOT recommended for this phase.

| Package | Registry | Age | Downloads | Source Repo | Verdict | Disposition |
|---------|----------|-----|-----------|-------------|---------|-------------|
| BitFaster.Caching | NuGet | mature (>5 yrs) | high | github.com/bitfaster/BitFaster.Caching | OK (well-known) | NOT used — documented alternative only |

**Packages removed due to [SLOP] verdict:** none
**Packages flagged as suspicious [SUS]:** none

*No `package-legitimacy check` run because no install is planned. If the planner elects to add `BitFaster.Caching` (against the recommendation), gate it behind a `checkpoint:human-verify` task and run the legitimacy gate first.*

## Architecture Patterns

### System Architecture Diagram (post-refactor data flow)

```
                         ┌─────────────────────────────────────────────┐
                         │   CombatLogsMonitor  (core lib, singleton    │
                         │   Instance = NullLogger;  public(ILogger)    │
                         │   ctor for tests)                            │
   log file ──MonitorAsync──▶ _lastFileName                            │
   (filesystem            │       │                                    │
    via CombatLogs        │   ReadAsync ──CombatLogLine.Parse──▶       │
    seam) ◀───────────────┤       │                                    │
                         │       ▼                                    │
                         │   Subject<CombatLogLine> ──(internal Push   │
                         │       │  seam for tests)                    │
                         │       ▼  ConfigureObservables               │
                         │   .Where(TimeStamp > Now-10s)  ◀── DateTime.Now landmine
                         │   .GroupBy(Source.Name)                     │
                         │   .Scan(Accumulator)  ◀── 10s window, lock  │
                         │   .Select(CalculateDpsHpsStats)             │
                         │       │                                    │
                         │   IObservable<PlayerStats> DpsHps  ─────────┼──┐
                         └─────────────────────────────────────────────┘  │
                                                                           │ subscribe
        ┌──────────────────────┬──────────────────────────┬───────────────┘
        ▼                      ▼                          ▼
   CLI host              Native.Cli host            Overlay host (net8.0-windows)
   SlidingExpirationList SlidingExpirationList       SlidingExpirationList : BindingList<Entry>
   (SHARED core type)    (SHARED core type)          (host adapter, wraps/derives shared core)
        │                      │                          │
   TableView render       Console render             DataGridView.DataSource

   Caches (core lib, RFCT-03):
   GameObject.Parse / Ability.Parse ─▶ content-keyed, bounded, type-correct cache(s)
   Action.Parse ─────────────────────▶ content-keyed, bounded ActionCache
```

### Recommended Project Structure (additions only)
```
SwtorLogParser/
├── Monitor/
│   ├── CombatLogsMonitor.cs   # RFCT-02 edit; internal Push seam for TEST-01
│   ├── CombatLogs.cs          # RFCT-03 cache fields; TEST filesystem seam
│   └── ICombatLogsSource.cs   # NEW (discretion): filesystem abstraction for PlayerNames + log enumeration
├── Model/
│   ├── GameObject.cs / Ability.cs / Action.cs   # RFCT-03 Parse() cache edits
│   └── ...
├── View/                      # NEW shared location (discretion namespace, e.g. SwtorLogParser.View)
│   ├── Entry.cs               # UI-agnostic DTO (Stats + Expiration)
│   └── SlidingExpirationList.cs  # UI-agnostic data list (no WinForms)
└── Caching/
    └── BoundedCache.cs        # NEW (discretion): small bounded ConcurrentDictionary wrapper

SwtorLogParser.Overlay/
└── View/
    └── BindingSlidingExpirationList.cs  # KEEP host-side: BindingList<Entry> + DataGridView refresh adapter
```

### Pattern 1: Unconditional singleton + DI ctor (RFCT-02)
**What:** One `Instance` defined in every build config; a public ctor for injection.
**When to use:** Replace the `#if RELEASE/#elif DEBUG` block at `CombatLogsMonitor.cs:14-23`.
```csharp
// Source: derived from current CombatLogsMonitor.cs (lines 10-46, 110-114)
public class CombatLogsMonitor
{
    private readonly ILogger<CombatLogsMonitor> _logger;

    // Unconditional: defined in ALL configs (fixes the undefined-Instance gap).
    public static CombatLogsMonitor Instance { get; } =
        new(NullLogger<CombatLogsMonitor>.Instance);

    // PUBLIC for DI/tests (was private). _logger now always assigned (fixes CS8618).
    public CombatLogsMonitor(ILogger<CombatLogsMonitor> logger)
    {
        _logger = logger;
        ConfigureObservables();   // was reached via the private parameterless ctor `: this()`
    }
}
```
Notes:
- The current private parameterless ctor (line 43) only calls `ConfigureObservables()`. Folding it into the `ILogger` ctor (or keeping it private and chaining) is fine. Either way `_logger` and `DpsHps` are always assigned, silencing the two CS8618 warnings observed this session.
- `NullLogger<CombatLogsMonitor>.Instance` is AOT-safe (no reflection). Hosts that want console logging can construct their own `CombatLogsMonitor(consoleLogger)` and pass it (the Overlay's `ParserForm` ALREADY takes a `CombatLogsMonitor` via ctor — `ParserForm.cs:20`), but the locked decision keeps hosts on `Instance` for now.

### Pattern 2: Type-correct, content-keyed, bounded cache (RFCT-03)
**What:** Separate caches per concrete type, keyed by string content, with a size cap.
**When to use:** `GameObject.Parse`, `Ability.Parse`, `Action.Parse`.
```csharp
// Source: derived from CombatLogs.cs:8-10 + GameObject.cs:101-115 + Ability.cs:10-26
// Option A (recommended): separate caches per concrete type — eliminates the cast collision entirely.
internal static readonly BoundedCache<string, GameObject> GameObjectCache = new(cap: 4096);
internal static readonly BoundedCache<string, Ability>    AbilityCache    = new(cap: 4096);
internal static readonly BoundedCache<string, Action>     ActionCache     = new(cap: 4096);

// In GameObject.Parse:
var key = rom.ToString();                       // content key (allocation accepted, locked)
if (GameObjectCache.TryGetValue(key, out var go)) return go;
var gameObject = new GameObject(rom);
if (gameObject.Id == null) return null;
return GameObjectCache.GetOrAdd(key, gameObject);
```

### Pattern 3: Internal Push seam for Rx tests (TEST-01)
**What:** Expose an internal way to feed `CombatLogLine`s into the private `Subject` without the filesystem.
```csharp
// Source: new internal seam on CombatLogsMonitor (InternalsVisibleTo already present)
internal void Push(CombatLogLine line) => CombatLogLines.OnNext(line);
// Test:
var monitor = new CombatLogsMonitor(NullLogger<CombatLogsMonitor>.Instance);
var received = new List<CombatLogsMonitor.PlayerStats>();
using var sub = monitor.DpsHps.Subscribe(received.Add);
monitor.Push(ParseLine("...damage line with TimeStamp ~= now..."));
// assert received reflects the pushed line
```
Note: `Subject<T>.OnNext` is synchronous — it delivers on the calling thread before returning, so no scheduler pumping is needed for the Subject itself.

### Anti-Patterns to Avoid
- **Putting `BindingList`/`DataGridView`/`Control` in the core lib** — breaks `IsAotCompatible` and `net8.0` (these are WinForms / `net8.0-windows` only). The shared `SlidingExpirationList` must be data-only.
- **Re-keying caches by `GetHashCode()` of the new string** — defeats the purpose; key by the *string itself* (`ConcurrentDictionary<string, T>`), let the dictionary hash it with content semantics.
- **Casting `(Ability?)value` from a shared cache** (current `Ability.cs:16`) — if a `GameObject` (non-`Ability`) was cached under that content key, this throws `InvalidCastException`. Separate caches eliminate it.
- **Rewriting `Accumulator`/`CalculateDpsHpsStats` for performance** — that is PERF-03 / Phase 4. Tests must lock the CURRENT behavior, not improve it.
- **Touching `DateTime.Now` call sites to inject a clock** — tempting for testability, but that is a behavior-shaping change; the locked decision says keep behavior identical and not pull perf work forward. Prefer testing the math methods directly or using now-relative timestamps (see Pitfall 1).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Default no-op logger | A custom empty `ILogger` | `NullLogger<CombatLogsMonitor>.Instance` | BCL-provided, AOT-safe, allocation-free singleton |
| Thread-safe dictionary | `Dictionary` + manual `lock` | `ConcurrentDictionary` (already in use) | Phase 2 already did this; keep it |
| Synchronous test pump for Rx | A custom scheduler | Direct `Subject.OnNext` + `Subscribe(list.Add)` | `Subject<T>` is synchronous; no scheduler needed |
| Sliding-window list (UI-agnostic core) | (this IS the thing being de-duplicated) | One shared core type | The point of RFCT-01 — write it once |

**Caveat — bounded cache IS borderline hand-rolling.** A correct concurrent LRU is genuinely tricky (the c-sharpcorner and BitFaster references show the subtleties). BUT the locked decision explicitly accepts *simple* eviction and a *reasonable cap*, and the cache here is a parse-interning cache (correctness does not depend on perfect LRU recency). A minimal bounded `ConcurrentDictionary` that clears or trims when it exceeds the cap is acceptable and avoids a new dependency in a phase that defers dep changes to Phase 5. Document the eviction policy chosen. If true LRU semantics are later required, adopt `BitFaster.Caching.ConcurrentLru` in Phase 5.

**Key insight:** This phase's value is in NOT changing behavior. Every "smart" abstraction added here is a regression risk against a live DPS stream that has no integration test. Keep additions minimal and locked-behavior-preserving.

## Runtime State Inventory

> This is a refactor phase, so the inventory applies. The refactor is in-process only — no external/runtime state is renamed or migrated.

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | None — caches are in-memory `static` only; no persisted keys, DBs, or files written by the parser. The app reads logs but writes nothing. | None |
| Live service config | None — no external services; no UI-stored config. SWTOR `.txt`/`.ini` files are read-only inputs, not written. | None |
| OS-registered state | None — no scheduled tasks, services, or registered process names. `user32.dll` P/Invoke is overlay-only and unaffected. | None |
| Secrets/env vars | None — STACK.md confirms no env vars, `.env`, `appsettings.json`, or secrets are read. | None |
| Build artifacts | The three host `View/` `.cs` files are DELETED by RFCT-01 — `obj/`/`bin/` for all hosts + tests must rebuild. Namespaces `SwtorLogParser.Cli.View`, `.Native.Cli.View`, `.Overlay.View` change to the shared namespace; any `using` referencing them updates. | Delete duplicate files; update `using`s in `Program.cs` (CLI line 5, Native.Cli line 4) and `ParserForm.cs` (line 2); full rebuild |

**Nothing found in categories Stored data / Live service config / OS-registered state / Secrets:** verified by reading STACK.md ("No environment variables or external configuration files are read") and grepping the core lib for any write/persist path (only reads, in-memory caches).

## Common Pitfalls

### Pitfall 1: `DpsHps` window filter depends on `DateTime.Now` (the central landmine)
**What goes wrong:** `ConfigureObservables` line 51 filters `.Where(x => x.TimeStamp > DateTime.Now.AddSeconds(-10))`, and `Accumulator` line 71 removes lines older than `combatLog.TimeStamp.AddSeconds(-10)`. A test that pushes a `CombatLogLine` parsed from a fixed literal timestamp like `[20:33:17.759]` will be silently dropped by the `Where` filter because that wall-clock time is not within the last 10 seconds of "now".
**Why it happens:** SWTOR log timestamps are time-of-day (`HH:mm:ss[.fff]`), and the filter compares them to the live clock.
**How to avoid (two complementary strategies):**
1. **Math tests (TEST-02) — test the methods directly.** `Accumulator` and `CalculateDpsHpsStats` are private but reachable via `InternalsVisibleTo` (make them `internal`, or test through an internal seam). Build a `HashSet<CombatLogLine>` of known lines and call `CalculateDpsHpsStats(state)` directly — this bypasses the `DateTime.Now` `Where` entirely and is fully deterministic. The 10s expiry in `Accumulator` is tested by passing lines with controlled relative `TimeStamp`s and asserting which survive.
2. **Pipeline tests (TEST-01) — use now-relative timestamps.** When asserting the full `DpsHps` stream delivers, construct log lines whose timestamp section is `DateTime.Now.ToString("HH:mm:ss.fff", InvariantCulture)` so they pass the `> Now-10s` filter. Accept a small tolerance; do NOT assert exact DPS numbers in the time-dependent pipeline test (assert "a `PlayerStats` was delivered" / player identity), leave numeric assertions to the direct math test.
**Warning signs:** A pipeline test that pushes a line and gets zero `PlayerStats` — almost always the `DateTime.Now` filter dropped it.
**Do NOT** introduce an `IClock`/`TimeProvider` abstraction to fix this — that reshapes behavior and is outside the locked scope (and arguably Phase 4 territory). [VERIFIED: CombatLogsMonitor.cs:51,71]

### Pitfall 2: `Ability`/`GameObject` shared-cache `InvalidCastException`
**What goes wrong:** `Ability.Parse` (`Ability.cs:15-16`) and `GameObject.Parse` (`GameObject.cs:103-104`) both use `CombatLogs.GameObjectCache`. `Ability.Parse` does `return (Ability?)value;` on a hit. If the same content was first parsed as a base `GameObject`, the cached object is a `GameObject` (not `Ability`), and the cast throws. Today the bug is *latent* because `CombatLogLine.Parse` parses ability and actor sections from different rom slices that rarely collide on `GetHashCode()` — and the current key (reference+index+length) makes cross-type key collisions even rarer. Switching to CONTENT keys (RFCT-03) makes collisions MORE likely, which would *surface* this bug unless fixed simultaneously.
**Why it happens:** One cache shared across two types in an inheritance hierarchy, with a downcast on read.
**How to avoid:** Separate caches per concrete type (Pattern 2, Option A). This is the cleanest fix and is required *together with* the content-key change — they must land in the same task to avoid introducing a regression mid-refactor.
**Warning signs:** Any `(Ability?)` or `(GameObject?)` cast on a cache `TryGetValue` result. [VERIFIED: Ability.cs:16, GameObject.cs:104]

### Pitfall 3: `Action` GetHashCode/Equals inconsistency interacts with content keys
**What goes wrong:** `Action.Equals` uses `Rom.Span.SequenceEqual` (content) but `Action.GetHashCode` returns `Rom.GetHashCode()` (reference+index+length, NOT content) — `Action.cs:37,71`. The `ActionCache` is keyed on `rom.GetHashCode()` (`Action.cs:47,54`), so two `Action`s with identical content but different backing memory hash differently and both get cached. Re-keying `ActionCache` to `rom.ToString()` content keys fixes the dedup, but be aware `Action.GetHashCode` itself stays content-inconsistent unless also updated — it is used if `Action` is ever placed in a hash-based set. **Scope note:** the locked decision is about the *cache key*, not `Action.GetHashCode`. Recommend keying the cache by `rom.ToString()` and leaving `Action.GetHashCode` as-is unless a test requires otherwise (flag, don't fix speculatively).
**How to avoid:** Key `ActionCache` by string content; do not rely on `Action.GetHashCode` for cache identity. [VERIFIED: Action.cs:37,47,54,71]

### Pitfall 4: Deleting host `View/` files breaks three `using`s + the Overlay adapter
**What goes wrong:** RFCT-01 removes `SwtorLogParser.Cli/View/*`, `.Native.Cli/View/*`, `.Overlay/View/*`. The CLI `Program.cs:5` (`using SwtorLogParser.Cli.View;`), Native.Cli `Program.cs:4` (`using SwtorLogParser.Native.Cli.View;`), and `ParserForm.cs:2` (`using SwtorLogParser.Overlay.View;`) must point at the new shared namespace. The Overlay is NOT a drop-in: its `SlidingExpirationList` is `BindingList<Entry>` with `Control`/`Timer`/`DataGridView` refresh (`SlidingExpirationList.cs:11-66`) and its `Entry` exposes formatted string props (`Name`, `DPS`, `DCrit`, ...) bound by `DataPropertyName` in `ParserForm.cs:42-46`.
**Why it happens:** Only CLI + Native.Cli variants are genuinely identical and UI-free; the Overlay one is a true WinForms adapter.
**How to avoid:** Extract a data-only `Entry` (Stats + Expiration) and data-only `SlidingExpirationList` into the lib. Keep the Overlay's WinForms binding (the `BindingList<Entry>` subclass, the `Control` refresh timer, the formatted-string `Entry` projection) in an Overlay-side adapter that *wraps or consumes* the shared core. The `DataPropertyName` bindings (`Entry.Name`, `Entry.DPS`, ...) are formatting concerns that belong in the Overlay's view-model `Entry`, NOT the core DTO. [VERIFIED: Overlay/View/*.cs, ParserForm.cs:40-115]

### Pitfall 5: `Stop()` nulls task fields → `IsRunning` and re-`Start` semantics
**What goes wrong:** `Stop()` sets `_monitor = null; _reader = null;` (lines 147-148). `IsRunning` (line 32) reads `_monitor is { IsCompleted:false } && _reader is { IsCompleted:false }` — after Stop, both are null so `IsRunning` is false (correct). A lifecycle test must account for the async tasks not having actually wound down synchronously when `Stop()` returns (cancellation is cooperative; the worker `Task.Delay` loops observe the token). Asserting "no more lines after Stop" should push a line and assert no NEW `PlayerStats`, with a short settle, rather than asserting instantaneous teardown.
**How to avoid:** TEST-01 should assert `IsRunning` transitions and that `Stop()` before `Start()` is a no-op (already covered by `Stop_Before_Start_Does_Not_Throw`), plus that a second `Start()` does not throw (Phase 2 linked-CTS dispose logic, lines 121-125). For "stops delivering," prefer the internal `Push` seam over the real file loop so delivery is deterministic. [VERIFIED: CombatLogsMonitor.cs:32,116-150]

### Pitfall 6: `CombatLogs` static ctor touches the filesystem at type-load
**What goes wrong:** `CombatLogs` static ctor (`CombatLogs.cs:22-29`) enumerates `SettingsDirectory` for `*PlayerGUIState.ini` to populate `PlayerNames`. This runs the first time ANY `CombatLogs` member is touched — including the parse caches and `Actor.IsLocalPlayer` (`Actor.cs:36` reads `CombatLogs.PlayerNames`). On a CI machine without the SWTOR settings folder, `SettingsDirectory.EnumerateFiles` over a non-existent directory throws `DirectoryNotFoundException` inside the static ctor → `TypeInitializationException` that poisons the type for the whole test run. This is exactly why `All_Logs_Are_Not_Null` and `Player_Is_Local_Is_True` are flaky/non-hermetic.
**How to avoid:** The TEST filesystem seam must (a) make the logs directory + settings/PlayerNames injectable so tests supply temp/in-memory fixtures, and (b) avoid throwing when the real directories are absent. Options: guard `EnumerateFiles` with `Directory.Exists`, and/or introduce an `ICombatLogsSource` (or settable static providers behind `internal`) that tests override. Keep the static-ctor BUG-04 tolerance intact. [VERIFIED: CombatLogs.cs:22-29, Actor.cs:36, CombatLogLineTests.cs:9-44, ActorTests.cs:26-37]

## View Dedup Diff (RFCT-01 detail)

**CLI vs Native.Cli — byte-identical, UI-free (the genuinely shared part):**
- `Entry.cs`: `{ PlayerStats Stats; DateTime Expiration; }` — identical except nullable annotations (`= null!`, `= default` in Native.Cli). [VERIFIED]
- `SlidingExpirationList.cs`: identical — `SortedList<long, Entry>` keyed by `Player.Id`, `Timer`-based `RemoveExpiredItems`, `AddOrUpdate`, `Items` returns `ImmutableList` of `PlayerStats`. No WinForms. [VERIFIED: byte-compare of the two files]

**Overlay — fundamentally different (the host-specific adapter):**
- `Entry : IComparable<Entry>, IEquatable<Entry>` with formatted string projections `Name/DPS/DCrit/HPS/HCrit` for `DataGridView` `DataPropertyName` binding. [VERIFIED: Overlay/View/Entry.cs]
- `SlidingExpirationList : BindingList<Entry>` holding a `Control`, a render `Timer` calling `_control.Invoke(Refresh)`, `ClearItems`/`InsertItem` (BindingList API), `_control.Refresh()`. Pure WinForms. [VERIFIED: Overlay/View/SlidingExpirationList.cs]
- Bound in `ParserForm.cs:115`: `dataGridView.DataSource = _list = new SlidingExpirationList(dataGridView, ...)`.

**Recommended split:**
- **Core lib (`SwtorLogParser.View`, data-only):** `Entry { PlayerStats Stats; DateTime Expiration; }` + `SlidingExpirationList` (the `SortedList`/`Timer`/`AddOrUpdate`/`Items` CLI version). CLI and Native.Cli switch to this verbatim. AOT-safe — no WinForms.
- **Overlay host (keeps its adapter):** the `BindingList<Entry>` subclass + the formatted-string `Entry` view-model stay in the Overlay project. The Overlay's `Entry` formatting can consume the core `PlayerStats` directly (it already does via `Stats`). The Overlay does NOT use the core `SlidingExpirationList` unless refactored to wrap it; simplest behavior-preserving move is: extract the shared CLI list to core, delete CLI+Native duplicates, and leave the Overlay's WinForms list/Entry in place (renamed to avoid namespace confusion). This satisfies "exactly one location" for the *shared* `Entry`/`SlidingExpirationList` while honoring "WinForms binding stays host-side."

**Confirm no WinForms leak:** after the move, `SwtorLogParser` (core, `net8.0`) must still build with `IsAotCompatible=true`; grep the new `View/` files for `BindingList`, `Control`, `DataGridView`, `System.Windows.Forms` → must be zero.

## Code Examples

### Bounded cache wrapper (minimal, AOT-safe) — RFCT-03
```csharp
// Source: pattern grounded in BCL ConcurrentDictionary; simple cap eviction per locked decision.
// Eviction policy: when count exceeds cap after an add, drop ~one batch of oldest-inserted keys.
internal sealed class BoundedCache<TKey, TValue> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TValue> _map = new();
    private readonly ConcurrentQueue<TKey> _order = new();   // insertion order for simple eviction
    private readonly int _cap;
    public BoundedCache(int cap) => _cap = cap;

    public bool TryGetValue(TKey key, out TValue value) => _map.TryGetValue(key, out value!);

    public TValue GetOrAdd(TKey key, TValue value)
    {
        if (_map.TryGetValue(key, out var existing)) return existing;
        if (_map.TryAdd(key, value))
        {
            _order.Enqueue(key);
            while (_map.Count > _cap && _order.TryDequeue(out var old))
                _map.TryRemove(old, out _);   // simple oldest-out eviction
            return value;
        }
        return _map.TryGetValue(key, out var won) ? won : value;
    }
}
```
*Note: this is "simple eviction" (insertion-order, not strict LRU) — acceptable per the locked decision for a parse-interning cache. Cap value (e.g. 4096) is at Claude's discretion; document the choice.*

### Direct math test (deterministic, bypasses DateTime.Now) — TEST-02
```csharp
// Make Accumulator + CalculateDpsHpsStats internal; InternalsVisibleTo already grants access.
var monitor = new CombatLogsMonitor(NullLogger<CombatLogsMonitor>.Instance);
var state = new HashSet<CombatLogLine>(/* comparer */);
state = monitor.Accumulator(state, ParsePlayerDamageLine(t0));     // known damage
state = monitor.Accumulator(state, ParsePlayerDamageLine(t0_plus_1));
var stats = monitor.CalculateDpsHpsStats(state);
Assert.Equal(expectedDamageTotal / expectedSeconds, stats.DPS!.Value, precision: 3);
// 10s expiry: add a line 11s older, then a newer line, assert the old one is RemoveWhere'd.
```

### Now-relative pipeline test (TEST-01)
```csharp
string now = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
var line = CombatLogLine.Parse($"[{now}] [@LocalName#1|(0,0,0,0)|(1/2)] [=] [Ability {{1}}] [ApplyEffect {{836045448945477}}: Damage {{836045448945501}}] (100)".AsMemory());
var received = new List<CombatLogsMonitor.PlayerStats>();
using var sub = monitor.DpsHps.Subscribe(received.Add);
monitor.Push(line!);
Assert.NotEmpty(received);   // assert delivery + identity, not exact numbers
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `#if RELEASE/#elif DEBUG` for static `Instance` | Unconditional singleton + DI ctor | This phase | Compiles + is testable in any config |
| `ReadOnlyMemory<char>.GetHashCode()` cache keys (ref+index+length) | Content string keys (`rom.ToString()`) | This phase | Correct dedup; surfaces the cast bug → fix together |
| Shared `GameObjectCache` for `GameObject` + `Ability` | Separate per-type caches | This phase | Eliminates `InvalidCastException` risk |
| Unbounded caches | Bounded with simple eviction | This phase | No unbounded memory growth |
| Triplicated host `View/` | One core data-only copy + Overlay adapter | This phase | DRY; AOT-clean core |
| Non-hermetic FS tests | Injectable FS seam | This phase | CI-safe, deterministic |

**Deprecated/outdated (NOT this phase — noted for context):**
- All packages are on preview/alpha versions (e.g. `System.Reactive 6.0.1-preview.1`, MS.Extensions `8.0.0-preview.5`) — GA upgrade is DEP-01/Phase 5. Do not bump here.
- `Microsoft.Extensions.DependencyInjection` is referenced by the core lib csproj but never used in code (only `LoggerFactory` from Logging in the DEBUG `Instance`). After RFCT-02 moves to `NullLogger`, the core lib no longer needs `Logging.Console`/`Logging.Debug` either — but removing those references is a dependency change; flag for Phase 5 rather than removing here (removal could affect host transitive references). [VERIFIED: grep — only csproj reference, no code usage]

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | A cache cap of ~4096 entries is "reasonable" for parse interning | RFCT-03 / Code Examples | Low — cap is explicitly Claude's discretion; tune later. Too small = more re-parsing (correctness unaffected); too large = bounded but high memory |
| A2 | `Accumulator`/`CalculateDpsHpsStats` can be made `internal` without behavior change for tests | TEST-02 | Low — visibility change only; `InternalsVisibleTo` already present |
| A3 | The Overlay's `SlidingExpirationList` does not need to consume the shared core list to satisfy "exactly one location" (only the shared `Entry`/list must be deduped; the Overlay adapter is a distinct WinForms type) | RFCT-01 / View Dedup Diff | Medium — if the planner reads "exactly one `SlidingExpirationList` type in the whole solution" literally, the Overlay adapter must be refactored to *wrap* the core list rather than be a parallel `BindingList`. Recommend confirming intent: the locked text says "keep the WinForms binding adapter in the Overlay ONLY," which supports a separate Overlay adapter type. |
| A4 | Removing the `DateTime.Now` dependency is OUT of scope (would reshape behavior / overlap Phase 4) | Pitfall 1 | Low — locked decision says keep behavior identical and don't pull perf forward |
| A5 | `Action.GetHashCode` content-inconsistency does not need fixing this phase (only the cache key) | Pitfall 3 | Low — locked scope is cache keys; flag-don't-fix is conservative |

## Open Questions

1. **Should the Overlay's `SlidingExpirationList` be refactored to *wrap* the shared core list, or remain a parallel WinForms adapter?**
   - What we know: Locked decision says shared `Entry`/`SlidingExpirationList` live in exactly one place; WinForms binding stays Overlay-side. CLI + Native.Cli lists are identical and UI-free.
   - What's unclear: Whether "exactly one location" forbids the Overlay having its own `BindingList`-based list type at all.
   - Recommendation: Treat the *data-only* `Entry` + `SlidingExpirationList` as the shared core (CLI/Native consume directly). Keep the Overlay's WinForms list as a thin adapter, ideally consuming the core list's data API rather than duplicating the expiry logic. Confirm with the user during planning if literal interpretation matters (see A3).

2. **Cache eviction: insertion-order vs true LRU?**
   - What we know: Locked decision says "oldest/LRU" and "simple eviction," cap at discretion.
   - What's unclear: Whether strict recency matters for the parse cache.
   - Recommendation: Insertion-order ("oldest-out") is simplest and sufficient for parse interning; document it. Defer true LRU (BitFaster) to Phase 5 if ever needed.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET 8 SDK | Build/test all projects | ✓ | 8.x (build + 77 tests pass this session) | — |
| xUnit test runner | TEST-01/02 | ✓ | 2.5.0-pre.44 | — |
| SWTOR `CombatLogs` directory | `All_Logs_Are_Not_Null` (today) | ✗ on CI | — | **The point of the FS seam** — fixtures replace it |
| SWTOR settings `*PlayerGUIState.ini` | `Player_Is_Local_Is_True` + `CombatLogs` static ctor | ✗ on CI | — | FS seam + `Directory.Exists` guard |

**Missing dependencies with no fallback:** none — both missing FS dependencies are *the thing this phase abstracts away*.
**Missing dependencies with fallback:** SWTOR log/settings folders → replaced by injectable seam + temp/in-memory fixtures (the deliverable that makes CI possible in Phase 6).

## Validation Architecture

> `.planning/config.json` not present / key absent → nyquist_validation treated as ENABLED.

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.5.0-pre.44 (+ xunit.runner.visualstudio, Microsoft.NET.Test.Sdk 17.7.0-preview, coverlet.collector 6.0.0) |
| Config file | none (convention-based; `GlobalUsings.cs` = `global using Xunit;`) |
| Quick run command | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj --filter "FullyQualifiedName~CombatLogsMonitor"` |
| Full suite command | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| RFCT-02 | `Instance` defined + non-null in Debug/Release; public ctor constructs | unit | `dotnet test --filter "FullyQualifiedName~Monitor_Constructs"` | ❌ Wave 0 |
| RFCT-03 | Content keys dedup identical-content roms; `Ability` vs `GameObject` no cast throw; cap bounds growth | unit | `dotnet test --filter "FullyQualifiedName~Cache"` | ❌ Wave 0 (extend AbilityTests/GameObjectTests/ActionTests) |
| RFCT-01 | Core `SlidingExpirationList` AddOrUpdate/expiry; core builds AOT-clean | unit + build | `dotnet build SwtorLogParser -c Release` + `dotnet test --filter "SlidingExpiration"` | ❌ Wave 0 |
| TEST-01 | Start→delivers; Stop→stops; Stop-before-Start no-op; second Start no throw | unit | `dotnet test --filter "FullyQualifiedName~CombatLogsMonitorTests"` | ⚠️ Partial (`Stop_Before_Start_Does_Not_Throw` exists) |
| TEST-02 | DPS/HPS/crit% math + 10s window from known lines | unit | `dotnet test --filter "FullyQualifiedName~DpsHps"` | ❌ Wave 0 |
| TEST seam | `All_Logs_Are_Not_Null` / `Player_Is_Local_Is_True` hermetic | unit | `dotnet test --filter "All_Logs_Are_Not_Null|Player_Is_Local"` | ⚠️ Exist but non-hermetic (CombatLogLineTests.cs:9, ActorTests.cs:26) |

### Sampling Rate
- **Per task commit:** `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` (suite is ~480ms — run the whole thing every commit; no need to sub-filter)
- **Per wave merge:** full suite + `dotnet build SwtorLogParser -c Release` (proves AOT-clean core after RFCT-01)
- **Phase gate:** full suite green (≥77, growing with new tests) before `/gsd-verify-work`; optionally `dotnet publish SwtorLogParser.Native.Cli -c Release` to prove AOT still compiles end-to-end

### Wave 0 Gaps
- [ ] `CombatLogsMonitorTests.cs` — extend with lifecycle (Start/Stop/IsRunning, second Start) for TEST-01
- [ ] new `DpsHpsMathTests.cs` — `Accumulator` + `CalculateDpsHpsStats` direct tests for TEST-02
- [ ] new `ParseCacheTests.cs` (or extend `AbilityTests`/`GameObjectTests`/`ActionTests`) — content-key dedup, type-correctness, cap bound for RFCT-03
- [ ] make `Accumulator`, `CalculateDpsHpsStats`, and a `Push`/`OnNext` seam `internal` (visible via existing `InternalsVisibleTo`)
- [ ] filesystem seam fixtures for hermetic `All_Logs_Are_Not_Null` / `Player_Is_Local_Is_True`
- [ ] Framework install: none — infrastructure already present

## Security Domain

This phase has no authentication, session, network, crypto, or untrusted-input-over-the-wire surface. The only inputs are local SWTOR log files (read-only) and settings `.ini` files (read-only), already opened `FileAccess.Read`/`FileShare.ReadWrite` (BUG-07).

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | — |
| V3 Session Management | no | — |
| V4 Access Control | no | — |
| V5 Input Validation | partial | Parser already returns `null` over throwing on malformed input (Phase 2 BUG-05); cache content keys do not alter this |
| V6 Cryptography | no | — |

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Unbounded cache from attacker-controlled-volume log lines (DoS / memory exhaustion) | Denial of Service | RFCT-03 bound directly mitigates this — bounded caches cap memory |
| Malformed log line | Tampering | Existing null-return parsing (BUG-05) |

## Sources

### Primary (HIGH confidence)
- Current repository source (read this session): `CombatLogsMonitor.cs`, `CombatLogs.cs`, `GameObject.cs`, `Ability.cs`, `Action.cs`, `Value.cs`, `CombatLogLine.cs`, `CombatLog.cs`, `Actor.cs` (grep), all three host `View/` folders, `ParserForm.cs`, all three `Program.cs`, the four csproj files, and the existing test files — every code-level claim is `[VERIFIED]` against these.
- Build/test executed this session: 77 tests passed; Release build of core lib succeeded (2 CS8618 warnings reproduced).

### Secondary (MEDIUM confidence)
- [BitFaster.Caching ConcurrentLru](https://github.com/bitfaster/BitFaster.Caching) — documented bounded-cache alternative (NOT adopted).
- [A Threadsafe C# LRUCache Implementation](https://www.c-sharpcorner.com/article/a-threadsafe-c-sharp-lrucache-implementation/) — confirms hand-rolled LRU subtlety, justifying the simpler insertion-order eviction choice.

### Tertiary (LOW confidence)
- None.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new packages; all current packages verified in csproj and build/test run.
- Architecture / refactor mechanics: HIGH — exact source lines verified; the four refactors are mechanically clear.
- Pitfalls: HIGH — the `DateTime.Now`, shared-cache cast, and static-ctor FS landmines are confirmed in source.
- View dedup split (A3): MEDIUM — depends on interpretation of "exactly one location" for the Overlay adapter; flagged as Open Question 1.

**Research date:** 2026-06-11
**Valid until:** 2026-07-11 (stable domain; re-verify line numbers only if source changes before planning)

Sources:
- [BitFaster.Caching](https://github.com/bitfaster/BitFaster.Caching)
- [A Threadsafe C# LRUCache Implementation](https://www.c-sharpcorner.com/article/a-threadsafe-c-sharp-lrucache-implementation/)
