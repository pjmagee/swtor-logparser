# Roadmap: SWTOR Log Parser

## Milestones

- ✅ **v1.0 Hardening (+ .NET 10)** — Phases 1-7 (shipped 2026-06-12) — see [milestones/v1.0-ROADMAP.md](milestones/v1.0-ROADMAP.md)
- 🚧 **v1.1 WinUI 3 Overlay & Dev Tooling** — Phases 8-13 (in progress)

## Phases

<details>
<summary>✅ v1.0 Hardening (Phases 1-7) — SHIPPED 2026-06-12</summary>

- [x] Phase 1: Parser Safety Net — characterization/golden tests (TEST-03)
- [x] Phase 2: Correctness Bugs — BUG-01..07 (+ Latin-1 encoding UAT fix)
- [x] Phase 3: Monitor Refactor + Coverage — RFCT-01..03, TEST-01/02
- [x] Phase 4: Performance — PERF-01..03
- [x] Phase 5: Dependency Upgrades — DEP-01..03, INFRA-02
- [x] Phase 6: .NET 10 Upgrade — PLAT-01 (closed issue #1)
- [x] Phase 7: CI Pipeline — INFRA-01 (CI green on main)

Full detail: [milestones/v1.0-ROADMAP.md](milestones/v1.0-ROADMAP.md) · audit: [milestones/v1.0-MILESTONE-AUDIT.md](milestones/v1.0-MILESTONE-AUDIT.md)

</details>

### 🚧 v1.1 WinUI 3 Overlay & Dev Tooling (In Progress)

**Milestone Goal:** Replace the WinForms overlay with a WinUI 3 overlay at parity (transparent, click-through, draggable, live DPS/HPS render, topmost over Fullscreen-Windowed via CsWin32), then retire WinForms — and modernize the toolchain (xUnit → MSTest .NET SDK, VSCode launch/tasks, refreshed docs). The core parser and the live `CombatLogsMonitor.Instance.DpsHps` stream stay **frozen**.

**Critical path (overlay):** Phase 8 scaffold → Phase 9 stream/render (integration crux) → Phase 10 CsWin32 interop → **Phase 11 HARD parity gate + WinForms deletion**. Each native behavior needs a live window to style, so render precedes interop, and WinForms is never deleted until parity holds. **Phase 12 (MSTest)** and **Phase 13 (tooling + docs)** are decoupled and do not block the parity gate; docs land last so they describe the shipped state.

- [x] **Phase 8: WinUI 3 Scaffold + Dependencies + Guardrails** — WinUI 3 project opens an empty window; AOT/CI guardrails intact (OVL-01) (completed 2026-06-12)
- [ ] **Phase 9: Live Stream Render + Dispatcher Marshaling** — overlay renders live per-player DPS/HPS rows with persistence + font controls (OVL-02, OVL-07, OVL-08)
- [ ] **Phase 10: CsWin32 Interop — Transparency, Click-through, Drag, Topmost** — native overlay behaviors via CsWin32 (OVL-03..06, INT-01..03)
- [ ] **Phase 11: Parity Gate + WinForms Removal** — WinUI 3 validated at parity, WinForms host deleted (OVL-09)
- [ ] **Phase 12: MSTest Migration** — 106 tests on MSTest .NET SDK, CI + coverage preserved (TEST-01, TEST-02)
- [ ] **Phase 13: Dev Tooling + Docs Refresh** — VSCode launch/tasks + refreshed README/docs (DX-01, DX-02, DOCS-01)

## Phase Details

### Phase 8: WinUI 3 Scaffold + Dependencies + Guardrails

**Goal**: A WinUI 3 (Windows App SDK) overlay project exists as an unpackaged, self-contained app that launches an empty window — without breaking the `.slnx` build or the Native AOT CLI publish.
**Depends on**: Phase 7 (v1.0 CI baseline; WinForms overlay still present)
**Requirements**: OVL-01
**Success Criteria** (what must be TRUE):

  1. `Microsoft.WindowsAppSDK` and `Microsoft.Windows.CsWin32` are added as `PackageVersion` entries in `Directory.Packages.props` and referenced **only** from the overlay project (no AOT contamination of core/Native CLI)
  2. The overlay project builds with `UseWinUI=true`, `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true` and launches an empty window from a **published folder on a clean profile** (not just F5)
  3. `dotnet build SwtorLogParser.slnx -c Release` stays green on `windows-latest` with the WinUI 3 project added
  4. `dotnet publish SwtorLogParser.Native.Cli -c Release` stays warning-free (no new trim/AOT warnings) — AOT regression gate passes
  5. The WinForms overlay still builds and runs (parity safety net intact)**Plans**: 2 plans

**Wave 1**

  - [x] 08-01-PLAN.md — Pin WinAppSDK 2.2.0 + CsWin32 0.3.275 in CPM; scaffold the unpackaged self-contained WinUI 3 project (empty window)

**Wave 2** *(blocked on Wave 1 completion)*

  - [x] 08-02-PLAN.md — Guardrail verification: full .slnx build + Native AOT regression gate + CI wiring + clean-profile published-folder launch

**UI hint**: yes

### Phase 9: Live Stream Render + Dispatcher Marshaling

**Goal**: The WinUI 3 overlay subscribes to the frozen `DpsHps` stream and renders live per-player rows on the UI thread with no cross-thread crash, plus parity-level font controls and cross-run persistence.
**Depends on**: Phase 8
**Requirements**: OVL-02, OVL-07, OVL-08
**Success Criteria** (what must be TRUE):

  1. The overlay subscribes to `CombatLogsMonitor.Instance.DpsHps` and renders live per-player rows (Player / DPS / Crit% / HPS / Crit%), reusing the **core** `View/SlidingExpirationList` + `Entry` types unchanged
  2. The first live update arrives without a cross-thread `COMException 0x8001010E` — every UI mutation is marshaled to the captured UI `DispatcherQueue` (background aggregation in the locked core list; UI-tick render mirror into an `ObservableCollection`)
  3. The user can increase/decrease the overlay font size, at parity with the WinForms controls
  4. The overlay persists window position, size, and opacity across runs via a local settings file, with no new dependency on the core library

**Plans**: TBD
**UI hint**: yes

### Phase 10: CsWin32 Interop — Transparency, Click-through, Drag, Topmost

**Goal**: All native overlay window behaviors WinUI cannot express — transparency, click-through, drag, and BL-01 topmost re-assert — work over the live render surface, generated by CsWin32 (no hand-written P/Invoke).
**Depends on**: Phase 9 (needs a real window with live content to style)
**Requirements**: OVL-03, OVL-04, OVL-05, OVL-06, INT-01, INT-02, INT-03
**Success Criteria** (what must be TRUE):

  1. A throwaway transparency + click-through **spike** validates the layered-HWND approach on a real window before the production grid styling commits (OVL-03 de-risk step, sequenced first in this phase)
  2. The overlay is transparent and borderless at visual parity with the WinForms overlay (game shows through behind it; stats text stays crisp), and the user can drag it to reposition
  3. The user can toggle click-through mode (mouse passes to the game); default is **off** (overlay movable/interactive)
  4. All overlay Win32 interop is generated by `Microsoft.Windows.CsWin32` from `NativeMethods.txt` (hand-written `NativeMethods.cs` no longer the interop path) — closes #3
  5. The overlay stays on top of SWTOR in Fullscreen (Windowed)/Borderless by re-asserting `HWND_TOPMOST` on foreground changes (`SetWinEventHook` static-pinned callback + `SetWindowPos`), and does not steal focus or appear in Alt-Tab (`WS_EX_NOACTIVATE`/tool-window) — closes BL-01

**Plans**: TBD
**UI hint**: yes

### Phase 11: Parity Gate + WinForms Removal

**Goal**: WinUI 3 is demonstrably at parity with WinForms, then the WinForms overlay is removed so WinUI 3 is the sole overlay host.
**Depends on**: Phase 10 (HARD GATE — parity must hold before any deletion)
**Requirements**: OVL-09
**Success Criteria** (what must be TRUE):

  1. A parity UAT pass confirms the WinUI 3 overlay is transparent, draggable, renders live DPS/HPS, and stays topmost over Fullscreen-Windowed SWTOR — matching the WinForms baseline
  2. After the gate passes, the WinForms host files (`ParserForm`, `NativeMethods.cs`, the WinForms `View/*` adapter, the old WinForms `Program.cs`) are deleted and the solution points only at the WinUI 3 overlay
  3. `dotnet build SwtorLogParser.slnx -c Release` and the Native AOT CLI publish stay green after deletion (no dangling references)

**Plans**: TBD
**UI hint**: yes

### Phase 12: MSTest Migration

**Goal**: The test project runs on the MSTest .NET SDK with all 106 tests passing and CI code coverage preserved — decoupled from the overlay critical path.
**Depends on**: Phase 7 (v1.0 test/CI baseline; independent of overlay Phases 8-11)
**Requirements**: TEST-01, TEST-02
**Success Criteria** (what must be TRUE):

  1. The test project is migrated to `MSTest.Sdk` with all **106 tests passing** (count unchanged), `InternalsVisibleTo` internals access intact, and source-swap tests serialized
  2. CI stays green and the code-coverage artifact is still produced after the migration (e.g. `UseVSTest=true` so coverlet keeps working), with `.github/workflows/*` updated in the **same** change

**Plans**: TBD

### Phase 13: Dev Tooling + Docs Refresh

**Goal**: VSCode launch/tasks cover every host, and the docs describe the shipped WinUI 3 / .NET 10 state — landing last so docs reflect reality.
**Depends on**: Phase 11 (docs describe the shipped overlay state) and Phase 8 (overlay launch config needs the WinUI host)
**Requirements**: DX-01, DX-02, DOCS-01
**Success Criteria** (what must be TRUE):

  1. VSCode `launch.json` provides an F5 debug configuration for every runnable host (managed CLI, Native CLI, WinUI 3 overlay)
  2. VSCode `tasks.json` provides build, test, and Native-AOT-publish tasks
  3. The README/docs describe the WinUI 3 overlay, .NET 10, the three hosts, the run/debug story, and the **windowed-borderless vs exclusive-fullscreen** limitation

**Plans**: TBD

## Progress

**Execution Order:**
Overlay critical path 8 → 9 → 10 → 11 (hard parity gate). Phase 12 (MSTest) and Phase 13 (tooling + docs) are decoupled — Phase 12 can run in parallel anytime; Phase 13 lands last so docs describe the shipped state.

| Phase | Milestone | Status | Completed |
|-------|-----------|--------|-----------|
| 1. Parser Safety Net | v1.0 | Complete | 2026-06-11 |
| 2. Correctness Bugs | v1.0 | Complete | 2026-06-11 |
| 3. Monitor Refactor + Coverage | v1.0 | Complete | 2026-06-11 |
| 4. Performance | v1.0 | Complete | 2026-06-11 |
| 5. Dependency Upgrades | v1.0 | Complete | 2026-06-12 |
| 6. .NET 10 Upgrade | v1.0 | Complete | 2026-06-12 |
| 7. CI Pipeline | v1.0 | Complete | 2026-06-12 |
| 8. WinUI 3 Scaffold + Guardrails | 2/2 | Complete   | 2026-06-12 |
| 9. Live Stream Render + Dispatcher | v1.1 | Not started | - |
| 10. CsWin32 Interop | v1.1 | Not started | - |
| 11. Parity Gate + WinForms Removal | v1.1 | Not started | - |
| 12. MSTest Migration | v1.1 | Not started | - |
| 13. Dev Tooling + Docs Refresh | v1.1 | Not started | - |
