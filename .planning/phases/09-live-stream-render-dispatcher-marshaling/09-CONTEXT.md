# Phase 9: Live Stream Render + Dispatcher Marshaling - Context

**Gathered:** 2026-06-12
**Status:** Ready for planning

<domain>
## Phase Boundary

The WinUI 3 overlay (`SwtorLogParser.Overlay.WinUi`, scaffolded in Phase 8) subscribes to the frozen `CombatLogsMonitor.Instance.DpsHps` stream and renders live per-player rows on the UI thread with no cross-thread crash, reusing the core `SwtorLogParser.View` sliding-expiry types unchanged. Includes parity font controls and cross-run persistence of window position + size. Covers **OVL-02, OVL-07 (position + size only — see scope note), OVL-08**.

**In scope:** stream subscription + UI-thread marshaling; live 5-column render (Player / DPS / Crit% / HPS / Crit%); 10s sliding expiry via the core list; row sort; font +/- controls; persist window position + size.

**Out of scope (Phase 10):** transparency, borderless chrome, click-through, drag-to-move, topmost re-assert, **and the opacity control + its persistence**. This phase's window is a **normal, opaque, movable window** (standard title bar is fine) so the live-render integration can be made correct before the native window styling lands.
</domain>

<decisions>
## Implementation Decisions

### Render cadence (OVL-02)
- **D-01:** Use a **1-second timed UI tick**, not per-`OnNext` rendering. The `DpsHps` subscription feeds the core `SlidingExpirationList.AddOrUpdate` **off-thread** (the list is already internally locked); a `DispatcherQueueTimer` running on the UI thread (~1s) reads the `Items` snapshot and mirrors it into the bound collection. This preserves the WinForms aggregate/render split, keeps UI churn low, and minimizes `DispatcherQueue.TryEnqueue` traffic.
- **D-02:** Every mutation of UI-bound state happens on the captured UI `DispatcherQueue` — never touch XAML from the Rx background reader thread (prevents `COMException 0x8001010E`). The off-thread path only touches the locked core list, not XAML.

### Row sort order (OVL-02)
- **D-03:** Sort rows by **DPS descending** (standard DPS-meter behavior). Players with no/zero DPS (e.g. pure healers) sort to the bottom. This is a render-time sort over the `Items` snapshot; it does NOT change the core list (which remains keyed by `Player.Id`).

### Persistence (OVL-07)
- **D-04:** Persist **window position + size only** this phase. **Opacity persistence is deferred to Phase 10** (it belongs with the opacity/transparency control). This is a deliberate split of OVL-07 across phases — Phase 9 = position + size; Phase 10 = opacity. Verifier should not treat missing opacity persistence as a Phase 9 gap.
- **D-05:** Store settings as a **local JSON file** under `%LocalAppData%\SwtorLogParser\settings.json` (the app is unpackaged, so `ApplicationData.Current` is not used). System.Text.Json, source-generated context preferred (keeps the door open for AOT-friendliness, though this host is not AOT).
- **D-06:** **Save on window close** (not debounced-on-change). Load on startup; if the file is missing/corrupt, fall back to a sensible default position/size without throwing.

### Font controls (OVL-08)
- **D-07:** Keep the **WinForms parity +/- buttons** (cyan ➕/➖ today) to increase/decrease the row/header font size. Persist the chosen font size alongside the window settings (so it restores across runs). No Ctrl+scroll / hotkey this phase.

### Claude's Discretion
- The exact MVVM shape — an `ObservableCollection<EntryViewModel>` (or equivalent) bound via `x:Bind` to a `ListView`/`ItemsRepeater`, with a thin `EntryViewModel` projecting `PlayerStats` for display — is left to research/planning, provided it honors D-01/D-02 (off-thread aggregate, UI-thread mirror) and reuses the core `SlidingExpirationList`/`Entry` unchanged.
- Column formatting (number formatting, crit% display, column widths/headers) at reasonable parity with the WinForms grid; exact styling is fine to choose.
- Empty state (no active combat / empty list) handling — a blank list is acceptable; a subtle placeholder is optional.
- Settings JSON schema/versioning details.
</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase 9 research (milestone-level)
- `.planning/research/ARCHITECTURE.md` — the Rx→`DispatcherQueue` marshaling pattern, the aggregate/render split, the new-vs-modified component list, and the `MainViewModel`/`EntryViewModel` shape
- `.planning/research/PITFALLS.md` §3 — cross-thread `COMException 0x8001010E` and the capture-dispatcher-once / `TryEnqueue` prevention
- `.planning/research/SUMMARY.md` — overall build order and the frozen-stream constraint

### Parity baseline + core types (read before implementing)
- `SwtorLogParser.Overlay/ParserForm.cs` — the WinForms overlay being matched: 5-column grid (Player/DPS/Crit%/HPS/Crit%), 10s expiry, per-`OnNext` `AddOrUpdate`, cyan +/- font buttons, `Opacity=0.5`. Render parity reference (NOT to be modified — it's the safety net until Phase 11)
- `SwtorLogParser/View/SlidingExpirationList.cs` — the **core, UI-free** list to reuse unchanged: `AddOrUpdate(PlayerStats)` (internally locked, keyed by `Player.Id`), `Items` (immutable `PlayerStats` snapshot), 10s `Timer`-based expiry
- `SwtorLogParser/View/Entry.cs` — core `Entry { Stats, Expiration }`
- `SwtorLogParser/Monitor/CombatLogsMonitor.cs` — the producer: `Instance`, `DpsHps` (`IObservable<PlayerStats>`), `PlayerStats` (Player, DPS, DPSCritP, HPS, HPSCritP), `Start`/`IsRunning` (overlay starts the monitor on activation, as `ParserForm.OnActivated` does)
- `SwtorLogParser.Overlay.WinUi/MainWindow.xaml(.cs)` — the Phase 8 empty-window scaffold to build the render into
</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- **Core `SwtorLogParser.View.SlidingExpirationList`** — reuse as-is for off-thread aggregation + 10s expiry; exposes `Items` snapshot for the UI tick to mirror.
- **`CombatLogsMonitor.Instance.DpsHps`** — the single seam; subscribe and `Dispose` on window close. Start the monitor on window activation (mirror `ParserForm.OnActivated` → `_monitor.Start(...)` when `!IsRunning`).
- **WinForms `ParserForm`** — the column set, expiry window, and font-button behavior to match (NOT referenced/compiled by the WinUI project; read for parity only).

### Established Patterns
- Frozen core: no changes to the parser, `CombatLogsMonitor`, `PlayerStats`, or the core `View` types. The overlay is a pure consumer.
- The WinForms host marshals via `Control.Invoke` + a render path; the WinUI analogue is `DispatcherQueue.TryEnqueue` + a `DispatcherQueueTimer` tick.

### Integration Points
- New WinUI view-model subscribes to `DpsHps`; off-thread → core list; UI tick → bound `ObservableCollection`; XAML `ListView` renders.
- New `settings.json` read/write under `%LocalAppData%\SwtorLogParser\`.
</code_context>

<specifics>
## Specific Ideas

- Match the WinForms 5-column layout: **Player / DPS / Crit% / HPS / Crit%**, 10-second sliding expiry.
- DPS-meter ordering (highest DPS on top); healers fall to the bottom — accepted tradeoff.
- Window stays opaque and movable via the normal title bar this phase; the "real" borderless transparent overlay is Phase 10.
</specifics>

<deferred>
## Deferred Ideas

- **Opacity control + opacity persistence** → Phase 10 (with the layered-window transparency work). The settings file may include an `opacity` field now, but the control and its application land in Phase 10.
- **Ctrl+MouseWheel / hotkey font resizing** → possible future polish; Phase 9 uses +/- buttons only.
- **"By active metric" sort / local-player pinning** → considered; not chosen. Could revisit as overlay polish later.
- Borderless chrome, click-through, drag-to-move, topmost re-assert → Phase 10 (per roadmap).
</deferred>

---

*Phase: 9-Live Stream Render + Dispatcher Marshaling*
*Context gathered: 2026-06-12*
