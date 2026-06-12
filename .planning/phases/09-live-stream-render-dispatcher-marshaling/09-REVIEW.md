---
phase: 09-live-stream-render-dispatcher-marshaling
reviewed: 2026-06-12T00:00:00Z
depth: standard
files_reviewed: 9
files_reviewed_list:
  - SwtorLogParser.Overlay.WinUi/ViewModels/MainViewModel.cs
  - SwtorLogParser.Overlay.WinUi/ViewModels/EntryViewModel.cs
  - SwtorLogParser.Overlay.WinUi/ViewModels/EntryFormat.cs
  - SwtorLogParser.Overlay.WinUi/Settings/SettingsService.cs
  - SwtorLogParser.Overlay.WinUi/Settings/OverlaySettings.cs
  - SwtorLogParser.Overlay.WinUi/Settings/OverlaySettingsContext.cs
  - SwtorLogParser.Overlay.WinUi/MainWindow.xaml.cs
  - SwtorLogParser.Overlay.WinUi/MainWindow.xaml
  - SwtorLogParser.Tests/OverlaySettingsServiceTests.cs
findings:
  critical: 1
  warning: 5
  info: 4
  total: 10
status: issues_found
---

# Phase 9: Code Review Report

**Reviewed:** 2026-06-12
**Depth:** standard
**Files Reviewed:** 9
**Status:** issues_found

## Summary

Phase 9 wires the WinUI 3 overlay to the live `CombatLogsMonitor.Instance.DpsHps` Rx stream
and adds JSON settings persistence + font controls. The core dispatcher-marshaling design is
sound: the Rx `OnNext` handler touches only the internally-locked core `SlidingExpirationList`,
a UI-thread `DispatcherQueueTimer` mirrors the snapshot into the `ObservableCollection`, and the
`SyncRows` reconciliation (remove-stale / upsert-in-order via `Move`/`Insert`) was traced through
reorder and insert cases and produces correct DPS-descending ordering without flicker. The
`DispatcherQueue` is captured once on the UI thread, and the subscription + render timer are
disposed on `Closed`. `SettingsService` is robust against missing/corrupt files and never throws.

The findings below concern (1) a real off-UI-thread mutation hazard hidden inside the core
`SlidingExpirationList.AddOrUpdate` that the phase's "OnNext only touches the locked core list"
contract relies on but does not actually hold safe against malformed actors; (2) a leaked
`System.Threading.Timer` inside the per-VM `SlidingExpirationList` that `Dispose` does not release,
directly contradicting the "no leaked timer" claim; (3) collision of null-id rows in `SyncRows`;
and assorted robustness / dead-code items.

## Critical Issues

### CR-01: `OnNext` can throw on a background thread when a player actor has a null `Id`, faulting the Rx subscription

**File:** `SwtorLogParser.Overlay.WinUi/ViewModels/MainViewModel.cs:55`
(call chain into `SwtorLogParser/View/SlidingExpirationList.cs:34,50,53,67`)

**Issue:** The subscription is `monitor.DpsHps.Subscribe(stats => _core.AddOrUpdate(stats))`. The
phase contract states `OnNext` "does the ONLY allowed off-thread work — feeding the internally-locked
core list" and must never throw off the UI thread. But `SlidingExpirationList.AddOrUpdate`
dereferences `item.Player.Id!.Value` (null-forgiving) at three sites
(`SlidingExpirationList.cs:34`, `:50`, `:53`). `Actor.Id` is `long?` and is documented as
"Null only for malformed actors" (`EntryViewModel.cs:31`). The producer's
`CalculateDpsHpsStats` sets `Player = player!` from `line.Source` without guaranteeing a non-null
`Id`. If any `PlayerStats` arrives with `Player.Id == null`, `Id!.Value` throws
`InvalidOperationException` on the background reader thread. Because this exception propagates out
of an Rx `OnNext` with no `onError` handler supplied to `Subscribe`, the observer pipeline faults:
the subscription terminates and the overlay silently stops receiving all further stats (not just
the bad row). This is a correctness/data-loss defect for the live stream — the entire render
pipeline dies on a single malformed actor, and it happens off the UI thread where it is hardest to
observe.

The new `EntryViewModel`/`SyncRows` code is carefully null-tolerant of `Player?.Id`
(`MainViewModel.cs:79`, `EntryViewModel.cs:27`), which proves the authors expect null ids to be
possible — yet the feed path into `_core` is not, and there is no `onError`/guard.

**Fix:** Guard the feed and supply an error handler so a single bad row cannot kill the stream:

```csharp
_sub = monitor.DpsHps.Subscribe(
    onNext: stats =>
    {
        // Drop malformed actors instead of faulting the whole subscription off-thread.
        if (stats?.Player?.Id is null) return;
        _core.AddOrUpdate(stats);
    },
    onError: ex =>
    {
        // Last-resort: never let an OnNext exception silently tear down the live stream.
        System.Diagnostics.Debug.WriteLine($"DpsHps stream faulted: {ex}");
    });
```

(Preferably also harden `SlidingExpirationList.AddOrUpdate` to skip null-id items, but at minimum
guard at the subscription boundary owned by this phase.)

## Warnings

### WR-01: Per-`MainViewModel` `SlidingExpirationList` leaks a `System.Threading.Timer`; `Dispose` does not release it (contradicts the "no leaked timer" claim)

**File:** `SwtorLogParser.Overlay.WinUi/ViewModels/MainViewModel.cs:51,126-134`
(leak source: `SwtorLogParser/View/SlidingExpirationList.cs:9,16`)

**Issue:** The ctor creates a fresh `_core = new SlidingExpirationList(...)` (`:51`). That type owns a
`System.Threading.Timer` (`SlidingExpirationList.cs:16`) and is **not** `IDisposable`, so its timer
is never stopped. `MainViewModel.Dispose` (`:126-134`) disposes `_sub` and stops `_renderTimer`,
but never releases `_core`'s expiration timer. The XML doc and inline comments assert
"no leaked subscription/timer" / "IN-03" — that claim is false for the core list's timer. The
timer holds a rooted callback (`RemoveExpiredItems`) referencing `_core`, keeping the whole VM
graph (including the now-disposed subscription's captured state) alive for the process lifetime.
For a single-window overlay the practical impact is bounded, but the resource is genuinely leaked
and the documented guarantee is incorrect.

**Fix:** Make `SlidingExpirationList` implement `IDisposable` (dispose the `Timer`) and dispose
`_core` in `MainViewModel.Dispose`:

```csharp
// SlidingExpirationList.cs
public sealed class SlidingExpirationList : IDisposable
{
    ...
    public void Dispose() => _expirationTimer.Dispose();
}

// MainViewModel.Dispose()
_sub.Dispose();
_renderTimer.Stop();
_renderTimer.Tick -= OnRenderTick;
_core.Dispose();
```

### WR-02: Two rows with null `PlayerId` collide and overwrite each other in `SyncRows`

**File:** `SwtorLogParser.Overlay.WinUi/ViewModels/MainViewModel.cs:90-114`

**Issue:** The upsert matches existing rows via `Rows[j].PlayerId == id` (`:98`). When `id` is null
(malformed actor) and an existing row also has `PlayerId == null`, `null == null` is `true`, so the
loop treats two distinct null-id players as the same row — the second is folded onto the first and
its stats are lost. Conversely, the stale-removal pass (`:78-87`) only adds non-null ids to
`liveIds` (`:79`), so any null-id row is unconditionally removed every tick (`:85`,
`rowId is null` → removed), then potentially re-inserted — causing churn. The net behavior for
null-id actors is incoherent. (Note: CR-01's guard, if applied to drop null-id rows at the feed,
makes this path unreachable and is the cleaner fix; absent that, this is a live defect.)

**Fix:** Either drop null-id stats at the source (see CR-01), or skip null-id rows explicitly in the
upsert so they cannot collide:

```csharp
var id = stats.Player?.Id;
if (id is null) continue; // never reconcile keyless rows by null-equality
```

### WR-03: `OrderByDescending` is not a stable tiebreaker; equal-DPS rows can swap positions every tick, causing visible row churn

**File:** `SwtorLogParser.Overlay.WinUi/ViewModels/MainViewModel.cs:122-124`

**Issue:** `OrderByDpsDescending` orders solely by `s.DPS ?? 0d`. The XML doc on this method claims
"Stable" (`:120`), and `OrderByDescending` is indeed a stable sort *for a fixed input order* — but
the input is `_core.Items`, materialized from a `SortedList` keyed by `Player.Id` whose value order
is stable, so in practice ties hold. However, all zero/null-DPS rows (e.g. heal-only players, or
players between combat ticks) collapse to key `0d` and there is no secondary sort key
(name or id). Combined with `SyncRows` `Move` calls, any reordering of equal-DPS rows produces
flicker the in-place reconciliation was specifically designed to avoid. A deterministic tiebreaker
removes the risk.

**Fix:** Add a stable secondary key:

```csharp
snapshot
    .OrderByDescending(s => s.DPS ?? 0d)
    .ThenBy(s => s.Player?.Id ?? long.MaxValue);
```

### WR-04: Settings save is non-atomic — a crash or full disk mid-write truncates `settings.json`, and the next load silently resets to defaults

**File:** `SwtorLogParser.Overlay.WinUi/Settings/SettingsService.cs:101-102`

**Issue:** `Save` calls `File.WriteAllText(_path, json)` directly over the live settings file. If the
process is killed (or the disk fills) between truncation and completion, the file is left empty or
partially written. `Load` then hits the `catch` and returns defaults (`:81-85`) — the user's saved
window placement and font size are silently lost. The `catch` in `Save` (`:104-107`) also swallows
the failure, so the user gets no signal that persistence failed. Window-close is exactly the moment
the OS may be tearing the process down, making a mid-write interruption plausible.

**Fix:** Write to a temp file and atomically replace:

```csharp
var tmp = _path + ".tmp";
File.WriteAllText(tmp, json);
File.Move(tmp, _path, overwrite: true);
```

### WR-05: `OnActivated` monitor-start has a check-then-act race and ignores the activation guard ordering

**File:** `SwtorLogParser.Overlay.WinUi/MainWindow.xaml.cs:109-119`

**Issue:** `_monitorStarted` is set to `true` (`:116`) *before* checking
`CombatLogsMonitor.Instance.IsRunning` and calling `Start` (`:117-118`). `Activated` events are
delivered on the UI thread so the `_monitorStarted` flag itself is not racy, but the guard order is
fragile: `_monitorStarted` is committed even though the actual `Start` is conditional on
`!IsRunning`. If `IsRunning` is transiently `true` (started elsewhere) at first activation, the flag
latches `true` and the window will never (re)start the monitor even if that other owner later stops
it — the overlay then renders nothing with no recovery path. The WinForms parity comment claims it
"start[s] only if not already running," but the latched flag changes the semantics. Lower severity
because in the single-host overlay the monitor is owned here, but the guard logic is incorrect as
written.

**Fix:** Only latch the flag once the monitor is actually owned by this window, e.g.:

```csharp
if (_monitorStarted) return;
if (args.WindowActivationState == WindowActivationState.Deactivated) return;
if (CombatLogsMonitor.Instance.IsRunning) return; // someone else owns it; don't latch
_monitorStarted = true;
CombatLogsMonitor.Instance.Start(CancellationToken.None);
```

## Info

### IN-01: `EntryViewModel.DpsValue` is dead code — computed and notified but never consumed

**File:** `SwtorLogParser.Overlay.WinUi/ViewModels/EntryViewModel.cs:22,64-69,81`
and `EntryFormat.cs:30-31`

**Issue:** `DpsValue` (and its backing `_dpsValue`, the `EntryFormat.DpsSortKey` helper, and the
`Set(ref _dpsValue, ...)` in `Apply`) exist to provide a numeric sort key, but sorting is actually
performed by `MainViewModel.OrderByDpsDescending` over `PlayerStats` (`MainViewModel.cs:122-124`),
not over `EntryViewModel`. `DpsValue` is never read by any binding (the XAML binds only
`Name/DPS/DCrit/HPS/HCrit`) or by the ordering code. It is pure dead weight that raises spurious
`PropertyChanged` notifications. Remove `DpsValue`, `_dpsValue`, and `EntryFormat.DpsSortKey`, or
wire the ordering to use it — but not both ordering mechanisms.

**Fix:** Delete `DpsValue`/`_dpsValue` from `EntryViewModel`, drop the `DpsValue = ...` line in
`Apply` (`:81`), and remove `EntryFormat.DpsSortKey`.

### IN-02: `OverlaySettings.Opacity` is never validated/clamped on load (forward-compat field accepted blindly)

**File:** `SwtorLogParser.Overlay.WinUi/Settings/OverlaySettings.cs:40`

**Issue:** `Opacity` is reserved for Phase 10 and not applied now — fine. But it round-trips an
arbitrary persisted `double?` with no range constraint. When Phase 10 starts consuming it, a
hand-edited or corrupt value (e.g. `-5`, `1e9`, `NaN`) will flow straight through `Load` (which only
guards against deserialization failure, not value sanity). Worth noting now so Phase 10 clamps on
read rather than trusting the field.

**Fix:** When Phase 10 applies opacity, clamp to `[0,1]` on read; consider a brief comment on the
field documenting the expected range.

### IN-03: Off-screen restored window placement is intentionally not clamped — note for usability

**File:** `SwtorLogParser.Overlay.WinUi/MainWindow.xaml.cs:99-106`

**Issue:** `ApplySavedSettings` restores a saved `RectInt32` with only `w > 0 && h > 0` guards
(`:103`). The code comment explicitly accepts off-screen placement this phase. Flagged only as a
known gap: if the monitor layout changes between sessions, the overlay can restore fully off-screen
with no on-screen recovery affordance (the window has a standard title bar this phase, so it is at
least reachable via the taskbar). Acceptable per phase scope; record for Phase 10 hardening.

**Fix:** In a later phase, clamp the restored rect against the nearest `DisplayArea` work area.

### IN-04: Test suite does not cover the malformed-actor / null-id path or `SyncRows` ordering

**File:** `SwtorLogParser.Tests/OverlaySettingsServiceTests.cs` (whole file)

**Issue:** The settings round-trip/corruption tests are solid and exercise the real source files.
However, the highest-risk Phase 9 logic — `MainViewModel.SyncRows` reconciliation, DPS-descending
ordering with ties, and the null-`Player.Id` feed path (CR-01/WR-02) — has no tests, despite
`OrderByDpsDescending` and `SyncRows` being deliberately authored as UI-thread-free and
unit-testable (`MainViewModel.cs:72,118-124` doc comments explicitly invite this). The absence
leaves the most defect-prone code unguarded.

**Fix:** Add unit tests over `MainViewModel.OrderByDpsDescending` (ties, null DPS) and `SyncRows`
(insert / reorder / remove, and a null-id `PlayerStats`) using a plain `ObservableCollection` and
synthetic `PlayerStats`/`Actor` fixtures.

---

_Reviewed: 2026-06-12_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
