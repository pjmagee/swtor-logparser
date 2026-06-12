---
gsd_state_version: 1.0
milestone: v1.1
milestone_name: WinUI 3 Overlay & Dev Tooling
status: planning
last_updated: "2026-06-12T08:20:00.000Z"
last_activity: 2026-06-12
progress:
  total_phases: 6
  completed_phases: 0
  total_plans: 0
  completed_plans: 0
  percent: 0
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-06-12)

**Core value:** The live DPS/HPS stats pipeline stays correct and reliable while the codebase becomes safe to maintain and extend — no regressions to parsing or the reactive stream.
**Current focus:** Phase 8 — WinUI 3 Scaffold + Dependencies + Guardrails

## Current Position

Phase: 8 of 13 (WinUI 3 Scaffold + Dependencies + Guardrails) — first phase of v1.1
Plan: — of — (roadmap created, not yet planned)
Status: Ready to plan
Last activity: 2026-06-12 — v1.1 roadmap created (Phases 8-13, 17 requirements mapped)

Progress: [░░░░░░░░░░] 0%

## Performance Metrics

**Velocity:**

- Total plans completed: 0 (this milestone)
- Average duration: —
- Total execution time: 0 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| - | - | - | - |

**Recent Trend:**

- Last 5 plans: —
- Trend: —

*Updated after each plan completion*

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- [v1.1]: Replace WinForms overlay with **WinUI 3** (not MAUI) — modern Windows-native UI; overlay isn't AOT-constrained
- [v1.1]: **Same-name replace** — keep `SwtorLogParser.Overlay` identity; internals wholly new; delete WinForms only after parity
- [v1.1]: **Parity-before-deletion is a HARD gate** (Phase 11) — WinForms keeps building/running until WinUI 3 reaches parity
- [v1.1]: Adopt **Microsoft.Windows.CsWin32** (source-generated P/Invoke) for all overlay interop — closes #3; overlay-only references (no AOT contamination)
- [v1.1]: Migrate xUnit → **MSTest .NET SDK**; set `UseVSTest=true` to preserve coverlet CI coverage (decoupled from overlay path)
- [v1.1]: Core parser + `CombatLogsMonitor.Instance.DpsHps` stream are **FROZEN** — no phase changes them

### Pending Todos

None yet.

### Blockers/Concerns

- Phase 10 (transparency/click-through/BL-01): highest-risk area — WinUI 3 has no `TransparencyKey`; de-risk via a throwaway spike (OVL-03) before the production grid styling
- Phase 10: `SetWinEventHook` callback must be **static + delegate kept alive in a static field** or it crashes (AccessViolation)
- Phase 9: first live update must marshal to the captured UI `DispatcherQueue` or cross-thread `COMException 0x8001010E` crashes the overlay
- Phase 8 + every overlay phase: re-run Native AOT CLI publish + `.slnx` build as a regression gate (AOT-contamination guard)
- Phase 12 (MSTest): MTP silently breaks coverlet coverage — edit `.github/workflows/*` in the same change
- Hard limit: overlay cannot cover **exclusive fullscreen** — document Fullscreen-Windowed requirement (DOCS-01)

## Deferred Items

| Category | Item | Status | Deferred At |
|----------|------|--------|-------------|
| *(none for v1.1)* | | | |

## Session Continuity

Last session: 2026-06-12T08:20:00Z
Stopped at: v1.1 ROADMAP.md created (Phases 8-13); REQUIREMENTS.md traceability filled
Resume file: None
