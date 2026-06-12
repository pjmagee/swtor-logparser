---
phase: 09-live-stream-render-dispatcher-marshaling
verified: 2026-06-12T00:00:00Z
status: passed
score: 10/10 must-haves verified
overrides_applied: 0
re_verification:
  previous_status: none
  note: initial verification
deferred:
  - truth: "Live per-player rows render with real combat data (visual confirmation of populated rows)"
    addressed_in: "Phase 11"
    evidence: "Phase 11 (Parity Gate + WinForms Removal, OVL-09) is the live-game parity gate; 09-01 human-verify launched the overlay with no combat log running, confirming window open + monitor start + no cross-thread COMException. The populated-rows visual is intentionally validated at the Phase 11 live-game gate."
---

# Phase 9: Live Stream Render + Dispatcher Marshaling Verification Report

**Phase Goal:** The WinUI 3 overlay subscribes to the frozen `CombatLogsMonitor.Instance.DpsHps` stream and renders live per-player rows on the UI thread with no cross-thread crash, reusing the core View sliding-expiry types unchanged — plus parity font controls and cross-run persistence of window position + size.
**Verified:** 2026-06-12
**Status:** passed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| #   | Truth                                                                                                  | Status     | Evidence |
| --- | ------------------------------------------------------------------------------------------------------ | ---------- | -------- |
| 1   | Overlay shows live per-player rows (Player/DPS/Crit%/HPS/Crit%) while a combat log is written           | ✓ VERIFIED | `MainWindow.xaml:46-76` ListView `x:Bind ViewModel.Rows` with 5-column DataTemplate over `EntryViewModel`; header row `:28-41`. 09-01 human-verify confirmed window opens + monitor starts with no crash (populated-rows visual deferred to Phase 11 live-game gate). |
| 2   | Rows are sorted by DPS descending; zero/no-DPS players sort to the bottom                                | ✓ VERIFIED | `MainViewModel.OrderByDpsDescending` (`:133-135`) = `OrderByDescending(s => s.DPS ?? 0d)` — null/0 DPS collapse to 0 and land last; applied each tick in `SyncRows` (`:85`). |
| 3   | First live update does not crash with COMException 0x8001010E (all UI mutation marshaled to UI dispatcher)| ✓ VERIFIED | UI `DispatcherQueue` captured once in ctor on UI thread (`MainViewModel.cs:46-48`); `OnNext` touches only the locked core list (`:60-66`); all `Rows` mutation runs on the 1s `DispatcherQueueTimer` tick (`:69-75`, `SyncRows`). 09-01 human-verify confirmed NO cross-thread COMException. |
| 4   | Combat-log monitor starts when the overlay window activates (mirrors ParserForm.OnActivated)            | ✓ VERIFIED | `MainWindow.OnActivated` (`:109-119`): one-shot `_monitorStarted` guard, ignores Deactivated, `if (!IsRunning) Start(CancellationToken.None)`. |
| 5   | Closing the window disposes the DpsHps subscription and stops the render timer (no leaked subscription)  | ✓ VERIFIED | `OnClosed` (`:121-132`) calls `ViewModel.Dispose()`; `MainViewModel.Dispose` (`:137-145`) disposes `_sub`, stops `_renderTimer`, unsubscribes Tick. (Core list `Timer` non-disposal is WR-01, accepted non-blocker.) |
| 6   | User can click +/- buttons to increase/decrease overlay row + header font (WinForms parity)              | ✓ VERIFIED | `MainWindow.xaml:22-25` ➕/➖ buttons → `IncreaseFont_Click`/`DecreaseFont_Click` (`:73-75`) mutate `FontSize` (clamped 8..48); header `:36-40` and ListView `:48` bind `FontSize` OneWay (inherited DP flows to rows). |
| 7   | Overlay restores previous window position + size on next launch                                         | ✓ VERIFIED | `ApplySavedSettings` (`:95-107`) → `GetAppWindow().MoveAndResize(RectInt32)` when all of X/Y/W/H present and w/h>0; saved on close in `SaveSettings` (`:138-156`). 09-02 human-verify CONFIRMED restore across restart. |
| 8   | Overlay restores previously chosen font size on next launch                                             | ✓ VERIFIED | `ApplySavedSettings` (`:97`) sets `FontSize = settings.FontSize`; persisted in `SaveSettings` (`:140`). 09-02 human-verify CONFIRMED font restore. |
| 9   | Missing/corrupt settings.json falls back to default position/size/font without throwing                  | ✓ VERIFIED | `SettingsService.Load` (`:70-86`) returns `new OverlaySettings()` on missing file / any exception (catch-all). Unit tests `Load_MissingFile_*` + `Load_CorruptJson_*` PASS (5/5 run live). 09-02 human-verify CONFIRMED corrupt/missing → defaults, no crash. |
| 10  | Settings are saved when the window closes                                                               | ✓ VERIFIED | `OnClosed` (`:128`) → `SaveSettings()` → `_settings.Save(settings)` before VM dispose; reads `AppWindow.Position`/`Size` + current `FontSize`. |

**Score:** 10/10 truths verified

### Deferred Items

| # | Item | Addressed In | Evidence |
|---|------|-------------|----------|
| 1 | Populated live rows with real combat data (visual) | Phase 11 | Phase 11 is the live-game parity gate (OVL-09); 09-01 confirmed window/monitor/no-crash with no log running. Not a Phase 9 gap. |

### Required Artifacts

| Artifact | Expected | Status | Details |
| -------- | -------- | ------ | ------- |
| `ViewModels/EntryViewModel.cs` | PlayerStats display projection + INPC | ✓ VERIFIED | 92 lines; Name/DPS/DCrit/HPS/HCrit string props, `DpsValue` sort key, `PlayerId`, INPC `Set`; delegates formatting to `EntryFormat`. Wired into `MainViewModel.SyncRows` + XAML DataTemplate. |
| `ViewModels/MainViewModel.cs` | DpsHps→core list off-thread + 1s timer mirror, DPS-desc, IDisposable | ✓ VERIFIED | 146 lines; all of subscription, capture-once dispatcher, SlidingExpirationList, DispatcherQueueTimer, SyncRows reconciliation, Dispose. Constructed in `MainWindow` ctor. |
| `MainWindow.xaml` | ListView x:Bind Rows, 5 columns, +/- buttons | ✓ VERIFIED | ListView `x:Bind ViewModel.Rows`, 5-col header + DataTemplate, ➕/➖ font buttons. |
| `SwtorLogParser.Overlay.WinUi.csproj` | ProjectReference to core | ✓ VERIFIED | Line 38: `<ProjectReference Include="..\SwtorLogParser\SwtorLogParser.csproj" />`. |
| `Settings/OverlaySettings.cs` | X/Y/W/H + FontSize model (Opacity reserved) | ✓ VERIFIED | nullable int X/Y/W/H, `double FontSize` (default 14), reserved `Opacity` (not applied — D-04). |
| `Settings/SettingsService.cs` | corrupt-safe Load/Save at %LocalAppData%\SwtorLogParser\settings.json | ✓ VERIFIED | DefaultPath from `SpecialFolder.LocalApplicationData`; Load/Save catch-all; no `ApplicationData.Current`; no core dependency. |
| `Settings/OverlaySettingsContext.cs` | source-gen JsonSerializerContext | ✓ VERIFIED | `[JsonSerializable(typeof(OverlaySettings))] partial class : JsonSerializerContext`, WriteIndented. |

### Key Link Verification

| From | To | Via | Status | Details |
| ---- | --- | --- | ------ | ------- |
| MainViewModel | CombatLogsMonitor.Instance.DpsHps | `DpsHps.Subscribe(...)` off-thread | ✓ WIRED | `MainViewModel.cs:60` — feeds `_core.AddOrUpdate` with null-id guard + onError. |
| MainViewModel | View.SlidingExpirationList | AddOrUpdate off-thread, Items snapshot on tick | ✓ WIRED | `:51` construct, `:64` AddOrUpdate, `:75` `_core.Items` read on UI tick. |
| MainViewModel | ObservableCollection<EntryViewModel> Rows | DispatcherQueueTimer.Tick mirrors core.Items | ✓ WIRED | `:69-75` timer → `OnRenderTick` → `SyncRows`. |
| MainWindow.xaml.cs | SettingsService | Load on startup / Save on Closed | ✓ WIRED | `:66` Load, `:155` Save. |
| MainWindow.xaml.cs | AppWindow | MoveAndResize / Position+Size | ✓ WIRED | `GetAppWindow()` (`:83-88`), `MoveAndResize` (`:105`), read Position/Size (`:145-148`). |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
| -------- | ------------- | ------ | ------------------ | ------ |
| MainWindow ListView | `ViewModel.Rows` | `SyncRows(_core.Items)` ← `DpsHps.Subscribe` ← `CombatLogsMonitor` Rx stream | ✓ (live frozen stream; not hardcoded/empty) | ✓ FLOWING — Rows are populated only from the real `DpsHps` snapshot via the timer; no static seed. Populated-rows visual deferred to Phase 11 live-game gate. |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
| -------- | ------- | ------ | ------ |
| Settings round-trip + corrupt/missing → defaults | `dotnet test --filter OverlaySettingsServiceTests` | Passed: 5, Failed: 0 (35 ms) | ✓ PASS |

### Probe Execution

No probe scripts declared for this phase (`scripts/*/tests/probe-*.sh` not present). N/A.

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
| ----------- | ----------- | ----------- | ------ | -------- |
| OVL-02 | 09-01 | Overlay subscribes to DpsHps, renders live rows on UI dispatcher, no cross-thread crash, reuses core View types unchanged | ✓ SATISFIED | Truths 1-5; core `View/*` byte-identical since Phase 03-03 (`git diff 1dda7c2 HEAD -- View/` empty). |
| OVL-07 | 09-02 | Persist window position + size (opacity deferred to Phase 10 per D-04) | ✓ SATISFIED (position+size scope) | Truths 7,9,10; JSON at %LocalAppData%\SwtorLogParser\settings.json, source-gen, corrupt-safe, no core dependency. Opacity reserved field present but unapplied — accepted per phase scope. |
| OVL-08 | 09-02 | Increase/decrease font at WinForms parity | ✓ SATISFIED | Truth 6; ➕/➖ buttons adjust rows + header together, size persisted (Truth 8). |

No orphaned requirements: REQUIREMENTS.md maps OVL-02/OVL-07/OVL-08 to Phase 9; all three are claimed by the plans.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
| ---- | ---- | ------- | -------- | ------ |
| (none) | — | No TBD/FIXME/XXX/TODO/PLACEHOLDER in any phase-modified file | — | Clean — completion is auditable. |

Code-review items from 09-REVIEW.md noted but NOT phase blockers (per phase scope): CR-01 (FIXED in `a03f202`); WR-01 frozen-core timer non-disposal, WR-02/WR-03 null-id/tie handling rendered unreachable by CR-01 guard, WR-04 non-atomic write, WR-05 OnActivated check-then-act, IN-01..IN-04 (dead `DpsValue`, opacity clamp note, off-screen restore, test-coverage gap) — all minor/accepted.

### Frozen Core / No-Regression Confirmation

- `SwtorLogParser/View/*` UNCHANGED: `git diff 1dda7c2 HEAD -- SwtorLogParser/View/` is empty; last touch was Phase 03-03 promotion, before Phase 9. Goal's "reusing core View sliding-expiry types unchanged" holds.
- CR-01 fix present in working tree: `MainViewModel.cs:63` `if (stats.Player?.Id is not null)` + `:66` onError — host-side only, frozen core untouched (commit `a03f202`).
- Chore `bf8ee67` touched only Model/Monitor (behavior-preserving style cleanup), NOT View/*; not a Phase 9 logic change.

### Human Verification Required

None outstanding. Both planned human-verify checkpoints were executed during the phase and CONFIRMED:
- 09-01: overlay launched, window opened, monitor started, NO cross-thread COMException (no combat log running).
- 09-02: position+size+font restore across restart CONFIRMED; corrupt/missing settings → defaults without crash CONFIRMED.

The only remaining live-visual (populated rows with real combat data) is the Phase 11 live-game parity gate — recorded as a Deferred Item, not a Phase 9 human-verify requirement.

### Gaps Summary

No gaps. All 10 must-have truths are verified against the codebase, all 7 artifacts exist and are substantive and wired, all 5 key links are wired, the live data path flows from the real frozen `DpsHps` stream, requirements OVL-02/OVL-07(pos+size)/OVL-08 are satisfied, the frozen core `View/*` is byte-identical, the CR-01 blocker fix is present, and the settings robustness behavior passes a live 5/5 unit-test run. The one not-yet-visible item (populated rows under real combat) is deliberately scheduled for the Phase 11 live-game parity gate.

---

_Verified: 2026-06-12_
_Verifier: Claude (gsd-verifier)_
