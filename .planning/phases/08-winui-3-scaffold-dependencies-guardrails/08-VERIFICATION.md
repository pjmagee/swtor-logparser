---
phase: 08-winui-3-scaffold-dependencies-guardrails
verified: 2026-06-12T00:00:00Z
status: passed
score: 11/11 must-haves verified
overrides_applied: 0
requirements_verified: [OVL-01]
notes:
  - "Native AOT publish: IL analysis warning-free (0 IL2xxx/IL3xxx) — the contamination gate. The MSVC native-LINK step (vswhere/link.exe) is env-gated locally and CI-deferred, identical to v1.0 Phase 7 posture; per verification context this is NOT a regression and is not scored as a failure."
  - "OVL-01 launch criterion (self-contained published .exe opens an empty window from the publish folder) was human-approved during execution (Plan 02 Task 3, resume signal 'approved'); treated as human-confirmed, no outstanding human-verify item."
  - "Code review (08-REVIEW.md) WR-01/WR-02 flag a latent x86/AnyCPU slnx-vs-RID platform mismatch — advisory only; does not affect the x64 build path actually used. Recorded below, does not fail the phase."
---

# Phase 8: WinUI 3 Scaffold + Dependencies + Guardrails Verification Report

**Phase Goal:** A WinUI 3 (Windows App SDK) overlay project exists as an unpackaged, self-contained app that launches an empty window — without breaking the `.slnx` build or the Native AOT CLI publish.
**Verified:** 2026-06-12
**Status:** passed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| #  | Truth | Status | Evidence |
|----|-------|--------|----------|
| 1  | WinAppSDK 2.2.0 + CsWin32 0.3.275 pinned as PackageVersion in `Directory.Packages.props` | ✓ VERIFIED | `Directory.Packages.props:15-16` — `Microsoft.WindowsAppSDK` Version `2.2.0`, `Microsoft.Windows.CsWin32` Version `0.3.275`; existing pins (System.Reactive 6.0.2 etc.) untouched |
| 2  | New `SwtorLogParser.Overlay.WinUi` project exists and is registered in `SwtorLogParser.slnx` | ✓ VERIFIED | csproj present on disk; `SwtorLogParser.slnx:11` registers it; existing 5 projects (incl. WinForms Overlay) retained |
| 3  | WinUI project references both packages version-less (CPM); CsWin32 `PrivateAssets=all`; core lib + Native CLI gain no new reference | ✓ VERIFIED | csproj:26-30 version-less refs, CsWin32 `<PrivateAssets>all</PrivateAssets>` + IncludeAssets isolation; grep of `SwtorLogParser.csproj` and `Native.Cli.csproj` for WindowsAppSDK/CsWin32/WinUI → **No matches** |
| 4  | csproj has required knobs (UseWinUI, WindowsPackageType=None, WindowsAppSDKSelfContained, AllowUnsafeBlocks) + TFM `net10.0-windows10.0.19041.0` | ✓ VERIFIED | csproj:16,18,19,22,4 all present; `OutputType=WinExe`; no PublishAot/trim flags; no ProjectReference to core |
| 5  | App.xaml.cs bootstrap creates and activates MainWindow | ✓ VERIFIED | `App.xaml.cs:18-22` `OnLaunched` → `new MainWindow(); _window.Activate();`; no monitor/stream wiring (in scope) |
| 6  | MainWindow is an empty WinUI render surface | ✓ VERIFIED | `MainWindow.xaml:9` single empty `<Grid />`; `MainWindow.xaml.cs` `InitializeComponent()` only |
| 7  | NativeMethods.txt proves CsWin32 generator resolves/isolates | ✓ VERIFIED | `NativeMethods.txt:1` = `GetForegroundWindow` (one benign symbol, no `#` lines → no PInvoke001 warning) |
| 8  | Full `dotnet build SwtorLogParser.slnx -c Release` succeeds with WinUI added (all 6 projects, WinForms parity intact) | ✓ VERIFIED | Build re-run by verifier: **Build succeeded. 0 Error(s)**; all 6 projects compiled incl. WinUI (`win-x64/...WinUi.dll`) and WinForms Overlay; 5 pre-existing CS0108/CS8602 WinForms warnings (out of scope, not from this phase) |
| 9  | `dotnet publish SwtorLogParser.Native.Cli -c Release` stays warning-free (no new IL2xxx/IL3xxx) | ✓ VERIFIED | Publish re-run by verifier: IL analysis emits **NO_IL_WARNINGS** (0 IL2xxx/IL3xxx). MSVC native-LINK fails locally (vswhere/link.exe absent) — env-gated, CI-deferred, same as Phase 7; not a regression per verification context |
| 10 | Published overlay launches an empty window from a self-contained published folder | ✓ VERIFIED | Self-contained publish exists: `...win-x64/publish/SwtorLogParser.Overlay.WinUi.exe` + 273 dll/exe incl. WindowsAppRuntime.Bootstrap + Microsoft.UI.Xaml.* runtime bundled. Human-approved during execution (Plan 02 Task 3, "approved") |
| 11 | Existing WinForms `SwtorLogParser.Overlay` project is unmodified and still builds | ✓ VERIFIED | `git status --short SwtorLogParser.Overlay/` clean; last commit c00311c (net10 upgrade, pre-Phase-8); builds within the green `.slnx` Release build |

**Score:** 11/11 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `Directory.Packages.props` | CPM pins for WinAppSDK + CsWin32 | ✓ VERIFIED | Both pins at exact versions; existing pins intact |
| `SwtorLogParser.Overlay.WinUi.csproj` | Unpackaged self-contained WinUI 3 project | ✓ VERIFIED | All knobs + TFM present; CPM version-less refs; CsWin32 isolated |
| `App.xaml.cs` | App bootstrap → MainWindow | ✓ VERIFIED | `OnLaunched` activates MainWindow |
| `MainWindow.xaml` | Empty render surface | ✓ VERIFIED | Single empty Grid |
| `NativeMethods.txt` | CsWin32 generator input | ✓ VERIFIED | `GetForegroundWindow` |
| `App.xaml` / `MainWindow.xaml.cs` / `app.manifest` | Supporting scaffold | ✓ VERIFIED | XamlControlsResources merged; PerMonitorV2 DPI + longPathAware manifest wired via `<ApplicationManifest>` |
| `SwtorLogParser.slnx` | WinUI project registration | ✓ VERIFIED | Line 11; WinForms entry retained |
| `.github/workflows/ci.yml` | CI covers `.slnx` build incl. WinUI; AOT gate intact | ✓ VERIFIED | `dotnet build SwtorLogParser.slnx -c Release` on windows-latest; `aot-publish` job intact; comment documents no workload step needed |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `SwtorLogParser.Overlay.WinUi.csproj` | `Directory.Packages.props` | Version-less PackageReference via CPM | ✓ WIRED | `PackageReference Include="Microsoft.WindowsAppSDK"` (no Version) resolves to pinned 2.2.0; build succeeds |
| `App.xaml.cs` | `MainWindow` | `new MainWindow().Activate()` in OnLaunched | ✓ WIRED | `App.xaml.cs:20-21` |
| `.github/workflows/ci.yml` | `SwtorLogParser.Overlay.WinUi.csproj` | `dotnet build SwtorLogParser.slnx` pulls in WinUI project | ✓ WIRED | slnx-level build covers all 6 projects |
| Native AOT publish gate | `SwtorLogParser.Native.Cli` | `dotnet publish` re-run as contamination check | ✓ WIRED | aot-publish job present; IL-clean confirmed |

### Package Isolation (AOT-Contamination Guard)

| Project | WinAppSDK/CsWin32 reference | Status |
|---------|----------------------------|--------|
| `SwtorLogParser` (IsAotCompatible) | None (grep: no matches) | ✓ CLEAN |
| `SwtorLogParser.Native.Cli` (PublishAot) | None (grep: no matches) | ✓ CLEAN |
| `SwtorLogParser.Overlay.WinUi` | Both (CPM, CsWin32 PrivateAssets=all) | ✓ ISOLATED |

Native AOT publish IL analysis = 0 IL2xxx/IL3xxx warnings → no transitive leak into the AOT graph.

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Full solution Release build green | `dotnet build SwtorLogParser.slnx -c Release` | Build succeeded, 0 Error(s), 6 projects | ✓ PASS |
| AOT IL-analysis clean | `dotnet publish SwtorLogParser.Native.Cli -c Release` | 0 IL2xxx/IL3xxx (native-link env-gated, expected) | ✓ PASS |
| Self-contained exe + runtime emitted | `find .../publish -name *.exe` + runtime ls | exe + 273 assemblies incl. WindowsAppRuntime.Bootstrap | ✓ PASS |
| WinForms overlay untouched | `git status --short SwtorLogParser.Overlay/` | clean | ✓ PASS |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| OVL-01 | 08-01, 08-02 | Unpackaged self-contained WinUI 3 overlay launches from a published `.exe`; adding it keeps `.slnx` build + Native AOT publish green | ✓ SATISFIED | Truths 1-11. REQUIREMENTS.md marks OVL-01 `[x]` / Phase 8 Complete. No orphaned IDs (OVL-01 is the only Phase 8 requirement). |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| (none) | — | No TODO/FIXME/XXX/HACK/PLACEHOLDER/NotImplemented in any new file | — | Empty window + benign CsWin32 symbol are intentional scaffold scope, not stubs |

### Advisory (from 08-REVIEW.md — not phase-failing)

| ID | Concern | Disposition |
|----|---------|-------------|
| WR-01/WR-02 | WinUI csproj declares `Platforms=x64;arm64` but `.slnx` advertises Any CPU/arm64/x64/x86; default RID condition `!= 'arm64'` would mis-RID an `x86` build to `win-x64` | Advisory. The actually-used x64/Any-CPU path builds green (verified). Latent only for an explicit `/p:Platform=x86` build which is not used. Note for a future hardening pass; does not fail Phase 8 per verification context. |
| WR-03 | `GetForegroundWindow` is generated but unused | Intentional — proves CsWin32 resolves in isolation; real interop surface lands Phase 10 |
| IN-01/02/03 | NuGet cache key, implicit WinAppSDK isolation, no window-close teardown | Informational; no action required this phase |

### Human Verification Required

None outstanding. The one human-gated criterion (published self-contained `.exe` launches an empty window from the publish folder on a clean profile, not F5) was completed and **approved** during execution (Plan 02 Task 3, resume signal "approved"). Verifier independently confirmed the publish folder contains the runnable exe + bundled Windows App SDK runtime.

### Gaps Summary

No gaps. All 11 must-haves are verified against the codebase, not just SUMMARY claims:
- All scaffold artifacts exist on disk with the required csproj knobs and correct TFM.
- Package isolation confirmed by grep (core lib + Native CLI have zero WinUI/CsWin32 references) and empirically by a warning-free AOT IL analysis.
- The full `.slnx` Release build was re-run by the verifier and is green across all 6 projects, with the WinForms overlay untouched (clean git status).
- The Native AOT publish IL analysis is warning-free; the MSVC native-link failure is an expected local-environment gap (vswhere/link.exe absent), CI-deferred, matching v1.0 Phase 7 — not a regression.
- The self-contained published `.exe` exists with the full Windows App SDK runtime bundled; the empty-window launch was human-approved during execution.

Phase goal achieved. Ready to proceed to Phase 9.

---
_Verified: 2026-06-12_
_Verifier: Claude (gsd-verifier)_
