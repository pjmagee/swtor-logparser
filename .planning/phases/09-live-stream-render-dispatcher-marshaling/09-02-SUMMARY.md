---
phase: 09-live-stream-render-dispatcher-marshaling
plan: 02
subsystem: ui
tags: [winui3, settings-persistence, source-gen-json, appwindow, overlay, font-controls, corruption-safe, aot-isolation]

# Dependency graph
requires:
  - phase: 09-live-stream-render-dispatcher-marshaling
    plan: 01
    provides: Live ListView render + MainViewModel + MainWindow.xaml(.cs) (DpsHps→core list, 1s DispatcherQueueTimer mirror, dispose-on-close)
provides:
  - "OverlaySettings: serializable POCO — nullable WindowX/Y/Width/Height (int?) + double FontSize (DefaultFontSize const); reserved Opacity field for Phase 10 forward-compat (not controlled/applied this phase)"
  - "OverlaySettingsContext: source-generated (reflection-free) System.Text.Json JsonSerializerContext over OverlaySettings (WriteIndented)"
  - "SettingsService: Load()/Save() at %LocalAppData%\\SwtorLogParser\\settings.json; missing/corrupt/IO failure → defaults, NEVER throws; fixed path (no traversal); no core-library dependency; no ApplicationData.Current"
  - "MainWindow font +/- buttons (WinForms parity): single bindable FontSize adjusts rows AND header together, clamped 8..48"
  - "MainWindow load-on-startup (apply window pos/size via AppWindow.MoveAndResize + font) and save-on-Closed (read AppWindow.Position/Size + FontSize → SettingsService.Save)"
affects: [Phase 10 (opacity persistence + opacity control extend OverlaySettings reserved field; CsWin32 interop styles this window), Phase 11 (parity gate validates font controls + cross-run stickiness vs WinForms)]

# Tech tracking
tech-stack:
  added: []
  patterns: ["Corruption-safe local settings: Load wraps File.ReadAllText + source-gen Deserialize in try/catch returning new OverlaySettings() on ANY failure (missing/IO/JsonException); Save ensures dir + swallows write failure — a non-writable disk or corrupt file never crashes startup or window-close (T-09-04 mitigation)", "Fixed settings path from SpecialFolder.LocalApplicationData + literal 'SwtorLogParser'/'settings.json' — persisted values are numeric geometry/font only, never used as a path (T-09-05 mitigation)", "Source-generated System.Text.Json (JsonSerializerContext) keeps settings serialization reflection-free; settings files use only System.Text.Json + System.IO (no WinUI types) so they unit-test without dragging WinAppSDK into the test/AOT graph", "Managed AppWindow.MoveAndResize / Position+Size for window placement — no CsWin32/Win32 interop for pos+size this phase (interop deferred to Phase 10)"]

key-files:
  created:
    - SwtorLogParser.Overlay.WinUi/Settings/OverlaySettings.cs
    - SwtorLogParser.Overlay.WinUi/Settings/OverlaySettingsContext.cs
    - SwtorLogParser.Overlay.WinUi/Settings/SettingsService.cs
    - SwtorLogParser.Tests/OverlaySettingsServiceTests.cs
  modified:
    - SwtorLogParser.Tests/SwtorLogParser.Tests.csproj
    - SwtorLogParser.Overlay.WinUi/MainWindow.xaml
    - SwtorLogParser.Overlay.WinUi/MainWindow.xaml.cs

key-decisions:
  - "Persist position + size + font ONLY — no opacity control/apply this phase (D-04); a reserved Opacity field exists on OverlaySettings for Phase 10 forward-compat but is never controlled or applied"
  - "Local JSON at %LocalAppData%\\SwtorLogParser\\settings.json computed from SpecialFolder.LocalApplicationData; NOT ApplicationData.Current (unpackaged app) — D-05"
  - "Source-generated System.Text.Json JsonSerializerContext (reflection-free); settings files reference only System.Text.Json + System.IO so the corruption/round-trip tests run in the net10.0 test project without pulling WinAppSDK into the test/AOT graph"
  - "Missing / corrupt / IO-failing settings.json → OverlaySettings defaults; Load never throws and Save swallows write failure (D-05 corruption-safety; T-09-04 DoS mitigation)"
  - "Save on window Closed, load on startup/first-activation (D-06)"
  - "Window position + size applied/read via managed AppWindow.MoveAndResize + Position/Size — no CsWin32/Win32 interop in Phase 9 (interop is Phase 10)"

patterns-established:
  - "Pattern: corruption-safe local JSON settings for an unpackaged WinUI 3 app — fixed %LocalAppData% path, source-gen serialization, try/catch → defaults on every read/write failure, unit-tested over a temp-path override with no WinAppSDK in the test graph"

requirements-completed: [OVL-07, OVL-08]

# Metrics
duration: 3min
completed: 2026-06-12
---

# Phase 9 Plan 02: Font Controls + Settings Persistence Summary

**The WinUI 3 overlay now persists its window position + size + font size across runs via a corruption-safe local JSON file at `%LocalAppData%\SwtorLogParser\settings.json` (source-generated `System.Text.Json`, missing/corrupt → defaults without throwing), and exposes WinForms-parity `+`/`-` font buttons that resize the live rows AND the header together — loaded on startup via `AppWindow.MoveAndResize` and saved on window close, with no new dependency on the frozen core library and no contamination of the core AOT graph.**

## Performance

- **Duration:** ~3 min
- **Started:** 2026-06-12T11:24:04+01:00 (first task commit)
- **Completed:** 2026-06-12T11:26:59+01:00 (last task commit)
- **Tasks:** 3
- **Files modified:** 7 (4 created, 3 modified)

## Accomplishments
- Created `OverlaySettings` — a small serializable POCO with nullable window placement (`int? WindowX/WindowY/WindowWidth/WindowHeight`, where null = "no saved value yet → use default") and a `double FontSize` backed by a `DefaultFontSize` constant. Carries a reserved `Opacity` field for Phase 10 forward-compat that is **not** controlled or applied this phase (D-04).
- Created `OverlaySettingsContext` — a `partial JsonSerializerContext` annotated `[JsonSerializable(typeof(OverlaySettings))]` (`WriteIndented`) for **source-generated, reflection-free** serialization (D-05).
- Created `SettingsService` — `Load()`/`Save()` at a **fixed** `%LocalAppData%\SwtorLogParser\settings.json` (computed from `SpecialFolder.LocalApplicationData` + literal subfolder/file names; persisted values are numeric geometry/font only, never used as a path). `Load()` wraps read + source-gen deserialize in try/catch returning `new OverlaySettings()` on **any** failure (missing file, IO, `JsonException`) — it never throws; `Save()` ensures the directory exists then writes, swallowing write failures so a non-writable disk cannot crash window-close. A temp-path override keeps it unit-testable.
- Added **5 settings unit tests** (`OverlaySettingsServiceTests`): missing file → defaults; corrupt `{ not json` → defaults; `Save`→`Load` round-trips `WindowX/Y/Width/Height` + `FontSize` exactly; directory auto-create; default `%LocalAppData%` path. The test project references only `System.Text.Json` + `System.IO` against the settings types — **no WinAppSDK dragged into the test/AOT graph**.
- Added WinForms-parity `+`/`-` font buttons to `MainWindow.xaml`: a single bindable `FontSize` drives both the ListView rows **and** the header (WinForms changed both `ColumnHeadersDefaultCellStyle.Font` and `DefaultCellStyle.Font` by ±1), clamped to a usable 8..48 range; the current font size is held in a field for persistence.
- Wired `MainWindow.xaml.cs`: on startup/first-activation, `SettingsService.Load()` → acquire `AppWindow` (managed chain) → apply saved placement via `AppWindow.MoveAndResize` (skip when placement is null = first run) → apply saved font; the existing `Closed` handler (from 09-01) now **also** reads `AppWindow.Position`/`Size` + current `FontSize` into an `OverlaySettings` and calls `SettingsService.Save(...)` before/alongside disposing the VM — both run, neither throws out of `Closed`. Position/size use **only** managed `AppWindow` (no CsWin32/Win32 interop — Phase 9 scope). No opacity control added.

## Task Commits

Each task was committed atomically:

1. **Task 1: OverlaySettings + source-gen JsonSerializerContext + corruption-safe SettingsService (+ 5 unit tests)** — `5d642f1` (feat) — added the three `Settings/` files + `OverlaySettingsServiceTests.cs` + test-project wiring
2. **Task 2: Font +/- buttons resizing rows + header together (WinForms parity)** — `bb16146` (feat)
3. **Task 3: Persist window position+size+font via AppWindow (load on startup, save on close)** — `9d1fe27` (feat)

**Plan metadata:** see final docs commit.

## Files Created/Modified
- `SwtorLogParser.Overlay.WinUi/Settings/OverlaySettings.cs` *(created)* — serializable model: nullable `WindowX/Y/Width/Height` + `double FontSize` (`DefaultFontSize` const); reserved `Opacity` field (Phase 10, unused this phase).
- `SwtorLogParser.Overlay.WinUi/Settings/OverlaySettingsContext.cs` *(created)* — `[JsonSerializable(typeof(OverlaySettings))]` source-gen `JsonSerializerContext` (`WriteIndented`).
- `SwtorLogParser.Overlay.WinUi/Settings/SettingsService.cs` *(created)* — `Load()`/`Save()` at fixed `%LocalAppData%\SwtorLogParser\settings.json`; missing/corrupt/IO → defaults, never throws; temp-path override for tests; no core dependency; no `ApplicationData.Current`.
- `SwtorLogParser.Tests/OverlaySettingsServiceTests.cs` *(created)* — 5 tests (missing→defaults, corrupt→defaults, round-trip X/Y/W/H+FontSize, dir auto-create, default path).
- `SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` *(modified)* — wiring so the settings types compile into the test project without pulling WinAppSDK.
- `SwtorLogParser.Overlay.WinUi/MainWindow.xaml` *(modified)* — `+`/`-` font buttons; bound `FontSize` for rows + header.
- `SwtorLogParser.Overlay.WinUi/MainWindow.xaml.cs` *(modified)* — load-on-startup (apply pos/size via `AppWindow.MoveAndResize` + font) and save-on-`Closed` (read `AppWindow.Position`/`Size` + `FontSize` → `SettingsService.Save`); `+`/`-` click handlers (clamp 8..48).

## Artifacts This Phase Produces (Plan 02 portion)

| Artifact | New/Modified | Provides |
|----------|--------------|----------|
| `Settings/OverlaySettings.cs` | New | `OverlaySettings` model: `WindowX/Y/Width/Height` + `FontSize` (reserved `Opacity` for Phase 10 — D-04) |
| `Settings/OverlaySettingsContext.cs` | New | Source-generated `JsonSerializerContext` over `OverlaySettings` (D-05) |
| `Settings/SettingsService.cs` | New | `Load()`/`Save()` at `%LocalAppData%\SwtorLogParser\settings.json`; missing/corrupt → defaults, never throws (D-05/D-06) |
| `OverlaySettingsServiceTests.cs` | New | 5 settings unit tests (missing/corrupt → defaults; round-trip; dir auto-create; default path) |
| `MainWindow.xaml` | Modified | `+`/`-` font buttons; font binding for rows + header (WinForms parity) |
| `MainWindow.xaml.cs` | Modified | Load on startup (apply window pos/size + font); `+`/`-` handlers; save on `Closed` |

## Build / Gate Results
- **Settings unit tests:** **5/5 pass** — missing→defaults, corrupt→defaults, round-trip `WindowX/Y/Width/Height`+`FontSize`, directory auto-create, default `%LocalAppData%` path.
- **Overlay build** (`dotnet build SwtorLogParser.Overlay.WinUi -c Debug` + `-c Release`, win-x64): **0 warnings / 0 errors**.
- **Full solution build** (`dotnet build SwtorLogParser.slnx -c Release`): **0 errors**. The only warnings are the 5 **pre-existing** frozen WinForms `ParserForm.cs` warnings (out of scope, tracked in `deferred-items.md`).
- **AOT regression gate** (`dotnet publish SwtorLogParser.Native.Cli -c Release`): **0 IL2xxx/IL3xxx** warnings — the overlay's settings/font work did **not** contaminate the core AOT/trim graph (gate PASSES). The native C++ link step is environment-gated (see Deferred below).

## Human-Verify Outcome (Task 3 checkpoint) — APPROVED
- **Confirmed end-to-end:** the user launched the Release exe, **moved + resized** the window, **changed the font** via the `+`/`-` buttons, **closed**, and **relaunched** — window **position + size + font were all restored correctly**.
- **Confirmed corruption-safety:** a **corrupt/missing `settings.json` fell back to defaults without crashing** the overlay on startup.
- This satisfies **OVL-07 (position + size)** and **OVL-08 (font)** end-to-end, and validates the T-09-04 DoS mitigation (corrupt-file → defaults) against a real launch.

## Requirement Status
- **OVL-08** — **Satisfied.** The user can increase/decrease the overlay font size at parity with the WinForms `+`/`-` controls (rows + header adjust together, clamped 8..48); confirmed in the human-verify pass.
- **OVL-07** — **Satisfied for Phase 9 (position + size).** The overlay persists window position + size across runs via the local `%LocalAppData%\SwtorLogParser\settings.json` with **no** new core-library dependency; confirmed in the human-verify pass. **Opacity persistence is deliberately deferred to Phase 10 per D-04** — a reserved `Opacity` field exists on `OverlaySettings` for forward-compat but no opacity control is built or applied this phase. Marking OVL-07 complete for Phase 9 is appropriate per the planned scope split (Phase 9 = position + size; Phase 10 = opacity).

## Deviations from Plan

None — the plan executed as written. All locked decisions were honored:
- Position + size + font persisted only; **no opacity control/apply** (reserved field for Phase 10 — D-04).
- Local JSON at `%LocalAppData%\SwtorLogParser\settings.json` via `SpecialFolder.LocalApplicationData`; **no** `ApplicationData.Current` (D-05).
- **Source-generated** `System.Text.Json` (`JsonSerializerContext`).
- Missing / corrupt / IO-failing settings.json → defaults; `Load` never throws, `Save` swallows write failure (D-05).
- Save on `Closed`, load on startup/first-activation (D-06).
- Managed `AppWindow.MoveAndResize` + `Position`/`Size` for placement — **no** CsWin32/Win32 interop (Phase 9 scope).
- Settings types use only `System.Text.Json` + `System.IO` so the corruption/round-trip tests run in the test project with **no** WinAppSDK in the test/AOT graph.

## Known Stubs

- **Reserved `Opacity` field on `OverlaySettings`** — intentionally present for Phase 10 forward-compat, **not** wired to any control or applied to the window this phase (per D-04 scope split). This is a documented, intentional placeholder; **Phase 10** adds the opacity control + persistence. It does not block OVL-07's position+size portion (the only Phase-9 obligation for OVL-07).

## Deferred / Environment-gated
- **AOT native-link step (environment, NOT a code regression):** `dotnet publish SwtorLogParser.Native.Cli -c Release` reaches the native C++ link step and fails with `MSB3073` (`vswhere.exe`/`link.exe` not on this shell's PATH — MSVC toolchain absent in this environment). The **managed IL analysis was warning-free**, which is what the AOT-contamination gate checks. This is the **same** env limitation seen in v1.0 Phase 7, Phase 8, and 09-01 — it is **CI-covered** (CI runs from a Developer environment with the C++ build tools on PATH) and is **not a regression**. Re-run from a Visual Studio Developer prompt to complete the native link. Tracked in `deferred-items.md`.
- **Opacity persistence + opacity control** — deferred to **Phase 10** per D-04 (reserved field present; not applied this phase).

## Issues Encountered
- None blocking. One item is environment-gated and tracked above (native-link MSVC toolchain).

## Note on Working-Tree State (not part of this plan)
At finalization, the working tree contained only the pre-existing `.planning/v1.0-MILESTONE-AUDIT.md` deletion — unrelated archival churn from before this plan. It was left untouched and **excluded** from this plan's commits. No core-file (`SwtorLogParser/*.cs`) churn was present this finalization; the core parser remains **FROZEN** and was never staged.

## Next Phase Readiness
- **Phase 10** has a stylable live render surface with cross-run stickiness; it adds CsWin32 interop (transparency / click-through / drag / topmost) and the **opacity** control + persistence (extending the reserved `OverlaySettings.Opacity` field).
- **Phase 11** parity gate inherits font-control + position/size-stickiness validation against the WinForms baseline.

## Self-Check: PASSED

- All 4 created files present on disk (`OverlaySettings.cs`, `OverlaySettingsContext.cs`, `SettingsService.cs`, `OverlaySettingsServiceTests.cs`).
- All 3 task commits present in git history (`5d642f1`, `bb16146`, `9d1fe27`).

---
*Phase: 09-live-stream-render-dispatcher-marshaling*
*Completed: 2026-06-12*
