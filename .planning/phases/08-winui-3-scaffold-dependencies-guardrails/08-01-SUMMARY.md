---
phase: 08-winui-3-scaffold-dependencies-guardrails
plan: 01
subsystem: ui
tags: [winui3, windows-app-sdk, cswin32, central-package-management, dotnet10, overlay, aot-isolation]

# Dependency graph
requires:
  - phase: 07-ci-pipeline
    provides: v1.0 CI baseline (windows-latest .slnx build + Native AOT publish), WinForms overlay still present
provides:
  - "Microsoft.WindowsAppSDK 2.2.0 + Microsoft.Windows.CsWin32 0.3.275 pinned in CPM (Directory.Packages.props)"
  - "New SwtorLogParser.Overlay.WinUi project: unpackaged self-contained WinUI 3 host that opens an empty window"
  - "Package-isolation boundary: WinAppSDK + CsWin32 referenced ONLY from the WinUI project (core lib + Native CLI untouched)"
  - "CsWin32 generator proven to run in isolation (GetForegroundWindow) with zero runtime artifacts"
  - "Solution registration of the WinUI project in SwtorLogParser.slnx (WinForms Overlay retained, coexistence)"
affects: [Phase 9 (live stream render — has a real window to wire DpsHps into), Phase 10 (CsWin32 interop — generator + NativeMethods.txt seam established), Phase 11 (parity gate), Phase 13 (VSCode launch config for the WinUI host)]

# Tech tracking
tech-stack:
  added: [Microsoft.WindowsAppSDK 2.2.0, Microsoft.Windows.CsWin32 0.3.275]
  patterns: ["Unpackaged self-contained WinUI 3 (WindowsPackageType=None + WindowsAppSDKSelfContained=true)", "CPM-pinned package referenced only from the consuming project (AOT-contamination isolation)", "CsWin32 source-generated P/Invoke via NativeMethods.txt with PrivateAssets=all", "Default RuntimeIdentifier resolution so self-contained builds without an explicit -r"]

key-files:
  created:
    - SwtorLogParser.Overlay.WinUi/SwtorLogParser.Overlay.WinUi.csproj
    - SwtorLogParser.Overlay.WinUi/app.manifest
    - SwtorLogParser.Overlay.WinUi/App.xaml
    - SwtorLogParser.Overlay.WinUi/App.xaml.cs
    - SwtorLogParser.Overlay.WinUi/MainWindow.xaml
    - SwtorLogParser.Overlay.WinUi/MainWindow.xaml.cs
    - SwtorLogParser.Overlay.WinUi/NativeMethods.txt
  modified:
    - Directory.Packages.props
    - SwtorLogParser.slnx

key-decisions:
  - "Default RuntimeIdentifier (win-x64 / win-arm64 by Platform) resolved in the csproj so WindowsAppSDKSelfContained builds RID-less — keeps `dotnet build SwtorLogParser.slnx` and CI green without an explicit -r"
  - "NativeMethods.txt holds only the bare symbol (no `#` comment lines) — this CsWin32 version treats `#` lines as symbols and emits PInvoke001 warnings; context moved to the SUMMARY/plan instead"
  - "Reverted an incidental whitespace-only reformat of the WinForms SwtorLogParser.Overlay.csproj to keep it byte-for-byte unchanged (coexistence safety net)"

patterns-established:
  - "Pattern: New Windows-UI packages enter CPM but the PackageReference lives only in the overlay project — core lib (IsAotCompatible) and Native CLI gain no new dependency"
  - "Pattern: CsWin32 wired with PrivateAssets=all + IncludeAssets so it is compile-time only (no runtime artifact flows transitively)"

requirements-completed: [OVL-01]

# Metrics
duration: 3min
completed: 2026-06-12
---

# Phase 8 Plan 01: WinUI 3 Scaffold + Dependencies Summary

**Unpackaged self-contained WinUI 3 overlay project (`SwtorLogParser.Overlay.WinUi`) that opens an empty window, with WinAppSDK 2.2.0 + CsWin32 0.3.275 pinned in CPM and isolated to the overlay so the AOT-clean core and Native CLI stay untouched.**

## Performance

- **Duration:** 3 min
- **Started:** 2026-06-12T07:46:36Z
- **Completed:** 2026-06-12T07:50:00Z
- **Tasks:** 2
- **Files modified:** 9 (7 created, 2 modified)

## Accomplishments
- Pinned `Microsoft.WindowsAppSDK` 2.2.0 and `Microsoft.Windows.CsWin32` 0.3.275 as `PackageVersion` entries in `Directory.Packages.props` (no existing pins touched).
- Scaffolded a new, separate `SwtorLogParser.Overlay.WinUi` project (unpackaged, self-contained WinUI 3) that builds to a `WinExe` and activates an empty `MainWindow` (single `Grid`).
- Established the package-isolation boundary: both new packages are referenced **only** from the WinUI project; the core library and Native CLI gain no new dependency (AOT-contamination guard, Pitfall 7).
- Proved the CsWin32 source generator runs in isolation via a single benign symbol (`GetForegroundWindow`) — zero runtime artifacts this phase.
- Registered the WinUI project in `SwtorLogParser.slnx` while keeping the WinForms `SwtorLogParser.Overlay` entry (coexistence; identity consolidation deferred to Phase 11).

## Task Commits

Each task was committed atomically:

1. **Task 1: Pin WinAppSDK + CsWin32 in CPM, register WinUI project in solution** - `9cfd991` (feat)
2. **Task 2: Scaffold the unpackaged self-contained WinUI 3 project with an empty window** - `fb2e5c4` (feat)

**Plan metadata:** see final docs commit.

## Files Created/Modified
- `Directory.Packages.props` - Added 2 `PackageVersion` pins (WinAppSDK 2.2.0, CsWin32 0.3.275).
- `SwtorLogParser.slnx` - Registered `SwtorLogParser.Overlay.WinUi` (WinForms Overlay retained).
- `SwtorLogParser.Overlay.WinUi/SwtorLogParser.Overlay.WinUi.csproj` - Unpackaged self-contained WinUI 3 project (UseWinUI, WindowsPackageType=None, WindowsAppSDKSelfContained, AllowUnsafeBlocks; net10.0-windows10.0.19041.0; default RID resolution).
- `SwtorLogParser.Overlay.WinUi/app.manifest` - PerMonitorV2 DPI-awareness + longPathAware + Win10/11 supportedOS.
- `SwtorLogParser.Overlay.WinUi/App.xaml` / `App.xaml.cs` - Application bootstrap; `OnLaunched` activates `MainWindow` (no monitor/stream wiring).
- `SwtorLogParser.Overlay.WinUi/MainWindow.xaml` / `MainWindow.xaml.cs` - Empty render surface (single `Grid`).
- `SwtorLogParser.Overlay.WinUi/NativeMethods.txt` - CsWin32 input; one benign symbol (`GetForegroundWindow`).

## Decisions Made
- **Default `RuntimeIdentifier` in the csproj.** `WindowsAppSDKSelfContained=true` requires a resolved single-architecture RID; a plain `dotnet build` (and the solution build) does not pick one from `RuntimeIdentifiers`, so the csproj defaults to `win-x64` (or `win-arm64` on the arm64 Platform) when none is supplied. This keeps `dotnet build SwtorLogParser.slnx` and CI green without forcing an explicit `-r`.
- **`NativeMethods.txt` holds only the bare symbol.** This CsWin32 version does not treat `#` lines as comments — they generate PInvoke001 "symbol not found" warnings. The explanatory context was moved out of the file (into the plan/summary) so the build stays warning-free.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added default RuntimeIdentifier resolution to the WinUI csproj**
- **Found during:** Task 2 (scaffold + build)
- **Issue:** `WindowsAppSDKSelfContained=true` fails a RID-less build with `error : WindowsAppSDKSelfContained requires a supported Windows architecture`. The plan's verify gate (and the Plan-02 `.slnx`/CI build) run `dotnet build` with no `-r`, so the project would never build in those contexts.
- **Fix:** Added conditional `<RuntimeIdentifier>` properties (default `win-x64`, or `win-arm64` when `Platform=arm64`) that apply only when no RID is supplied on the CLI.
- **Files modified:** `SwtorLogParser.Overlay.WinUi/SwtorLogParser.Overlay.WinUi.csproj`
- **Verification:** RID-less `dotnet build ... -c Release` now succeeds with 0 warnings / 0 errors and produces `SwtorLogParser.Overlay.WinUi.exe`.
- **Committed in:** `fb2e5c4` (Task 2 commit)

**2. [Rule 1 - Bug] Stripped `#` comment lines from NativeMethods.txt**
- **Found during:** Task 2 (first build)
- **Issue:** The header comment lines (`# ...`) each emitted a `warning PInvoke001: Method, type or constant "# ..." not found` — this CsWin32 version treats every non-blank line as a symbol, so the comments would ship as build warnings.
- **Fix:** Reduced `NativeMethods.txt` to the single bare symbol `GetForegroundWindow`; moved the contextual note into the plan/summary.
- **Files modified:** `SwtorLogParser.Overlay.WinUi/NativeMethods.txt`
- **Verification:** Build is warning-free (0 Warning(s)); `grep -v '^#' NativeMethods.txt` still yields a real symbol so the plan verify gate passes.
- **Committed in:** `fb2e5c4` (Task 2 commit)

**3. [Rule 1 - Bug] Reverted incidental whitespace reformat of the WinForms overlay csproj**
- **Found during:** Task 2 (post-build status check)
- **Issue:** `git status` reported `SwtorLogParser.Overlay/SwtorLogParser.Overlay.csproj` as modified — a whitespace/indentation/EOL-only reformat with no content change. The plan requires the WinForms project to be byte-for-byte unchanged.
- **Fix:** `git checkout -- SwtorLogParser.Overlay/SwtorLogParser.Overlay.csproj` to restore the original exactly.
- **Files modified:** none (revert)
- **Verification:** `git status --short SwtorLogParser.Overlay/` is clean; WinForms overlay rebuilds (0 errors; 5 pre-existing CS8602 warnings, out of scope).
- **Committed in:** n/a (no change committed — file restored to its committed state)

---

**Total deviations:** 3 auto-fixed (2 blocking/bug build fixes, 1 coexistence-safety revert)
**Impact on plan:** All three were required to satisfy the plan's own acceptance criteria (RID-less build success, warning-free CsWin32, WinForms untouched). No scope creep — no transparency/interop/stream wiring was added.

## Issues Encountered
- No Windows App SDK workload is installed on the host (`dotnet workload list` is empty), but the WinAppSDK NuGet packages restored and built without one — matching the STACK.md expectation that the metapackage restores without a separate workload.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- A real empty WinUI 3 window now exists for Phase 9 to wire the frozen `CombatLogsMonitor.Instance.DpsHps` stream into (capture the UI `DispatcherQueue`; do NOT update XAML off the Rx background thread — Pitfall 5).
- The CsWin32 `NativeMethods.txt` seam is established for Phase 10 to add the real interop surface (`SetWindowPos`, `SetWinEventHook`, `ReleaseCapture`, `WS_EX_*`, `HWND_TOPMOST`, etc.).
- **Deferred to Plan 08-02 (Wave 2):** full `dotnet build SwtorLogParser.slnx -c Release` gate, Native AOT publish regression gate (`dotnet publish SwtorLogParser.Native.Cli`), CI wiring, and the clean-profile published-folder launch verification. This plan verified only that the new project itself builds (per its scope).

## Self-Check: PASSED

- All 7 created source files present on disk + SUMMARY.md present.
- Both task commits present in git history (`9cfd991`, `fb2e5c4`).

---
*Phase: 08-winui-3-scaffold-dependencies-guardrails*
*Completed: 2026-06-12*
