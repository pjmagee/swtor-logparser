---
phase: quick-260612-hjb
verified: 2026-06-12T13:05:00Z
status: human_needed
score: 7/7 must-haves verified
overrides_applied: 0
human_verification:
  - test: "Run `SwtorLogParser.Cli monitor` and `SwtorLogParser.Native.Cli monitor` against a live/active combat log in an interactive terminal."
    expected: "Both hosts render the IDENTICAL Spectre live table (columns Player / dps / (crit %) / hps / (crit %)) with the current filename pinned as a grey title above the rows, updating in place without screen-clearing. The Native CLI no longer shows the old 'DPS: x (y%); HPS:' cursor-positioned output."
    why_human: "Spectre AnsiConsole.Live in-place refresh and the grey TableTitle pin (WR-03/WR-04) are visual terminal behaviors that cannot be confirmed by static grep or build; they require a live combat-log stream and an interactive console buffer."
  - test: "Press Ctrl+C once during the monitor loop, then press Ctrl+C again (or press it after the loop has begun shutting down)."
    expected: "The process exits cleanly; no ObjectDisposedException is thrown on the SIGINT handler thread by the second/late Ctrl+C; the cursor is restored visible."
    why_human: "The race between the disposed CancellationTokenSource and a late SIGINT is timing-dependent runtime behavior. The guard (try/catch ObjectDisposedException) is verified present in source, but exercising the late-Ctrl+C path is a runtime check."
  - test: "Pipe/redirect either host's monitor output to a file (e.g. `SwtorLogParser.Native.Cli monitor > out.txt`)."
    expected: "Output degrades to plain per-row line writes (no cursor/live ANSI ops), preserving WR-05. No Spectre live-frame escape codes in the captured file."
    why_human: "The non-interactive fallback path depends on Console.IsOutputRedirected / AnsiConsole interactive capability detection at runtime; the branch is verified present in source but its actual redirected output must be observed."
---

# Quick 260612-hjb: Unify both CLI hosts behind a shared Spectre renderer — Verification Report

**Task Goal:** Unify both CLI hosts behind a shared Spectre.Console live renderer (AOT-safe). A new `SwtorLogParser.Cli.Common` library holds the shared renderer; both `SwtorLogParser.Cli` (managed) and `SwtorLogParser.Native.Cli` (Native AOT) are thin forwarders to `SwtorCliApp.Run`. Must stay AOT-clean; whole solution builds; no regression to live DPS/HPS stream or redirected-output fallback (WR-03/04/05); ObjectDisposedException guard on late Ctrl+C preserved.

**Verified:** 2026-06-12T13:05:00Z
**Status:** human_needed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
| - | ----- | ------ | -------- |
| 1 | Shared `SwtorLogParser.Cli.Common` exists: net10.0, IsAotCompatible=true, version-less Spectre.Console ref, ProjectReference to core | ✓ VERIFIED | `SwtorLogParser.Cli.Common.csproj`: `<TargetFramework>net10.0</TargetFramework>` (l.4), `<IsAotCompatible>true</IsAotCompatible>` (l.7), `<ProjectReference Include="..\SwtorLogParser\SwtorLogParser.csproj"/>` (l.11), `<PackageReference Include="Spectre.Console"/>` with NO Version (l.15). Version 0.57.0 centrally managed in Directory.Packages.props. |
| 2 | Core `SwtorLogParser` library did NOT gain any Spectre/console dependency | ✓ VERIFIED | `SwtorLogParser.csproj` PackageReferences are only `Microsoft.Extensions.Logging.Abstractions` + `System.Reactive` (l.15-16). Grep for "Spectre" in core csproj: no matches. |
| 3 | Both `Program.cs` are one-line forwarders to `SwtorCliApp.Run(args)` | ✓ VERIFIED | `SwtorLogParser.Cli/Program.cs` l.5 and `SwtorLogParser.Native.Cli/Program.cs` l.5 both: `public static int Main(string[] args) => SwtorLogParser.Cli.Common.SwtorCliApp.Run(args);`. All old rendering/monitor/list code deleted. |
| 4 | Shared renderer preserves ObjectDisposedException guard, WR-03, WR-04, WR-05, DpsHps.Subscribe seam, and CombatLogsMonitor Start/Stop lifecycle | ✓ VERIFIED | `SwtorCliApp.cs`: ObjectDisposed guard l.55 `try { cts.Cancel(); } catch (ObjectDisposedException)`; WR-03 grey TableTitle l.122-124; WR-04 `AnsiConsole.Live(Table).Start(...)` l.76; WR-05 plain-write fallback l.108-113; `DpsHps.Subscribe(Update)` l.71/87; `Start(cts.Token)` l.72/88, `Stop()` l.82/92; CancelKeyPress unsubscribe in finally l.98. |
| 5 | Native.Cli no longer references Console.SetCursorPosition (none in host or shared lib) | ✓ VERIFIED | Grep "SetCursorPosition" across repo: matches only in `.planning/` docs; ZERO source files contain it. Native CLI cursor math removed. |
| 6 | Both CLI exe projects reference `SwtorLogParser.Cli.Common` | ✓ VERIFIED | `SwtorLogParser.Cli.csproj` l.17 and `SwtorLogParser.Native.Cli.csproj` l.22 both have `<ProjectReference Include="..\SwtorLogParser.Cli.Common\SwtorLogParser.Cli.Common.csproj"/>`. |
| 7 | Whole solution builds in Release; shared lib AOT-clean (0 IL warnings) | ✓ VERIFIED | `dotnet build SwtorLogParser.slnx -c Release` → Build succeeded, 0 errors. `dotnet build SwtorLogParser.Cli.Common -c Release` → 0 Warnings, 0 Errors (IsAotCompatible analyzers emit nothing). |

**Score:** 7/7 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
| -------- | -------- | ------ | ------- |
| `SwtorLogParser.Cli.Common/SwtorLogParser.Cli.Common.csproj` | net10.0 IsAotCompatible lib w/ version-less Spectre + core ProjectReference | ✓ VERIFIED | All required properties present (l.4,7,11,15); compiles standalone Release clean. |
| `SwtorLogParser.Cli.Common/SwtorCliApp.cs` | `public static int Run(string[])` owning dispatch, CTS/CancelKeyPress, interactive detection, Spectre Table+Live, redirected fallback, list | ✓ VERIFIED | 163 lines (> 100 min); `public static int Run` l.30; all required behaviors present and substantive. |
| `SwtorLogParser.Cli/Program.cs` | Thin Main forwarding to SwtorCliApp.Run | ✓ VERIFIED | 7 lines; single forwarder l.5. |
| `SwtorLogParser.Native.Cli/Program.cs` | Thin Main forwarding to SwtorCliApp.Run | ✓ VERIFIED | 7 lines; single forwarder l.5. |

### Key Link Verification

| From | To | Via | Status | Details |
| ---- | -- | --- | ------ | ------- |
| `SwtorLogParser.Cli/Program.cs` | `SwtorCliApp.Run` | static call from Main | ✓ WIRED | l.5 `=> SwtorLogParser.Cli.Common.SwtorCliApp.Run(args)` |
| `SwtorLogParser.Native.Cli/Program.cs` | `SwtorCliApp.Run` | static call from Main | ✓ WIRED | l.5 `=> SwtorLogParser.Cli.Common.SwtorCliApp.Run(args)` |
| `SwtorCliApp.cs` | `CombatLogsMonitor.Instance.DpsHps` | Subscribe in monitor loop | ✓ WIRED | l.71/87 `.DpsHps.Subscribe(Update)`; target `DpsHps` exists on monitor type (CombatLogsMonitor.cs:38) |
| `SwtorLogParser.slnx` | `SwtorLogParser.Cli.Common.csproj` | `<Project Path=...>` entry | ✓ WIRED | slnx l.10 registers the project |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
| -------- | ------------- | ------ | ------------------ | ------ |
| `SwtorCliApp.cs` (live table rows) | `List.Items` (PlayerStats) | `CombatLogsMonitor.Instance.DpsHps.Subscribe(Update)` → `List.AddOrUpdate` | Yes — fed by the live Rx DpsHps stream; Start/Stop lifecycle present (CombatLogsMonitor.cs:177/192) | ✓ FLOWING |
| `SwtorCliApp.cs` (table title) | `_currentFile` | `OnCombatLogAdded` ← `CombatLogAdded` event (CombatLogsMonitor.cs:25) | Yes — real event source | ✓ FLOWING |

Note: The DpsHps stream itself was frozen/corrected by prerequisite task quick-260612-dso; this task consumes it unchanged. No stub or hardcoded data path introduced — the renderer reads from the live monitor.

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
| -------- | ------- | ------ | ------ |
| Shared lib compiles AOT-clean | `dotnet build SwtorLogParser.Cli.Common -c Release` | Build succeeded, 0 Warnings, 0 Errors | ✓ PASS |
| Whole solution builds | `dotnet build SwtorLogParser.slnx -c Release` | Build succeeded, 0 Errors (5 pre-existing CS warnings in unrelated Overlay) | ✓ PASS |
| Run dispatch / list / monitor at runtime | (requires interactive console + live log) | — | ? SKIP → human |

### Probe Execution

No project probes declared for this quick task (no `scripts/*/tests/probe-*.sh`); not a probe-gated phase. N/A.

### Requirements Coverage

`requirements-completed: []` in SUMMARY frontmatter; this is a CONCERNS.md-driven dedup/refactor task with no formal REQ-ID mapping. The WR-03/04/05 working-rules and the AOT/build gates are covered by truths 4 and 7 above.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
| ---- | ---- | ------- | -------- | ------ |
| — | — | None | — | No TBD/FIXME/XXX/TODO/PLACEHOLDER markers in modified source files. `return null` (RebuildTable title l.123) and `_live = null` (l.97) are intentional state, not stubs. No empty-data stubs; renderer reads live stream. |

### Human Verification Required

The structural goal (one shared renderer; both hosts thin forwarders; AOT-clean; whole solution builds; no SetCursorPosition; ObjectDisposed guard / WR-03/04/05 constructs present in source) is fully VERIFIED by code inspection and build. The following are inherently runtime/visual and cannot be confirmed without an interactive console attached to a live SWTOR combat log:

1. **Identical live Spectre table in both hosts** — run both `monitor` commands interactively; confirm matching Player/dps/(crit %)/hps/(crit %) table with grey filename title, in-place refresh, and that the Native CLI no longer prints the old cursor-positioned `DPS: x (y%); HPS:` format.
2. **Late/second Ctrl+C does not throw ObjectDisposedException** — press Ctrl+C twice (or once after shutdown begins); confirm clean exit and cursor restored.
3. **Redirected output degrades to plain line writes (WR-05)** — pipe monitor output to a file; confirm plain rows, no live ANSI frames.

### Gaps Summary

No gaps. All 7 must-have truths are VERIFIED against the actual codebase, both build gates pass, and all key links/data flows resolve to real targets. The Native AOT native-link step (MSB3073) remains environment-gated (MSVC not on shell PATH) as in prior phases — the IL analysis that proves AOT-safety completed clean (0 IL2026/IL2104/IL3050/IL3053), and IsAotCompatible build-time analysis on the shared lib is warning-free. Status is `human_needed` solely because the visual live-table rendering, the late-SIGINT runtime race, and the redirected-fallback runtime output are not statically observable — the code constructs implementing them are all confirmed present.

---

_Verified: 2026-06-12T13:05:00Z_
_Verifier: Claude (gsd-verifier)_
