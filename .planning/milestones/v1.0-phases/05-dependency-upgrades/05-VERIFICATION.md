---
phase: 05-dependency-upgrades
verified: 2026-06-12T00:00:00Z
status: human_needed
score: 13/13 must-haves verified (auto-verifiable)
overrides_applied: 0
re_verification:
  previous_status: null
human_verification:
  - test: "Ctrl+C clean stop — managed CLI. Run `dotnet run --project SwtorLogParser.Cli -- monitor`, confirm the Spectre 5-column table renders, then press Ctrl+C."
    expected: "Process exits promptly and cleanly — CancellationTokenSource cancels, WaitHandle.WaitOne() unblocks, Stop() runs, no hang, no unhandled exception."
    why_human: "Requires an interactive console to deliver SIGINT; cannot be exercised headless."
  - test: "Ctrl+C clean stop — Native CLI. Run `dotnet run --project SwtorLogParser.Native.Cli -- monitor`, then press Ctrl+C."
    expected: "Prompt clean exit; in-place PERF-02 row renderer behaves as before; Stop() runs."
    why_human: "Requires an interactive console to deliver SIGINT; cannot be exercised headless."
  - test: "Native AOT publish link step. After installing the VS 2022 'Desktop development with C++' workload (provides link.exe + vswhere.exe), run `dotnet publish SwtorLogParser.Native.Cli -c Release`."
    expected: "Produces a native executable with no IL2xxx/IL3xxx trim/AOT warnings. (AOT code-generation stage already verified clean here; only the MSVC native link step is blocked by the missing toolchain.)"
    why_human: "MSVC linker (link.exe/vswhere.exe) is not on PATH in this environment — environment limitation, not a code/phase failure. AOT-safety is already proven by zero IL warnings at the ILCompiler stage."
  - test: "list parity (both hosts). Run `dotnet run --project SwtorLogParser.Cli -- list` and `dotnet run --project SwtorLogParser.Native.Cli -- list`."
    expected: "Both print the combat-log files exactly as before."
    why_human: "Output depends on local SWTOR combat-log files present on the user's machine; not deterministically verifiable in CI."
---

# Phase 5: Dependency Upgrades Verification Report

**Phase Goal:** Every NuGet package is on a stable GA release managed centrally; the CLI host no longer depends on the abandoned System.CommandLine.Rendering 0.4.0-alpha
**Verified:** 2026-06-12
**Status:** human_needed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| #   | Truth | Status | Evidence |
| --- | ----- | ------ | -------- |
| 1 | Directory.Packages.props exists at root with ManagePackageVersionsCentrally=true | ✓ VERIFIED | `Directory.Packages.props:4` — `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`; 7 GA `<PackageVersion>` entries |
| 2 | No PackageReference in any csproj carries a Version= attribute | ✓ VERIFIED | Grep `Version=`/`VersionOverride` across all `*.csproj` → only hit is the commented-out Overlay line (`<!-- ... -->`); every active ref is bare |
| 3 | No package pinned to preview/alpha/beta — all GA | ✓ VERIFIED | Grep `preview\|alpha\|beta\|-pre` (case-insensitive) → only `<LangVersion>preview</LangVersion>` (not a package) and a commented-out Overlay ref. Props: System.Reactive 6.0.2, Logging.Abstractions 8.0.3, Spectre.Console 0.57.0, xunit 2.9.3, xunit.runner.visualstudio 3.1.5, Microsoft.NET.Test.Sdk 18.6.0, coverlet.collector 6.0.4 |
| 4 | `dotnet restore SwtorLogParser.slnx` succeeds, no NU1008/NU1010 | ✓ VERIFIED | Ran: restore completed, projects restored, no NU errors |
| 5 | `dotnet build SwtorLogParser.slnx -c Debug` succeeds | ✓ VERIFIED | Ran: "Build succeeded. 0 Error(s)". Only 1 pre-existing CS0108 warning in Overlay/ParserForm.cs:140 (out of scope, unrelated to deps) |
| 6 | `dotnet test` GREEN, 106 passing, zero skips | ✓ VERIFIED | Ran: "Passed! Failed: 0, Passed: 106, Skipped: 0, Total: 106" |
| 7 | Core lib stays IsAotCompatible=true | ✓ VERIFIED | `SwtorLogParser.csproj:6` — `<IsAotCompatible>true</IsAotCompatible>` retained |
| 8 | Core lib references only System.Reactive + Logging.Abstractions (both bare); dead refs gone | ✓ VERIFIED | `SwtorLogParser.csproj:15-16` — exactly those two bare refs; no DependencyInjection/Logging.Console/Logging.Debug |
| 9 | Neither CLI references System.CommandLine(.Rendering) in csproj or code | ✓ VERIFIED | Grep `System\.CommandLine` across `.cs`+`.csproj` (excluding .planning) → zero matches in source; only CLAUDE.md docs mention it |
| 10 | Both hosts dispatch list/monitor via hand-rolled switch on args[0]; unknown → usage + exit 1 | ✓ VERIFIED | `Cli/Program.cs:11-25` and `Native.Cli/Program.cs:8-22` — `switch (args.Length > 0 ? args[0] : "")`, default writes usage to `Console.Error`, returns 1 |
| 11 | Ctrl+C → CancellationTokenSource → Start(token)/Stop() wired in both hosts | ✓ VERIFIED (code path) | `Cli/Program.cs:29-44` and `Native.Cli/Program.cs:26-44` — `Console.CancelKeyPress += (_,e)=>{e.Cancel=true; cts.Cancel();}` → WaitOne unblocks → `Stop()`. Runtime Ctrl+C behavior is human-verify |
| 12 | Managed CLI renders 5-column table via Spectre.Console (N format, '-' for nulls); Native CLI has NO Spectre ref | ✓ VERIFIED | `Cli/Program.cs:51-67` — Spectre `Table`, columns Player/dps/(crit %)/hps/(crit %), `ToString("N")`, `"-"`. Native CLI: grep `Spectre\|AnsiConsole` → zero matches; csproj has no Spectre ref |
| 13 | DockerDefaultTargetOS absent from every csproj | ✓ VERIFIED | Grep `DockerDefaultTargetOS` across `*.csproj` → no matches |

**Score:** 13/13 auto-verifiable truths VERIFIED

### Required Artifacts

| Artifact | Expected | Status | Details |
| -------- | -------- | ------ | ------- |
| `Directory.Packages.props` | CPM source, 7 GA versions | ✓ VERIFIED | ManagePackageVersionsCentrally=true + 7 GA PackageVersion entries, no preview/alpha/beta |
| `SwtorLogParser/SwtorLogParser.csproj` | dead refs removed, explicit Logging.Abstractions, AOT-compatible | ✓ VERIFIED | Two bare refs; IsAotCompatible=true |
| `SwtorLogParser.Cli/Program.cs` | dispatch + Spectre table + Ctrl+C bridge | ✓ VERIFIED | All present, no System.CommandLine types |
| `SwtorLogParser.Cli/SwtorLogParser.Cli.csproj` | Spectre ref, no Rendering, no Docker prop | ✓ VERIFIED | Bare Spectre.Console; no System.CommandLine.Rendering; no DockerDefaultTargetOS |
| `SwtorLogParser.Native.Cli/Program.cs` | dispatch + Ctrl+C bridge; PERF-02 renderer preserved | ✓ VERIFIED | Switch dispatch, CancelKeyPress bridge, in-place renderer byte-preserved, Spectre-free |
| `SwtorLogParser.Native.Cli/SwtorLogParser.Native.Cli.csproj` | no System.CommandLine, no Docker, PublishAot=true, no Spectre | ✓ VERIFIED | Only Logging.Abstractions ref; PublishAot=true; no Docker prop; Spectre-free |

### Key Link Verification

| From | To | Via | Status | Details |
| ---- | -- | --- | ------ | ------- |
| every csproj PackageReference | Directory.Packages.props PackageVersion | CPM bare Include | ✓ WIRED | Restore resolves all versions centrally, no NU1008/NU1010 |
| Ctrl+C (Console.CancelKeyPress) | CombatLogsMonitor.Instance.Stop() | CTS.Cancel() → WaitHandle.WaitOne() unblocks → Stop() | ✓ WIRED (code) | Both Program.cs files; runtime confirmation is human-verify |
| Cli Update() | Spectre.Console Table | AnsiConsole.Write(table) with 5 AddColumn/AddRow | ✓ WIRED | Cli/Program.cs:51-67 |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
| -------- | ------- | ------ | ------ |
| Restore is NU1008/NU1010-clean | `dotnet restore SwtorLogParser.slnx` | Restored, no NU errors | ✓ PASS |
| Solution builds | `dotnet build SwtorLogParser.slnx -c Debug` | Build succeeded, 0 errors | ✓ PASS |
| Tests green | `dotnet test SwtorLogParser.Tests/...` | 106 passed / 0 failed / 0 skipped | ✓ PASS |
| AOT code-gen produces no IL warnings | `dotnet publish SwtorLogParser.Native.Cli -c Release` | "Generating native code" reached, ZERO IL2xxx/IL3xxx warnings; fails only at MSVC link.exe (vswhere.exe not recognized, exit 123) | ✓ PASS (AOT-safety) / link step is env-gap |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
| ----------- | ----------- | ----------- | ------ | -------- |
| DEP-01 | 05-01 | All NuGet packages on stable GA versions | ✓ SATISFIED | All 7 props versions GA; no preview/alpha/beta active anywhere; restore GA-only |
| DEP-02 | 05-01 | Versions centrally managed via Directory.Packages.props | ✓ SATISFIED | CPM enabled; no per-csproj version attributes on active refs |
| DEP-03 | 05-02 | CLI rendering no longer depends on System.CommandLine.Rendering 0.4.0-alpha | ✓ SATISFIED | Zero System.CommandLine refs in source; managed CLI renders via Spectre.Console |
| INFRA-02 | 05-02 | DockerDefaultTargetOS removed from CLI projects | ✓ SATISFIED | No DockerDefaultTargetOS in any csproj |

No orphaned requirements — REQUIREMENTS.md maps DEP-01/02/03 and INFRA-02 to Phase 5, all claimed by plans.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
| ---- | ---- | ------- | -------- | ------ |
| SwtorLogParser.Overlay/ParserForm.cs | 140 | CS0108 warning (MouseDown hides inherited member) | ℹ️ Info | Pre-existing, out of scope, unrelated to dependency changes (documented in both summaries) |

No debt markers (TODO/FIXME/XXX/TBD/HACK/PLACEHOLDER) in any source file. No stubs, no empty implementations introduced by this phase.

**Note on 05-01 VersionOverride:** The 05-01 SUMMARY documents a transient `VersionOverride` on the two System.CommandLine refs (an auto-fixed deviation because NU1008 fires on any `Version=` under this SDK). 05-02 deleted those references entirely. Confirmed: no `VersionOverride` attribute remains on any active PackageReference in the codebase.

### Human Verification Required

1. **Ctrl+C clean stop — managed CLI**
   - Test: `dotnet run --project SwtorLogParser.Cli -- monitor`, confirm 5-column Spectre table, press Ctrl+C
   - Expected: prompt clean exit, Stop() runs, no hang/exception
2. **Ctrl+C clean stop — Native CLI**
   - Test: `dotnet run --project SwtorLogParser.Native.Cli -- monitor`, press Ctrl+C
   - Expected: prompt clean exit, in-place renderer behaves as before
3. **Native AOT publish link step**
   - Test: install VS 2022 "Desktop development with C++" workload, then `dotnet publish SwtorLogParser.Native.Cli -c Release`
   - Expected: native binary produced, no IL2xxx/IL3xxx warnings (AOT code-gen already verified clean here; only the MSVC link step is blocked by the missing toolchain in this environment)
4. **list parity (both hosts)**
   - Test: `dotnet run --project SwtorLogParser.Cli -- list` and `dotnet run --project SwtorLogParser.Native.Cli -- list`
   - Expected: both print the combat-log files as before

### Gaps Summary

No gaps. All four ROADMAP success criteria are met in the actual codebase and confirmed by the three gates run during verification (restore clean, build 0 errors, 106/0/0 tests). All four requirement IDs (DEP-01, DEP-02, DEP-03, INFRA-02) are satisfied with source evidence.

The phase goal is achieved as far as automated verification can prove. The only outstanding items are inherently human/interactive: runtime Ctrl+C clean-stop on both hosts, the Native AOT native-link step (blocked solely by the absent MSVC toolchain — an environment limitation, with AOT-safety independently proven by ZERO IL2xxx/IL3xxx warnings at the ILCompiler stage), and `list` parity (depends on local SWTOR log files). Per the phase's documented manual-verification plan, status is **human_needed**.

---

_Verified: 2026-06-12_
_Verifier: Claude (gsd-verifier)_
