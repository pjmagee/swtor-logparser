# Phase 3: Monitor Refactor + Coverage - Context

**Gathered:** 2026-06-11
**Status:** Ready for planning

<domain>
## Phase Boundary

Make the shared `CombatLogsMonitor` constructible in all build configurations and testable via DI; deduplicate the view-layer types into the core library; redesign the static parse caches to be content-keyed, bounded, and thread-safe; and add automated coverage for the monitor lifecycle, the Rx pipeline, and the DPS/HPS math. Requirements: RFCT-01, RFCT-02, RFCT-03, TEST-01, TEST-02.

**Critical invariant:** This refactor touches state every host depends on. The parser model, the live `IObservable<PlayerStats>` (DpsHps) behavior, and the three hosts must behave identically afterward. The core library MUST remain `IsAotCompatible=true` (no reflection, no DI container, no WinForms types in the core lib). Keep `dotnet test` green every commit; the Phase 1/2 suite (77 tests) is the regression contract.

**In scope:** `SwtorLogParser/Monitor/CombatLogsMonitor.cs`, `Monitor/CombatLogs.cs`, the cache code in `Model/*.cs`, a new shared view location in the core lib, host `View/` folders, and new tests.

**Out of scope:** Performance work (Phase 4 — PERF-01..03), dependency upgrades (Phase 5), CI (Phase 6). The overlay-topmost issue (BACKLOG BL-01) is NOT in scope but RFCT-01 touches overlay View code — do not fold BL-01 in unless trivially adjacent.

</domain>

<decisions>
## Implementation Decisions

### DI & construction (RFCT-02)
- Add a public constructor to `CombatLogsMonitor` taking `ILogger<CombatLogsMonitor>` (constructor injection) for DI/testing. Keep a lazily-initialized static `Instance` DEFINED IN ALL BUILD CONFIGURATIONS — remove the `#if RELEASE/#elif DEBUG` gap that currently leaves `Instance` undefined in other configs; add a default branch. Hosts keep using `Instance`; tests use the public ctor.
- Plain constructor injection only — NO reflection and NO DI container inside the core library. The core lib stays `IsAotCompatible=true`. (Hosts MAY use MS.DI if they want, but the core lib must not require it.)
- The default `Instance` uses `NullLogger<CombatLogsMonitor>` (console/debug logger providers stay host-side); behavior of the default singleton is unchanged from today's intent, just defined everywhere.

### Cache redesign (RFCT-03)
- Key the parse caches by string content (`rom.ToString()`) instead of `ReadOnlyMemory<char>.GetHashCode()` (which is reference+index+length, not content — the latent ME-02 finding). Accept the span→string allocation as the correctness cost.
- Bound the caches: a size cap with simple eviction (oldest/LRU) so they cannot grow without limit. Pick a reasonable cap.
- Keep thread-safety via `ConcurrentDictionary` (Phase 2) plus the bound.
- FIX the latent GameObject/Ability shared-cache type bug: make the content key type-correct (or use separate caches per type) so an `Ability` lookup cannot return a base `GameObject` and vice versa.

### View dedup (RFCT-01)
- `Entry` and `SlidingExpirationList` end up in EXACTLY ONE location: a UI-agnostic core in the core library (`SwtorLogParser`). All three hosts reference that single copy; per-host duplicate `View/` files are removed.
- The three `SlidingExpirationList` variants differ (the Overlay one binds a WinForms `DataGridView`; CLI/Native do not). Extract the UI-AGNOSTIC sliding-expiration core into the lib; keep the WinForms `DataGridView` binding adapter in the Overlay project ONLY.
- AOT constraint: NO WinForms types (DataGridView, BindingList, etc.) may enter the AOT core library. The shared part must be UI-free; host-specific binding stays in the host.

### Test coverage (TEST-01, TEST-02)
- Monitor/Rx tests use the new DI constructor plus a testable seam to push `CombatLogLine`s into the internal Subject (e.g. an internal method, already enabled by `InternalsVisibleTo(SwtorLogParser.Tests)` added in Phase 2). Assert: after Start the Subject delivers lines; after Stop it stops. Cover the Phase 2 cancellation wiring.
- DPS/HPS math: unit-test the accumulator and `CalculateDpsHpsStats` against known `CombatLogLine` inputs — assert DPS, HPS, crit%, and the 10s sliding-window expiry behavior.
- Abstract the filesystem access in `CombatLogs` (introduce a seam/interface for the logs directory + settings files) so the deferred Phase 1 non-hermetic tests (`All_Logs_Are_Not_Null`, `Player_Is_Local_Is_True`) become hermetic against in-memory/temp fixtures. This also removes the flaky failures seen in Phases 1-2.

### Claude's Discretion
- Exact cache cap size and eviction policy details, the precise shape of the testable seam (internal method vs internal-visible Subject), the filesystem-abstraction interface shape, and the namespace/folder for the shared view types are at Claude's discretion — guided by AOT-safety, keeping the suite green, and not pulling Phase 4/5 work forward.

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets
- `CombatLogsMonitor` already has a private `CombatLogsMonitor(ILogger<CombatLogsMonitor> logger)` ctor (`:this()`) and a private parameterless ctor — RFCT-02 mostly makes a ctor public and fixes the `#if` `Instance`.
- Phase 2 added `InternalsVisibleTo(SwtorLogParser.Tests)` to `SwtorLogParser.csproj` — reuse it for the testable seam.
- `CombatLogs` resolves paths via `Environment.GetFolderPath` and exposes `PlayerNames`, `EnumerateCombatLogs`, and the static caches — this is the filesystem seam target.
- The three `View/` folders (`SwtorLogParser.Cli/View`, `.Native.Cli/View`, `.Overlay/View`) contain the duplicated `Entry`/`SlidingExpirationList`.

### Established Patterns
- Core lib is `net8.0`, `IsAotCompatible=true`. Hosts: Cli/Native.Cli `net8.0`, Overlay `net8.0-windows` + WinForms.
- ConcurrentDictionary caches keyed on `Rom.GetHashCode()` (from Phase 2) — RFCT-03 replaces the key + adds bounding.
- `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` (77 tests currently green).

### Integration Points
- Every host subscribes to `CombatLogsMonitor.Instance.DpsHps`. The Overlay starts the monitor on form activation; Native/CLI on the `monitor` command. Refactor must not change these call sites' behavior (or update all three consistently).
- The Overlay's `SlidingExpirationList` binds a `DataGridView` (`dataGridView.DataSource = _list`) — the AOT-free shared core cannot include that.

</code_context>

<specifics>
## Specific Ideas

- The `#if RELEASE/#elif DEBUG` `Instance` is at `CombatLogsMonitor.cs:~14-20` (no `#else`).
- The static caches live in `CombatLogs.cs` (`GameObjectCache`, `ActionCache`) and are populated from `GameObject.cs`/`Ability.cs`/`Action.cs` (now via `TryAdd`).
- Phase 2 left two non-hermetic filesystem tests untouched (`CombatLogLineTests.All_Logs_Are_Not_Null`, `ActorTests.Player_Is_Local_Is_True`) — they flaked during Phases 1-2; abstracting the filesystem here makes them deterministic and CI-safe (important before Phase 6 CI).
- Keep behavior identical: the live DpsHps stream is the core value of the whole project.

</specifics>

<deferred>
## Deferred Ideas

- Overlay-topmost fix (BACKLOG BL-01) — separate; only touch if trivially adjacent to RFCT-01 view work.
- Performance of the accumulator full re-scan/re-sort (PERF-03) → Phase 4. RFCT-03 changes cache design, NOT the accumulator hot path.
- Dependency GA upgrades (Phase 5), CI (Phase 6).

</deferred>
