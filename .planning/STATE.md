---
gsd_state_version: 1.0
milestone: v1.1
milestone_name: WinUI 3 Overlay & Dev Tooling
status: executing
stopped_at: Completed 09-02-PLAN.md
last_updated: "2026-06-12T10:27:00.000Z"
last_activity: 2026-06-12 -- Completed Phase 9 (09-02: font controls + settings persistence; OVL-07 pos+size, OVL-08)
progress:
  total_phases: 6
  completed_phases: 2
  total_plans: 4
  completed_plans: 4
  percent: 100
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-06-12)

**Core value:** The live DPS/HPS stats pipeline stays correct and reliable while the codebase becomes safe to maintain and extend — no regressions to parsing or the reactive stream.
**Current focus:** Phase 9 — Live Stream Render + Dispatcher Marshaling

## Current Position

Phase: 9 (Live Stream Render + Dispatcher Marshaling) — COMPLETE
Plan: 2 of 2 (all plans complete)
Status: Phase 9 complete (09-01 + 09-02); ready for Phase 10
Last activity: 2026-06-12 -- Completed 09-02 (font +/- controls + settings persistence: OVL-07 pos+size, OVL-08)

Progress: [██████████] 100% (Phase 9: 2/2 plans)

## Performance Metrics

**Velocity:**

- Total plans completed: 4 (this milestone)
- Average duration: —
- Total execution time: 0 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 8 | 2 | - | - |

**Recent Trend:**

- Last 5 plans: —
- Trend: —

*Updated after each plan completion*
| Phase 8 P01 | 3min | 2 tasks | 9 files |
| Phase 8 P02 | 25min | 3 tasks | 2 files |
| Phase 9 P01 | 3min | 3 tasks | 6 files |
| Phase 9 P02 | 3min | 3 tasks | 7 files |

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
- [Phase ?]: [08-01] WinUI overlay csproj resolves a default RuntimeIdentifier so WindowsAppSDKSelfContained builds RID-less (keeps .slnx/CI green)
- [Phase ?]: [08-01] WinAppSDK 2.2.0 + CsWin32 0.3.275 pinned in CPM but referenced only from SwtorLogParser.Overlay.WinUi (core + Native CLI stay AOT-clean)
- [Phase ?]: [08-02] Headless windows-latest restores+builds the unpackaged self-contained WinUI 3 project with no dotnet workload step needed in CI
- [Phase ?]: [08-02] Native AOT publish IL-analysis warning-free (0 IL2xxx/IL3xxx); WinAppSDK/CsWin32 absent from Native.Cli graph — AOT-contamination boundary holds
- [quick-260612-dso]: APPROVED exception to the FROZEN-core-parser decision: fixed Value.Parse outer-paren scope (absorb/shield Total bug — outer damage 133 not inner absorbed 149) + switched damage-type/result detection to the numeric {id} (locale-robust, plain AOT-safe switch); added Value.Absorbed int?. This intentionally changes live DPS for absorb/shield hits. `~` effective-HPS remains OUT OF SCOPE (deferred).
- [Phase ?]: [09-01] Off-thread DpsHps aggregation in reused core SlidingExpirationList(10s); 1s DispatcherQueueTimer mirrors into ObservableCollection (DPS-desc, null/zero last); OnNext never touches XAML — no cross-thread COMException 0x8001010E
- [Phase ?]: [09-01] OVL-02 satisfied at no-crash-launch level (window opens, monitor starts, marshaling holds); live-data visual confirmation of rows rendering/expiring deferred to Phase 11 parity gate
- [Phase ?]: [09-01] Native AOT IL analysis warning-free (overlay->core ProjectReference did not contaminate core/Native.Cli AOT graph); native-link MSB3073 is env-gated (MSVC not on shell PATH), CI-covered
- [Phase ?]: [09-02] Overlay persists window pos+size+font via corruption-safe local JSON at %LocalAppData%\SwtorLogParser\settings.json (source-gen System.Text.Json; missing/corrupt → defaults, never throws); load-on-startup/save-on-close via managed AppWindow.MoveAndResize — no CsWin32 interop, no core dependency. OVL-07 pos+size + OVL-08 confirmed end-to-end (human-verify APPROVED)
- [Phase ?]: [09-02] OVL-07 opacity persistence DEFERRED to Phase 10 (D-04 scope split); reserved Opacity field on OverlaySettings exists for forward-compat but is not controlled/applied this phase

### Pending Todos

None yet.

### Blockers/Concerns

- Phase 10 (transparency/click-through/BL-01): highest-risk area — WinUI 3 has no `TransparencyKey`; de-risk via a throwaway spike (OVL-03) before the production grid styling
- Phase 10: `SetWinEventHook` callback must be **static + delegate kept alive in a static field** or it crashes (AccessViolation)
- Phase 9: first live update must marshal to the captured UI `DispatcherQueue` or cross-thread `COMException 0x8001010E` crashes the overlay
- Phase 8 + every overlay phase: re-run Native AOT CLI publish + `.slnx` build as a regression gate (AOT-contamination guard)
- Phase 12 (MSTest): MTP silently breaks coverlet coverage — edit `.github/workflows/*` in the same change
- Hard limit: overlay cannot cover **exclusive fullscreen** — document Fullscreen-Windowed requirement (DOCS-01)

### Quick Tasks Completed

| # | Description | Date | Commit | Status | Directory |
|---|-------------|------|--------|--------|-----------|
| 260612-czd | Benchmark + optimize CombatLogLine.Parse allocations (−55% pure parse; span-keyed cache lookup + lazy sub-parsing) | 2026-06-12 | 72811c8 |  | [260612-czd-benchmark-optimize-combatlogline-parse-a](./quick/260612-czd-benchmark-optimize-combatlogline-parse-a/) |
| 260612-dso | Fix Value.Parse outer-paren absorb bug + id-based damage-type detection (correctness; breaks v1.1 core freeze w/ approval — absorb DPS now counts outer damage) | 2026-06-12 | 093381d | Verified | [260612-dso-fix-value-parse-outer-paren-absorb-bug-i](./quick/260612-dso-fix-value-parse-outer-paren-absorb-bug-i/) |

## Deferred Items

| Category | Item | Status | Deferred At |
|----------|------|--------|-------------|
| *(none for v1.1)* | | | |

## Session Continuity

Last session: 2026-06-12T10:27:00.000Z
Stopped at: Completed 09-02-PLAN.md (Phase 9 complete)
Resume file: None
