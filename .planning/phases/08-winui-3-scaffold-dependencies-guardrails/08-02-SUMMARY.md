---
phase: 08-winui-3-scaffold-dependencies-guardrails
plan: 02
subsystem: infra
tags: [winui3, windows-app-sdk, native-aot, ci, github-actions, dotnet10, self-contained, guardrails]

# Dependency graph
requires:
  - phase: 08-winui-3-scaffold-dependencies-guardrails
    provides: "Plan 01 — the SwtorLogParser.Overlay.WinUi project (unpackaged self-contained WinUI 3, empty window) registered in the .slnx; WinAppSDK 2.2.0 + CsWin32 0.3.275 pinned in CPM and isolated to the overlay"
  - phase: 07-ci-pipeline
    provides: "v1.0 CI baseline (windows-latest .slnx build + Native AOT publish gate)"
provides:
  - "Verified-green full `dotnet build SwtorLogParser.slnx -c Release` (all 6 projects incl. WinUI 3 + WinForms)"
  - "Native AOT publish IL-analysis regression gate confirmed warning-free (0 IL2xxx/IL3xxx) — AOT-contamination boundary holds"
  - "Resolved open question: headless windows-latest restores + builds the unpackaged self-contained WinUI 3 project with NO `dotnet workload` step (zero installed workloads)"
  - "CI (.github/workflows/ci.yml) confirmed to cover the WinUI project via the existing slnx-level build; comment updated to record the verification"
  - "Human-confirmed clean-profile launch: published self-contained `SwtorLogParser.Overlay.WinUi.exe` opens an empty window standalone (not F5), no bootstrapper / runtime-not-found error"
affects: [Phase 9 (live stream render — has a verified-launchable window to wire DpsHps into), Phase 10 (CsWin32 interop), Phase 11 (parity gate — AOT/CI guardrails to re-run after WinForms deletion)]

# Tech tracking
tech-stack:
  added: []
  patterns: ["AOT-contamination regression gate: re-run `dotnet publish SwtorLogParser.Native.Cli -c Release` and assert 0 IL2xxx/IL3xxx after any overlay-package change", "Unpackaged self-contained WinUI 3 restores + builds on headless windows-latest with no `dotnet workload` install (NuGet metapackage only)"]

key-files:
  created:
    - .planning/phases/08-winui-3-scaffold-dependencies-guardrails/08-02-SUMMARY.md
  modified:
    - .github/workflows/ci.yml

key-decisions:
  - "No `dotnet workload` step needed in CI — the WinUI 3 unpackaged self-contained project restores the Windows App SDK metapackage from NuGet and builds on a zero-workload windows-latest runner; ci.yml left structurally unchanged (comment-only update)"
  - "MSVC native-link step for the Native AOT publish env-gates locally (vswhere/link.exe absent) — IL analysis is warning-free; native link is CI-covered, same posture as v1.0 Phase 7 (NOT a regression)"

patterns-established:
  - "Pattern: After any overlay dependency change, re-run the Native AOT publish as a contamination gate (IL2xxx/IL3xxx == fail) and confirm WinAppSDK/CsWin32 stay absent from the Native.Cli resolved graph"

requirements-completed: [OVL-01]

# Metrics
duration: ~25min (incl. human-verify checkpoint wait)
completed: 2026-06-12
---

# Phase 8 Plan 02: WinUI 3 Scaffold Guardrail Verification Summary

**Closed the OVL-01 guardrails: full `.slnx` Release build green with the WinUI 3 project, Native AOT publish IL-analysis warning-free, CI confirmed to cover the new project with no workload step, and a human-verified clean-profile launch of the self-contained published overlay `.exe`.**

## Performance

- **Duration:** ~25 min (includes the human-verify checkpoint wait)
- **Completed:** 2026-06-12
- **Tasks:** 3 (2 automated + 1 human-verify checkpoint, now approved)
- **Files modified:** 2 (1 created — this SUMMARY; 1 modified — `.github/workflows/ci.yml`)

## Accomplishments
- Verified the full `dotnet build SwtorLogParser.slnx -c Release` is green across all 6 projects (core lib + managed CLI + Native CLI + WinForms overlay + WinUI 3 overlay + tests), 0 errors — WinForms parity safety net intact.
- Confirmed the Native AOT publish (`dotnet publish SwtorLogParser.Native.Cli -c Release`) IL analysis is warning-free (0 IL2xxx/IL3xxx) — the AOT-contamination guard (Pitfall 7 / T-08-04) holds.
- Confirmed WinAppSDK + CsWin32 are absent from the Native.Cli resolved dependency graph (CPM metadata only, no transitive leak) — the package-isolation boundary from Plan 01 survives the full-solution build.
- Resolved the phase's open research question: a clean, from-scratch restore + build of the unpackaged self-contained WinUI 3 project succeeds with **zero installed workloads**, so CI on windows-latest needs **no `dotnet workload` step** (Pitfall 6 headless-runner trap cleared).
- Updated `.github/workflows/ci.yml` (comment-only) to record that the existing slnx-level build now covers + has verified the WinUI project; no new jobs or workload steps added.
- **Human-verified (checkpoint approved):** the self-contained published `SwtorLogParser.Overlay.WinUi.exe` launches an empty window standalone (double-clicked from the publish folder, NOT VS/F5; also confirmed via `dotnet run`), stays open, no crash, no "Windows App Runtime not found" / bootstrapper error — clearing the "launches from a published folder, not just F5" half of OVL-01 success criterion 2 (Pitfall 6).

## Task Commits

1. **Task 1: Verify full `.slnx` Release build + AOT regression gate; wire CI** — `f2f6bce` (docs — ci.yml comment update; gates verified green, no structural CI change needed)
2. **Task 2: Publish the WinUI overlay self-contained; confirm the launch artifact** — build artifact only (self-contained publish folder + `SwtorLogParser.Overlay.WinUi.exe`); no source change, no commit
3. **Task 3: Confirm the published overlay launches an empty window on a clean profile** — human-verify checkpoint, **APPROVED** (no commit; manual verification)

**Plan metadata:** see final docs commit (this SUMMARY + STATE.md + ROADMAP.md).

## Files Created/Modified
- `.github/workflows/ci.yml` — Build-step comment updated to record that the full `.slnx` build now covers the new unpackaged self-contained WinUI 3 overlay AND the WinForms overlay (both require the Windows runner), and that the WinUI project restores the Windows App SDK metapackage from NuGet with no separate `dotnet workload` step on windows-latest. No new jobs; build-test, coverage, and aot-publish jobs preserved.
- `.planning/phases/08-winui-3-scaffold-dependencies-guardrails/08-02-SUMMARY.md` — This summary.

## Decisions Made
- **No CI workload step.** The open question (does headless windows-latest restore/build an unpackaged self-contained WinUI 3 project without a separate workload install?) is resolved: it does. A clean restore+build with zero installed workloads succeeds because the Windows App SDK metapackage restores from NuGet. `ci.yml` therefore stays structurally unchanged (comment-only update) — the smallest possible change that keeps the runner green without excluding the WinUI project.
- **MSVC native-link is env-gated locally, CI-covered.** The Native AOT publish's IL analysis is warning-free (the contamination-relevant signal). The MSVC native-link step (vswhere/link.exe) does not run on this host — identical to v1.0 Phase 7 posture. This is an expected local-environment gap, not a regression; CI's `aot-publish` job exercises the native link.

## Deviations from Plan

None — plan executed as written. ci.yml required only a documentation/comment update (the plan explicitly allowed "leave the workflow as-is and record that no change was needed"; the comment update records the verification). No workload step was needed; no source or project-configuration changes were required.

## Issues Encountered
- The MSVC native-link step of the Native AOT publish does not run locally (vswhere/link.exe not on this host). This is expected and matches v1.0 Phase 7 — the IL-analysis (trim/AOT-warning) portion, which is the contamination gate this plan cares about, is warning-free, and the native link is covered by CI. Not a blocker, not a regression.

## User Setup Required
None — no external service configuration required.

## Next Phase Readiness
- Phase 9 now has a guardrail-verified, launchable empty WinUI 3 window to wire the frozen `CombatLogsMonitor.Instance.DpsHps` stream into (capture the UI `DispatcherQueue`; do NOT mutate XAML off the Rx background thread — Pitfall 5 / cross-thread `COMException 0x8001010E`).
- The AOT-contamination regression gate and the CI build coverage are established as the pattern to re-run after every subsequent overlay change (Phases 9–11), including after the WinForms deletion in Phase 11.
- Phase 8 (OVL-01) is complete: both halves of the success criteria (AOT/CI guardrails + clean-profile published-folder launch) are satisfied.

## Self-Check: PASSED

- Commit `f2f6bce` present in git history (verified via `git show --stat`).
- `.github/workflows/ci.yml` modified by `f2f6bce` (build-step comment update verified in the diff).
- This SUMMARY.md written to `.planning/phases/08-winui-3-scaffold-dependencies-guardrails/08-02-SUMMARY.md`.
- REQUIREMENTS.md OVL-01 confirmed already marked `[x]` Complete (set by 08-01; not duplicated).
- No fabricated build results — only the guardrail outcomes the prior executor actually verified are recorded.

---
*Phase: 08-winui-3-scaffold-dependencies-guardrails*
*Completed: 2026-06-12*
