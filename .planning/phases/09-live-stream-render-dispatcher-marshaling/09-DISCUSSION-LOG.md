# Phase 9: Live Stream Render + Dispatcher Marshaling - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-06-12
**Phase:** 9-Live Stream Render + Dispatcher Marshaling
**Areas discussed:** Render cadence, Row sort order, Persistence scope, Font control UX

---

## Render cadence

| Option | Description | Selected |
|--------|-------------|----------|
| 1s timed UI tick | Off-thread aggregate into core list; DispatcherQueueTimer mirrors snapshot once/sec | ✓ |
| 500ms timed tick | Same pattern, snappier refresh, more churn | |
| Per-update immediate | Marshal every OnNext to the UI list (WinForms parity) | |

**User's choice:** 1s timed UI tick
**Notes:** Matches research-locked pattern + WinForms aggregate/render split; lowest UI churn and TryEnqueue traffic.

---

## Row sort order

| Option | Description | Selected |
|--------|-------------|----------|
| DPS descending | Top damage first; healers (DPS~0) sink to bottom | ✓ |
| By active metric (max DPS/HPS) | Rank by dominant metric so healers sort by HPS | |
| Local player pinned, rest by DPS | Your character always top | |
| Player-id order (parity) | WinForms SortedList-by-id order | |

**User's choice:** DPS descending
**Notes:** Standard DPS-meter behavior; healers-to-bottom accepted. Render-time sort over the snapshot; core list stays keyed by Player.Id.

---

## Persistence scope

| Option | Description | Selected |
|--------|-------------|----------|
| Pos+size now, opacity in Phase 10 | Persist position+size (save on close); opacity control+persistence deferred to Phase 10 | ✓ |
| Pos+size+opacity now, save on close | Add a basic opacity control in Phase 9 too | |
| Pos+size+opacity, save on change (debounced) | Persist all three, debounced writes | |

**User's choice:** Pos+size now, opacity in Phase 10
**Notes:** Local JSON at %LocalAppData%\SwtorLogParser\settings.json (unpackaged → no ApplicationData.Current). Save-on-close. Splits OVL-07 across phases: pos+size (P9) / opacity (P10).

---

## Font control UX

| Option | Description | Selected |
|--------|-------------|----------|
| +/- buttons (parity) | Keep WinForms cyan ➕/➖ buttons; persist size | ✓ |
| Buttons + Ctrl+MouseWheel | Buttons plus Ctrl+scroll | |
| Ctrl+MouseWheel only | Drop buttons; scroll-to-resize | |

**User's choice:** +/- buttons (parity)
**Notes:** Persist chosen font size with the window settings.

## Claude's Discretion

- MVVM shape (`ObservableCollection<EntryViewModel>` + `x:Bind` `ListView`/`ItemsRepeater`), provided it honors off-thread aggregate / UI-thread mirror and reuses core View types unchanged.
- Column formatting / number formatting / empty-state handling / settings JSON schema.

## Deferred Ideas

- Opacity control + opacity persistence → Phase 10.
- Ctrl+MouseWheel / hotkey font resize → future polish.
- "By active metric" sort + local-player pinning → considered, not chosen.
- Borderless / transparency / click-through / drag / topmost → Phase 10 (roadmap).
