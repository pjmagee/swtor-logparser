# Project Research Summary

**Project:** SWTOR Log Parser — v1.1 WinUI 3 Overlay & Dev Tooling
**Domain:** Windows-native transparent game-stats overlay (WinUI 3 / Windows App SDK) + Win32 interop + .NET dev/test toolchain modernization, brownfield on .NET 10
**Researched:** 2026-06-12
**Confidence:** HIGH (existing code read directly; new-stack facts verified against Microsoft Learn; WinUI 3 transparency/topmost recipes are MEDIUM — community-sourced)

## Executive Summary

v1.1 is a **host re-implementation plus tooling refresh, not new product capability.** The parser and the live `CombatLogsMonitor.Instance.DpsHps` (`IObservable<PlayerStats>`) stream are frozen; the WinUI 3 overlay is a new pure consumer of that single seam, exactly like the WinForms overlay and the two CLIs. Experts build this kind of overlay by treating the WinUI 3 `Window` as a thin XAML render surface over a frozen Rx stream, marshaling every background-thread `OnNext` to the UI `DispatcherQueue`, and dropping to the raw HWND (via the CsWin32 source generator) for the native behaviors WinUI does not expose — transparency, click-through, drag, and topmost re-assertion. The new stack is verified and narrow: `Microsoft.WindowsAppSDK` 2.2.0, `Microsoft.Windows.CsWin32` 0.3.275, and `MSTest.Sdk/4.2.3`, all isolated to their respective projects.

**The honest scope of the overlay matters for requirements.** The shipped WinForms overlay is draggable, semi-transparent (`Opacity=0.5` + `TransparencyKey`), borderless, and topmost — but it is **NOT click-through** (it has no `WS_EX_TRANSPARENT` and depends on receiving `MouseDown` to drag itself). So strict parity is: transparent + borderless + topmost + draggable + live 5-column render + 10s expiry + font +/- + monitor-on-activation. **Click-through is a new differentiator, not parity**, and it directly conflicts with drag and the font buttons — it must be a toggle, never an always-on default. The roadmap/requirements should decide click-through explicitly rather than assume it.

**The risk profile is dominated by WinUI 3 itself.** The highest-risk item is transparency: WinUI 3 has **no `TransparencyKey` equivalent** and its DirectX compositor has no back-buffer for per-pixel alpha hit-testing, so the WinForms recipe does not port — it must be rebuilt with layered-HWND / `SetLayeredWindowAttributes` (or DWM) and de-risked in a throwaway spike *before* porting the grid. Other top risks: cross-thread `COMException 0x8001010E` if the Rx stream touches XAML off-thread; BL-01 topmost dropping on foreground change (and the honest hard limit that no normal overlay covers exclusive fullscreen); the unpackaged WinAppSDK "won't start / breaks CI" trap; AOT contamination of the clean core/Native CLI; and the MSTest→MTP migration silently breaking coverlet code coverage in CI. Mitigations are known and documented below.

## Key Findings

### Recommended Stack

The new v1.1 stack pieces are all verified GA and additive — the existing .NET 10 / `System.Reactive` 6.0.2 / Spectre.Console / Native AOT CLI stack is untouched. See `.planning/research/STACK.md`.

**Core technologies (NEW for v1.1):**
- **`Microsoft.WindowsAppSDK` 2.2.0** (released 2026-06-09): WinUI 3 UI framework for the new overlay — modern Windows-native XAML, full compositor access for transparency, supports **unpackaged self-contained** desktop apps (build → run an `.exe`, no runtime install). Overlay TFM: `net10.0-windows10.0.19041.0`, `UseWinUI=true`, `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, `AllowUnsafeBlocks=true`.
- **`Microsoft.Windows.CsWin32` 0.3.275**: source-generated, type-safe Win32 P/Invoke driven by `NativeMethods.txt` — replaces hand-written `NativeMethods.cs` (closes #3), zero runtime dependency, `PrivateAssets=all`.
- **`MSTest.Sdk/4.2.3`** (project SDK): single-line test-project SDK replacing the xUnit + Test.Sdk + runner + coverlet soup (closes #2). Defaults to the **Microsoft.Testing.Platform (MTP)** runner — the source of the coverage-break pitfall.
- **VSCode C# Dev Kit + C# extensions**: `coreclr` launch configs per host (debug the JIT build of each, including the managed overlay; AOT exe is a publish/CI step).

**Project/TFM shape (verified):** Overlay = `Microsoft.NET.Sdk` + `UseWinUI=true` + WinUI-versioned TFM + unpackaged/self-contained + `AllowUnsafeBlocks`. Test = `<Project Sdk="MSTest.Sdk/4.2.3">` (SDK version pinned **inline in the csproj**, NOT in `Directory.Packages.props` — CPM only governs the WinAppSDK and CsWin32 `PackageVersion` entries).

### Expected Features

Organized by the five milestone categories (Overlay, Interop, Testing, Dev Tooling, Docs). See `.planning/research/FEATURES.md`.

**Must have (table stakes / parity):**
- WinUI 3 overlay strict parity: transparent, borderless, always-on-top, **draggable**, live 5-column rows (Player/DPS/Crit%/HPS/Crit%), 10s sliding expiry (reusing core `View/SlidingExpirationList`), font +/- controls, monitor-on-activation — consuming `DpsHps` unchanged
- **BL-01 fix**: stays topmost over borderless/windowed SWTOR (`IsAlwaysOnTop` + `SetWindowPos(HWND_TOPMOST)` re-assert on a foreground hook)
- CsWin32 interop replacing `NativeMethods.cs` (`SendMessage`, `ReleaseCapture`, `SetWindowPos`, `SetWinEventHook`)
- MSTest migration: all **106 tests** pass, internals access intact, source-swap tests serialized, CI green
- VSCode `launch.json` (per-host F5) + `tasks.json` (build-all / test / AOT-publish)
- Docs refresh (README: what/install/run/3 hosts/overlay usage/**fullscreen caveat**/enable-logging/build)
- Retire WinForms host **after** WinUI parity validated (build-before-delete)

**Should have (differentiators, P2):**
- Click-through mode **with a toggle** (`WS_EX_LAYERED | WS_EX_TRANSPARENT`) — biggest UX win, but conflicts with drag/buttons
- Position/size/opacity persistence (local JSON, no core dependency)
- `WS_EX_NOACTIVATE` / `WS_EX_TOOLWINDOW` (never steal focus, stay out of alt-tab)
- Acrylic/Mica backdrop for legibility

**Defer (v1.2+):**
- Global show/hide hotkey (`RegisterHotKey`)
- Configurable columns / themes / new metrics — out of scope (would touch the frozen stream)
- Overlay over **exclusive fullscreen** — architecturally impossible without injection; **anti-feature**, document the windowed-borderless requirement instead

### Architecture Approach

The WinUI 3 host changes exactly three things in the host layer: thread marshaling moves `Control.Invoke` → `DispatcherQueue.TryEnqueue` / Rx `ObserveOn`; the render surface moves `DataGridView` + `BindingList<Entry>` → XAML `ListView`/`ItemsRepeater` bound via `x:Bind` to an `ObservableCollection<EntryViewModel>`; interop moves hand-written `NativeMethods` → CsWin32-generated `PInvoke.*` over the HWND. The core `SlidingExpirationList`/`Entry` (UI-free, post-RFCT-01) are **reused as-is**; the WinForms `BindingList` adapter is deleted, not ported. Decision: **same-name replace** (`SwtorLogParser.Overlay` identity preserved, internals wholly new) to keep the solution/CI graph stable. See `.planning/research/ARCHITECTURE.md`.

**Major components:**
1. `MainViewModel` (NEW) — subscribes `DpsHps` on the UI dispatcher → core `SlidingExpirationList`; a `DispatcherQueueTimer` (1s, UI thread) mirrors `core.Items` into the `ObservableCollection` (preserves the WinForms aggregate/render split; avoids hammering the non-thread-safe collection per `OnNext`)
2. `EntryViewModel` + `MainWindow.xaml` (NEW) — display projection + XAML render surface (replaces `ParserForm`)
3. `WindowInterop` + `NativeMethods.txt` (NEW) — HWND acquisition (`WindowNative.GetWindowHandle` → `AppWindow`/`OverlappedPresenter`), layered transparency, click-through, drag, `HWND_TOPMOST` re-assert
4. Core `CombatLogsMonitor.DpsHps` + `View/SlidingExpirationList`/`Entry` — **frozen / reused, the only seam**

### Critical Pitfalls

Top items from `.planning/research/PITFALLS.md` (10 documented, with a "Looks Done But Isn't" checklist):

1. **No `TransparencyKey` in WinUI 3 (highest risk)** — the WinForms color-key recipe has no port; the DirectX compositor gives no per-pixel alpha hit-test. Avoid: build transparency via layered HWND / `SetLayeredWindowAttributes` (or DWM), separate "visually transparent" from "click-through", and **de-risk in a throwaway spike before porting the grid**.
2. **Click-through vs lost input** — `WS_EX_TRANSPARENT` is whole-window; slapping it on kills drag + the +/- buttons. Avoid: tightly-sized window over stats only, OR a runtime style-flip toggle while keeping the drag handle hit-testable.
3. **Cross-thread `COMException 0x8001010E`** — `DpsHps.OnNext` fires on the background reader thread; touching XAML off-thread crashes. Avoid: capture the UI `DispatcherQueue` once on the UI thread, marshal every mutation via `TryEnqueue` (Rx.NET has no built-in WinUI scheduler).
4. **BL-01 topmost drops on foreground change + exclusive-fullscreen hard limit** — `IsAlwaysOnTop` sets the band once and the OS re-orders it. Avoid: `IsAlwaysOnTop` + `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)` re-assert via `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` + low-frequency timer; **document that exclusive fullscreen is unsupported** (run SWTOR Fullscreen-Windowed). The `SetWinEventHook` callback must be **static + delegate kept alive in a static field**, else `AccessViolation`.
5. **MSTest→MTP silently breaks CI coverage** — `coverlet.collector` + `--collect:"XPlat Code Coverage"` rely on VSTest, not MTP. Recommended: set **`<UseVSTest>true</UseVSTest>`** in the test project so MSTest runs under VSTest and the existing coverage CI step keeps working unchanged (lowest churn). Edit `.github/workflows/*` in the same phase as the migration.
6. **Unpackaged "won't start" / CI build trap** — set `WindowsPackageType=None` + `WindowsAppSDKSelfContained=true`; verify launch from a published folder on a clean profile and that `dotnet build SwtorLogParser.slnx` stays green on `windows-latest` once the WinUI 3 project is added.
7. **AOT contamination** — keep WinAppSDK/CsWin32 references in the overlay **only**; never add `PublishAot` to the overlay; re-run `dotnet publish SwtorLogParser.Native.Cli` as a regression gate at the end of every overlay phase.

## Implications for Roadmap

The controlling constraint is **parity-before-deletion**: the WinForms overlay keeps building and running until WinUI 3 reaches parity. The critical path is strictly dependency-ordered; MSTest migration, VSCode config, and docs hang off the side as parallel/trailing workstreams.

### Phase 1: Overlay scaffold + dependencies + AOT/CI guardrails
**Rationale:** Nothing can be styled or rendered until the WinUI 3 project exists and the new packages resolve. Establishing package isolation and the self-contained/unpackaged config up front prevents the AOT-contamination and CI-build traps from the start.
**Delivers:** WinAppSDK + CsWin32 in `Directory.Packages.props`; WinUI 3 project (`App.xaml`, `MainWindow.xaml`, unpackaged self-contained startup) — empty window opens; AOT publish + `slnx` build verified still green. WinForms overlay still present.
**Avoids:** Pitfall 6 (unpackaged/CI), Pitfall 7 (AOT contamination).

### Phase 2: Stream + dispatcher marshaling (the integration crux)
**Rationale:** The render path is the #1 correctness risk and must work before any native styling — you need a real window with live content to style.
**Delivers:** `MainViewModel` subscribing `DpsHps` → core `SlidingExpirationList` → `DispatcherQueueTimer` → `ObservableCollection` → `x:Bind` `ListView`. Live DPS/HPS rows render.
**Avoids:** Pitfall 5 (cross-thread COMException).

### Phase 3: CsWin32 interop — transparency, click-through, drag, BL-01 topmost
**Rationale:** The native behaviors WinUI can't express; ordered C1 (HWND + borderless presenter) → C2 (transparency + click-through) → C3 (drag) → C4 (BL-01 topmost). Highest-uncertainty work; gated behind a working render surface.
**Delivers:** `NativeMethods.txt` + `WindowInterop`; layered transparency; whole-window click-through (toggle); drag-to-move; `SetWindowPos(HWND_TOPMOST)` re-assert on `SetWinEventHook` foreground change.
**Avoids:** Pitfalls 1 (transparency), 2 (click-through input loss), 3 (drag re-wire), 4 (BL-01 + static-delegate trap), 8 (CsWin32 setup).

### Phase 4: Parity gate (HARD) + WinForms deletion
**Rationale:** The build-before-delete decision; the overlay must demonstrably match WinForms (transparent, draggable, live render, topmost over Fullscreen-Windowed) before anything is removed.
**Delivers:** parity UAT pass, then delete `ParserForm`, `NativeMethods.cs`, the WinForms `View/*` adapter, the old `Program.cs`; re-point the solution.
**Avoids:** Anti-Pattern 5 (deleting before parity).

### Parallel / trailing workstreams (no dependency on Phases 1–4)
- **MSTest migration** (xUnit → `MSTest.Sdk`): test project only. Mechanical attribute/assertion rewrite across 106 tests; preserve serialized source-swap tests (MSTest is sequential by default — free serialization unless `[Parallelize]` is added, then mark source-swap classes `[DoNotParallelize]`); keep `InternalsVisibleTo`; **set `UseVSTest=true` and edit CI in the same phase** to preserve coverage. Gate: 106/106 green + coverage artifact present.
- **VSCode launch/tasks**: CLI + Native CLI configs and build/test/AOT tasks can land first; the overlay launch config needs the WinUI host (Phase 1).
- **Docs refresh**: should land **last**, after the parity gate, so it describes the shipped state (incl. fullscreen caveat and enable-logging prerequisite).

### Phase Ordering Rationale
- Critical path forced by dependency: deps → scaffold → render (integration crux) → native interop → parity gate → delete. Each native behavior needs a live window to style, so render precedes interop.
- The parity gate is a **hard gate** — the WinForms host is the safety net and must not be deleted until WinUI parity holds.
- AOT publish + `slnx` build are re-run as regression gates after each overlay phase.
- MSTest/VSCode/docs are decoupled from overlay risk so they never block the parity gate.

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 3 (transparency/click-through/BL-01):** the single highest-risk area — WinUI 3 transparency has **no official single recipe** (community-sourced, MEDIUM confidence). Recommend a throwaway transparency **spike** before committing the grid port; validate the layered-vs-DWM recipe on a real machine over the running game.

Phases with standard / well-documented patterns (skip research-phase):
- **Phase 1 (scaffold):** WinUI 3 unpackaged self-contained setup documented on MS Learn (HIGH).
- **Phase 2 (dispatcher marshaling):** verified pattern (HIGH).
- **MSTest migration:** mechanical, documented mapping (HIGH); the coverage/MTP nuance is resolved with `UseVSTest=true`.
- **VSCode launch/tasks, Docs:** standard, LOW complexity.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All four new pieces verified GA (WinAppSDK 2.2.0, CsWin32 0.3.275, MSTest.Sdk 4.2.3); TFM/csproj shapes documented. |
| Features | HIGH | Parity baseline read directly from shipped `ParserForm.cs`/`NativeMethods.cs`; click-through-is-new correction grounded in code. |
| Architecture | HIGH | Integration seam, dispatcher marshaling, HWND/AppWindow chain, CsWin32 mechanism, build order, MSTest seam survival verified; transparency recipe the one MEDIUM detail. |
| Pitfalls | MEDIUM-HIGH | MS Learn docs HIGH; WinUI 3 transparency/topmost community-sourced (MEDIUM), cross-checked where possible. |

**Overall confidence:** HIGH (with one isolated MEDIUM: the exact WinUI 3 transparency recipe).

### Gaps to Address
- **WinUI 3 transparency parity recipe** (layered + `SetLayeredWindowAttributes` vs DWM blur) — no single official recipe; resolve via a Phase-3 spike validated on a real machine before the grid port commits.
- **Click-through UX decision** — a click-through window changes the interactive-grid model. Roadmap must make an explicit call (drag-grip + optional hotkey toggle). Default: ship locked-and-movable; click-through as a P2 toggle.
- **CI headless build of the WinUI 3 project** — validate `windows-latest` can restore/build/test the WinUI project and AOT publish stays warning-free before merge.
- **Version pinning at execution time** — confirm WinAppSDK and CsWin32 are set to current GA in `Directory.Packages.props` per the GA-only policy.

## Sources

### Primary (HIGH confidence)
- NuGet — `Microsoft.WindowsAppSdk` (2.2.0), `Microsoft.Windows.CsWin32` (0.3.275), `MSTest.Sdk` (4.2.3)
- Microsoft Learn — WinUI 3 + Win32 interop, retrieve HWND, unpackaged/self-contained deployment, `OverlappedPresenter.IsAlwaysOnTop`, MSTest SDK config (MTP/`UseVSTest`), MTP code coverage (coverlet incompatibility), `SetWindowPos`
- CsWin32 — Getting Started + `NativeMethods.txt`
- Existing codebase (read directly) — `ParserForm.cs`, `NativeMethods.cs`, core + Overlay `View/*`, `CombatLogsMonitor.cs`, `*.csproj`, `Directory.Packages.props`, `SwtorLogParser.Tests/*`, `.github/workflows`, `.planning/BACKLOG.md` (BL-01)

### Secondary (MEDIUM confidence)
- Microsoft Q&A — Semi-Transparent + Click-through; WinUI 3 window transparent
- dotnet/reactive #1651 — Rx lacks built-in WinUI DispatcherQueue scheduler
- microsoft-ui-xaml #8562 / #9990 / #2956 / #8410 — topmost/Z-order, transparent island, cross-thread COMException
- CsWin32 #1162 — static/pinned delegate for `SetWinEventHook`
- WinAppSDK 1.6 release notes — `PublishAot` partial support

### Tertiary (LOW confidence)
- Windows Forum / Build 2026 — May 2026 WinUI 3 AOT support (not needed this milestone)

---
*Research completed: 2026-06-12*
*Ready for roadmap: yes*
