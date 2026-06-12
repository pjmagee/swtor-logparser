---
phase: 09-live-stream-render-dispatcher-marshaling
plan: 01
subsystem: ui
tags: [winui3, dispatcher-marshaling, rx, dpshps, observablecollection, overlay, mvvm, aot-isolation]

# Dependency graph
requires:
  - phase: 08-winui-3-scaffold-dependencies-guardrails
    provides: Empty self-contained WinUI 3 window (SwtorLogParser.Overlay.WinUi) + WinAppSDK/CsWin32 isolated to the overlay
provides:
  - "EntryViewModel: INotifyPropertyChanged display projection of CombatLogsMonitor.PlayerStats (Name/DPS/DCrit/HPS/HCrit strings + numeric DpsValue sort key)"
  - "EntryFormat: pure WinUI-free formatting + DPS-sort-key helpers (blank cells for null, crit% as '42.5%', invariant culture)"
  - "MainViewModel: DpsHps.Subscribe -> core SlidingExpirationList(10s) off-thread; 1s DispatcherQueueTimer mirrors core.Items into ObservableCollection<EntryViewModel> sorted DPS-desc; IDisposable disposes sub + stops timer"
  - "MainWindow.xaml: ListView x:Bind to ViewModel.Rows with 5 parity columns (Player / DPS / Crit% / HPS / Crit%)"
  - "Monitor-start-on-activation + VM-dispose-on-close wired in MainWindow.xaml.cs (parity with ParserForm.OnActivated)"
  - "ProjectReference overlay -> core SwtorLogParser.csproj (no PublishAot/trim flag on the overlay; core AOT graph uncontaminated)"
affects: [Phase 9 Plan 02 (font controls + settings persistence — shares MainWindow.xaml(.cs)), Phase 10 (interop styles this live render surface), Phase 11 (parity gate validates live render vs WinForms)]

# Tech tracking
tech-stack:
  added: []
  patterns: ["Rx -> WinUI dispatcher marshaling: capture DispatcherQueue once on UI thread; OnNext touches only the internally-locked core list; all XAML mutation on a 1s DispatcherQueueTimer tick (T-09-01/T-09-02 mitigations)", "Off-thread aggregate into the reused core View/SlidingExpirationList (FROZEN core, unchanged); UI-tick render mirror reconciles into ObservableCollection by Player.Id (update-in-place, no full clear/rebuild)", "Pure WinUI-free EntryFormat helper so number/crit/sort logic stays correct by construction and unit-testable over core types only"]

key-files:
  created:
    - SwtorLogParser.Overlay.WinUi/ViewModels/EntryViewModel.cs
    - SwtorLogParser.Overlay.WinUi/ViewModels/EntryFormat.cs
    - SwtorLogParser.Overlay.WinUi/ViewModels/MainViewModel.cs
  modified:
    - SwtorLogParser.Overlay.WinUi/SwtorLogParser.Overlay.WinUi.csproj
    - SwtorLogParser.Overlay.WinUi/MainWindow.xaml
    - SwtorLogParser.Overlay.WinUi/MainWindow.xaml.cs

key-decisions:
  - "Off-thread aggregation in the reused core SlidingExpirationList(10s); DpsHps OnNext calls ONLY _core.AddOrUpdate(stats) and never touches XAML (D-02 / Pitfall 5 — avoids cross-thread COMException 0x8001010E)"
  - "DispatcherQueue captured once on the UI thread in the MainViewModel ctor (non-null asserted — VM must be built on the UI thread); a 1s DispatcherQueueTimer mirrors core.Items into Rows (D-01 — caps UI churn regardless of log volume)"
  - "DPS-descending ordering is a static, WinUI-free pure method (OrderByDpsDescending) over core PlayerStats; null/zero DPS sorts last (D-03 — render-time sort only, core list not re-keyed)"
  - "Rows reconciled in place by Player.Id (update existing / add missing / remove stale) instead of clear+rebuild, to avoid ListView flicker"
  - "Monitor started on window activation (guarded to fire once) and ViewModel.Dispose() called on window Closed (IN-03 — no leaked subscription/timer); window stays normal/opaque/movable (no Phase-10 interop here)"
  - "Overlay->core ProjectReference added with NO PublishAot/trim flag on the overlay; AOT regression gate confirms the core/Native.Cli AOT graph stayed warning-free (boundary holds)"

patterns-established:
  - "Pattern: Rx stream -> WinUI render without cross-thread crash — aggregate off-thread in a locked core list, marshal a periodic UI-tick snapshot to the captured DispatcherQueue, never marshal per OnNext"

requirements-completed: [OVL-02]

# Metrics
duration: 3min
completed: 2026-06-12
---

# Phase 9 Plan 01: Live Stream Render + Dispatcher Marshaling Summary

**The WinUI 3 overlay now subscribes to the frozen `CombatLogsMonitor.Instance.DpsHps` stream and renders live per-player rows (Player / DPS / Crit% / HPS / Crit%) on the UI thread — aggregating off-thread in the reused core `SlidingExpirationList` and mirroring a DPS-descending snapshot to an `ObservableCollection` on a 1s `DispatcherQueueTimer`, so the first live update marshals cleanly to the captured UI `DispatcherQueue` with no cross-thread `COMException 0x8001010E`.**

## Performance

- **Duration:** ~3 min
- **Started:** 2026-06-12T10:08:55+01:00 (first task commit)
- **Completed:** 2026-06-12T10:11:35+01:00 (last task commit)
- **Tasks:** 3
- **Files modified:** 6 (3 created, 3 modified)

## Accomplishments
- Added a `ProjectReference` from `SwtorLogParser.Overlay.WinUi` to the core `SwtorLogParser.csproj` with **no** `PublishAot`/trim flag on the overlay — the core (`IsAotCompatible`) and Native CLI AOT graph stay uncontaminated.
- Created `EntryViewModel` (INotifyPropertyChanged display projection of `PlayerStats` — `Name`/`DPS`/`DCrit`/`HPS`/`HCrit` strings + a numeric `DpsValue` sort key) backed by the pure, WinUI-free `EntryFormat` helper (rounded DPS/HPS, crit% as `42.5%`, **blank** cells for null values, invariant culture, `"?"` for a null player name).
- Created `MainViewModel`: captures the UI `DispatcherQueue` once in the ctor; subscribes `DpsHps` off-thread into a reused core `SlidingExpirationList(TimeSpan.FromSeconds(10))` (OnNext calls **only** `_core.AddOrUpdate(stats)` — never touches XAML); a 1s `DispatcherQueueTimer` mirrors `core.Items` into `ObservableCollection<EntryViewModel> Rows` via a static DPS-descending order (`OrderByDpsDescending`, null/zero last), reconciling rows in place by `Player.Id`; `Dispose()` disposes the subscription and stops the timer.
- Replaced the empty Phase 8 `<Grid/>` in `MainWindow.xaml` with a `ListView` `x:Bind` to `ViewModel.Rows` rendering the 5 parity columns (Player / DPS / Crit% / HPS / Crit%); `MainWindow.xaml.cs` constructs the VM on the UI thread, starts the monitor on first activation (guarded; parity with `ParserForm.OnActivated`), and disposes the VM on `Closed`.

## Task Commits

Each task was committed atomically:

1. **Task 1: Add core ProjectReference + EntryViewModel display projection** — `bc0eb8b` (feat) — also added `EntryFormat.cs` (pure formatting/sort helper)
2. **Task 2: MainViewModel — DpsHps subscription, 1s dispatcher mirror, DPS-desc sort, disposal** — `5a07651` (feat)
3. **Task 3: Bind live ListView, construct VM, start monitor on activation, dispose on close** — `990e046` (feat)

**Plan metadata:** see final docs commit.

## Files Created/Modified
- `SwtorLogParser.Overlay.WinUi/ViewModels/EntryViewModel.cs` *(created)* — display projection of `PlayerStats` with `INotifyPropertyChanged` + numeric `DpsValue` sort key; `Update(PlayerStats)` refreshes a row in place.
- `SwtorLogParser.Overlay.WinUi/ViewModels/EntryFormat.cs` *(created)* — pure WinUI-free formatting helpers: `Name` (null→`"?"`), `Rate` (`F0`, null→blank), `Crit` (`0.0` + `%`, null→blank), `DpsSortKey` (null→0). No XAML/WinAppSDK dependency.
- `SwtorLogParser.Overlay.WinUi/ViewModels/MainViewModel.cs` *(created)* — `IDisposable` VM owning the captured `DispatcherQueue`, the core `SlidingExpirationList(10s)`, the `DpsHps` subscription, and the 1s `DispatcherQueueTimer` render mirror; static `OrderByDpsDescending` sort.
- `SwtorLogParser.Overlay.WinUi/SwtorLogParser.Overlay.WinUi.csproj` *(modified)* — added `<ProjectReference Include="..\SwtorLogParser\SwtorLogParser.csproj" />` (no AOT/trim flag).
- `SwtorLogParser.Overlay.WinUi/MainWindow.xaml` *(modified)* — bound `ListView` (5 parity columns) replacing the empty grid.
- `SwtorLogParser.Overlay.WinUi/MainWindow.xaml.cs` *(modified)* — VM construction on the UI thread; monitor-start-on-activation (guarded once); VM dispose on `Closed`.

## Artifacts This Phase Produces (Plan 01 portion)

| Artifact | New/Modified | Provides |
|----------|--------------|----------|
| `SwtorLogParser.Overlay.WinUi.csproj` | Modified | `ProjectReference` to the core library (no AOT contamination) |
| `ViewModels/EntryViewModel.cs` | New | Display projection of `PlayerStats` + `INotifyPropertyChanged` + numeric sort key |
| `ViewModels/EntryFormat.cs` | New | Pure WinUI-free number/crit/name formatting + DPS sort key |
| `ViewModels/MainViewModel.cs` | New | `DpsHps`→core list off-thread; 1s `DispatcherQueueTimer` mirror; DPS-desc sort; `IDisposable` |
| `MainWindow.xaml` | Modified | `ListView` `x:Bind` to `ViewModel.Rows`, 5 columns |
| `MainWindow.xaml.cs` | Modified | VM construct on UI thread; monitor-on-activation; dispose-on-close |

## Build / Gate Results
- **Overlay-only build** (`dotnet build SwtorLogParser.Overlay.WinUi -c Debug` + `-c Release`, win-x64): **0 warnings / 0 errors**; a self-contained launchable `SwtorLogParser.Overlay.WinUi.exe` was produced.
- **Full solution build** (`dotnet build SwtorLogParser.slnx -c Release`): **0 errors**. The only warnings are the 5 **pre-existing** WinForms `ParserForm.cs` warnings (CS0108 + CS8602) — out of scope and frozen until the Phase 10/11 parity gate deletes the WinForms host (logged in `deferred-items.md`).
- **AOT regression gate** (`dotnet publish SwtorLogParser.Native.Cli -c Release`): the managed IL-compilation phase produced **zero IL2xxx/IL3xxx warnings** — confirming the new overlay→core `ProjectReference` did **not** contaminate the core's AOT/trim graph (gate PASSES). See "Deferred / Environment-gated" below for the native-link step.

## Human-Verify Outcome (Task 3 checkpoint)
- **Confirmed:** the user launched the Release self-contained `SwtorLogParser.Overlay.WinUi.exe` and the window **OPENED with no crash** and the monitor **started** — no cross-thread `COMException 0x8001010E` at launch or first tick. The dispatcher-marshaling mitigation (capture `DispatcherQueue` once; OnNext touches only the locked core list; XAML mutated only on the 1s timer tick) holds.
- **NOT visually confirmed this session:** live per-player rows updating/sorting/expiring with **real combat data** were not observed, because no combat log was producing data at the time. The no-crash launch meets the checkpoint's minimum bar; **full visual confirmation of live rows rendering DPS-desc and expiring after ~10s with real data is DEFERRED to the Phase 11 live-game parity gate** (and to the user's next real in-game session). This summary does NOT claim live rows were visually verified.

## Requirement Status
- **OVL-02** — Satisfied at the **no-crash-launch** level: the overlay references the core, subscribes to `DpsHps`, reuses the core `SlidingExpirationList` + `Entry` unchanged, and renders into a bound `ListView` with the dispatcher-marshaling mitigation in place (window opens, monitor starts, no cross-thread crash). The **live-data visual** (rows populating/sorting/expiring with real combat data) is **deferred to the Phase 11 parity gate**. Marking OVL-02 complete is appropriate because OVL-02 is wholly within this plan and the only remaining item is a visual reconfirmation gated on a live game session, tracked at Phase 11.

## Deviations from Plan

None — the plan executed as written. All locked decisions were honored:
- Off-thread aggregate into the reused core `SlidingExpirationList(10s)`; `DispatcherQueue` captured once; OnNext never touches XAML.
- 1s `DispatcherQueueTimer` mirrors `core.Items` into `ObservableCollection<EntryViewModel>` sorted DPS-descending (null/zero last).
- Monitor started on activation (guarded once); subscription + timer disposed on `Closed`.
- Window kept normal / opaque / movable (no Phase-10 interop introduced here).
- The DPS-descending ordering was extracted to a static WinUI-free method (`OrderByDpsDescending`) and formatting to a pure `EntryFormat` helper, so both stay unit-testable over core types without a WinAppSDK dependency in the test/AOT graph.

## Deferred / Environment-gated
- **AOT native-link step (environment, NOT a code regression):** `dotnet publish SwtorLogParser.Native.Cli -c Release` reached the native C++ link step and failed with `MSB3073` (`vswhere.exe`/`link.exe` not on this shell's PATH — MSVC toolchain not available in this environment). The **managed IL analysis was warning-free**, which is what the AOT-contamination gate checks. This is the same env limitation seen in v1.0 Phase 7 + Phase 8 and is **CI-covered** (CI runs from a Developer environment with the C++ build tools on PATH). Re-run from a Visual Studio Developer prompt to complete the native link. Logged in `.planning/phases/09-live-stream-render-dispatcher-marshaling/deferred-items.md`.
- **Phase 11 live-data parity gate:** visual confirmation of live rows rendering/updating/expiring with real combat data (see Human-Verify Outcome above).

## Issues Encountered
- None blocking. Two items are environment/scope-gated and tracked above (native-link toolchain, live-data visual).

## Note on Working-Tree State (not part of this plan)
At finalization, the working tree contained **uncommitted, formatting-only churn** in four core files (`SwtorLogParser/Model/CombatLogLine.cs`, `Model/GameObject.cs`, `Monitor/CombatLog.cs`, `Monitor/CombatLogsMonitor.cs` — brace reflows, a primary-constructor rewrite, and `object`→`Lock`). These are **NOT** part of plan 09-01's task commits, are unrelated to the WinUI render work, and the core parser is **FROZEN** this milestone. They were intentionally **left unstaged** and excluded from the docs commit — this finalization committed only the overlay docs/state, never the core. (The pre-existing `.planning/v1.0-MILESTONE-AUDIT.md` deletion is likewise unrelated archival churn from before this plan.)

## Next Phase Readiness
- **Plan 09-02 (Wave 2)** can now add font +/- controls + settings persistence on top of `MainWindow.xaml(.cs)` and the `MainViewModel`.
- **Phase 10** has a real, live render surface to style with CsWin32 interop (transparency/click-through/drag/topmost).
- **Phase 11** parity gate inherits the deferred live-data visual confirmation.

## Self-Check: PASSED

- All 3 created overlay files present on disk (`EntryViewModel.cs`, `EntryFormat.cs`, `MainViewModel.cs`).
- All 3 task commits present in git history (`bc0eb8b`, `5a07651`, `990e046`).

---
*Phase: 09-live-stream-render-dispatcher-marshaling*
*Completed: 2026-06-12*
