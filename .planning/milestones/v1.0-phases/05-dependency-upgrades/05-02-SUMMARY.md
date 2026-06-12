---
phase: 05-dependency-upgrades
plan: 02
subsystem: cli-hosts
tags: [system.commandline-removal, spectre.console, hand-rolled-dispatch, ctrl-c-cancellation, native-aot, infra-cleanup]

# Dependency graph
requires:
  - phase: 05-dependency-upgrades
    provides: "05-01 CPM + Directory.Packages.props declaring Spectre.Console 0.57.0 centrally; the two System.CommandLine refs isolated via VersionOverride for clean deletion here"
provides:
  - "Both CLI hosts free of System.CommandLine and System.CommandLine.Rendering (no refs, no usings, no types)"
  - "Hand-rolled switch(args[0]) dispatch (list/monitor/usage) in both hosts; unknown arg -> stderr usage + exit 1"
  - "Ctrl+C cancellation re-wired via Console.CancelKeyPress -> CancellationTokenSource feeding Start(token) + explicit Stop()"
  - "Managed CLI live 5-column table rendered via Spectre.Console (replaces System.CommandLine.Rendering TableView)"
  - "Native AOT CLI keeps its PERF-02 in-place renderer and stays Spectre-free"
  - "DockerDefaultTargetOS=Linux removed from both CLI csproj (INFRA-02)"
affects: [06-ci]

# Tech tracking
tech-stack:
  added: [Spectre.Console 0.57.0 (consumed by managed CLI only)]
  removed: [System.CommandLine 2.0.0-beta4.22272.1, System.CommandLine.Rendering 0.4.0-alpha.22272.1]
  patterns: [hand-rolled switch(args[0]) command dispatch (AOT-safe, zero-dependency), Console.CancelKeyPress -> CancellationTokenSource Ctrl+C bridge, Spectre.Console Table rebuilt-per-tick + Clear+Write full re-render]

key-files:
  created: []
  modified:
    - SwtorLogParser.Native.Cli/Program.cs
    - SwtorLogParser.Native.Cli/SwtorLogParser.Native.Cli.csproj
    - SwtorLogParser.Cli/Program.cs
    - SwtorLogParser.Cli/SwtorLogParser.Cli.csproj

key-decisions:
  - "Deleted the two System.CommandLine PackageReference lines outright (they used VersionOverride from 05-01) — no central PackageVersion was ever added, so removal required only deleting the reference lines"
  - "Managed CLI OnCombatLogAdded ported to AnsiConsole.MarkupLineInterpolated([grey]...[/]) for consistency with the new Spectre.Console rendering path (behavior-equivalent filename surfacing)"
  - "Managed CLI Update uses parity-first AnsiConsole.Clear() + AnsiConsole.Write(table) full re-render (matches the old fixed-Region(0,0) re-render); did not adopt AnsiConsole.Live (optional, not required)"
  - "Native AOT renderer (FormatRow / SetCursorPosition / _lastRowCount / IsOutputRedirected guards) left byte-identical — only command + cancellation plumbing changed"

requirements-completed: [DEP-03, INFRA-02]

# Metrics
duration: 7min
completed: 2026-06-12
---

# Phase 05 Plan 02: System.CommandLine Removal + Spectre.Console Wiring Summary

**Removed the last preview/alpha/beta dependencies (System.CommandLine beta + System.CommandLine.Rendering alpha) from both CLI hosts, replaced the RootCommand setup with hand-rolled switch(args[0]) dispatch, re-wired Ctrl+C through Console.CancelKeyPress -> CancellationTokenSource, ported the managed CLI's 5-column live table to Spectre.Console, kept the Native AOT host's PERF-02 renderer untouched and Spectre-free, and dropped DockerDefaultTargetOS from both csproj — solution builds and all 106 tests stay green.**

## Performance

- **Duration:** ~7 min
- **Completed:** 2026-06-12
- **Tasks:** 2 auto + 1 human-verify checkpoint
- **Files modified:** 4 (0 created, 4 edited)

## Accomplishments
- **Native AOT CLI (Task 1):** Replaced `RootCommand`/`Command`/`SetHandler`/`InvokeAsync` with a `switch (args[0])` dispatch (`list` -> 0, `monitor` -> 0, default -> usage on stderr + exit 1). Re-wired cancellation: `Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); }` feeding the SAME `ManualResetEvent.SetSafeWaitHandle(token.WaitHandle.SafeWaitHandle)` + `WaitOne()` blocking pattern; added explicit `CombatLogsMonitor.Instance.Stop()` after `WaitOne()`. PERF-02 in-place renderer (`Update`/`FormatRow`/`OnCombatLogAdded`/`_lastRowCount`/`IsOutputRedirected` guards) left byte-identical.
- **Native csproj:** Deleted the `System.CommandLine` `PackageReference` and the `DockerDefaultTargetOS=Linux` property. `<PublishAot>true</PublishAot>` and the bare `Microsoft.Extensions.Logging.Abstractions` ref preserved; no Spectre.Console added (AOT-safe invariant held).
- **Managed CLI (Task 2):** Same `switch (args[0])` dispatch shape. Dropped `ConsoleRenderer`, `OutputMode.Ansi`, `TableView`, `Region`, `context.GetCancellationToken()`, `context.Console.GetTerminal()`, `terminal.HideCursor()`. Ctrl+C bridge identical to Native; cursor-hide replaced with `Console.CursorVisible = false`. Ported the 5-column `TableView` to a Spectre.Console `Table` rebuilt per tick (headers `Player`/`dps`/`(crit %)`/`hps`/`(crit %)` in order, `"N"` format, `"-"` for nulls), rendered via `AnsiConsole.Clear()` + `AnsiConsole.Write(table)`. `OnCombatLogAdded` now emits the filename via `AnsiConsole.MarkupLineInterpolated($"[grey]{combatLog.FileInfo}[/]")`. `ListCombatLogs` unchanged.
- **Managed csproj:** Deleted the `System.CommandLine.Rendering` ref and the `DockerDefaultTargetOS=Linux` property; added a bare `<PackageReference Include="Spectre.Console" />` (version resolves centrally from `Directory.Packages.props`, 0.57.0). `<PublishAot>false</PublishAot>` and `<LangVersion>preview</LangVersion>` retained.
- **Gates green:** `dotnet build SwtorLogParser.slnx` succeeds; `dotnet test` -> 106 passed, 0 skipped, 0 failed. Repo-wide grep confirms ZERO `System.CommandLine` references in any `.cs`/`.csproj` and ZERO `DockerDefaultTargetOS` in any csproj.

## Task Commits

Each auto task was committed atomically:

1. **Task 1: Native AOT CLI — hand-rolled dispatch + Ctrl+C bridge, drop System.CommandLine + Docker prop** - `30ac67d` (feat)
2. **Task 2: Managed CLI — hand-rolled dispatch + Spectre.Console table + Ctrl+C bridge, drop System.CommandLine.Rendering + Docker prop** - `94c6db2` (feat)

## Files Created/Modified
- `SwtorLogParser.Native.Cli/Program.cs` - `Main` -> sync `switch(args[0])` dispatch; `MonitorCombatLogs()` parameterless with `Console.CancelKeyPress` -> `CancellationTokenSource` bridge + explicit `Stop()`; PERF-02 renderer byte-identical; all `System.CommandLine` usings/types removed.
- `SwtorLogParser.Native.Cli/SwtorLogParser.Native.Cli.csproj` - Removed `System.CommandLine` ref + `DockerDefaultTargetOS`; `PublishAot=true` + Logging.Abstractions preserved; Spectre-free.
- `SwtorLogParser.Cli/Program.cs` - `using Spectre.Console;`; `switch(args[0])` dispatch; Ctrl+C bridge; Spectre.Console `Table` 5-column live render via `AnsiConsole.Clear()`+`Write`; `Region` field deleted; `List` retained.
- `SwtorLogParser.Cli/SwtorLogParser.Cli.csproj` - Removed `System.CommandLine.Rendering` ref + `DockerDefaultTargetOS`; added bare `Spectre.Console` ref; `PublishAot=false` + `LangVersion=preview` retained.

## Decisions Made
- **Reference lines deleted outright:** Because 05-01 isolated both System.CommandLine refs via `VersionOverride` (no central `PackageVersion`), removal here was a clean delete of the `<PackageReference>` lines with no `Directory.Packages.props` change needed.
- **OnCombatLogAdded -> Spectre markup** in the managed host: chose `AnsiConsole.MarkupLineInterpolated([grey]...[/])` (one of the two plan-sanctioned options) for consistency with the new Spectre render path; behavior-equivalent (filename still surfaces on new log).
- **Parity-first full re-render** (`AnsiConsole.Clear()` + `Write`) over the optional `AnsiConsole.Live` — matches the old fixed-Region(0,0) re-render behavior.

## Deviations from Plan

None — plan executed exactly as written. Both auto tasks landed as specified; the checkpoint (Task 3) is documented below as a human-verify item.

## Human-Verify / Manual-Only Items (NOT blocking)

These cannot be automated in a headless environment. Recorded here for the user to confirm (per plan Task 3 + critical constraints):

### 1. Native AOT publish — ATTEMPTED, environment toolchain gap (NOT a code failure)
- **Command:** `dotnet publish SwtorLogParser.Native.Cli -c Release`
- **Result:** The managed compile and the **ILCompiler native-code generation stage both succeeded with NO trim/AOT (IL2xxx/IL3xxx) warnings** about the removed dependencies. The publish then failed at the **MSVC native link step**: `'vswhere.exe' is not recognized...` and `link.exe ... exited with code 123` (MSB3073, from `Microsoft.NETCore.Native.targets`). This is the documented environment gap (05-RESEARCH Pitfall 5 / Environment Availability) — the VS C++ / MSVC linker is not on PATH in this build environment. The code change itself is AOT-correct: the managed build is green, tests pass, no Spectre.Console was added to the AOT host, `PublishAot=true` is retained, and AOT code generation produced no new warnings.
- **User action:** Install / put on PATH the VS 2022 "Desktop development with C++" workload (provides `link.exe` + `vswhere.exe`) and re-run the publish to confirm a clean native binary.

### 2. Ctrl+C clean stop — managed CLI (interactive)
- **Command:** `dotnet run --project SwtorLogParser.Cli -- monitor`
- **Expect:** Spectre table renders the 5 columns (Player, dps, (crit %), hps, (crit %)); pressing Ctrl+C exits promptly and cleanly (CancellationTokenSource cancels -> `WaitHandle.WaitOne()` unblocks -> `Stop()` runs; no hang, no unhandled exception).

### 3. Ctrl+C clean stop — Native CLI (interactive)
- **Command:** `dotnet run --project SwtorLogParser.Native.Cli -- monitor`
- **Expect:** Prompt clean exit on Ctrl+C; in-place row renderer behaves as before.

### 4. list parity (both hosts)
- **Command:** `dotnet run --project SwtorLogParser.Cli -- list` and `dotnet run --project SwtorLogParser.Native.Cli -- list`
- **Expect:** Both print the combat-log files exactly as before.

## Issues Encountered
- **Pre-existing CS0108 warning (out of scope):** `dotnet build` still emits one CS0108 in `SwtorLogParser.Overlay/ParserForm.cs:140` (`MouseDown` hides inherited member). Unrelated to this plan's CLI changes; same warning noted in 05-01 — not fixed (scope boundary).
- **AOT MSVC linker gap:** see Human-Verify item 1 — environment, not code.

## User Setup Required
- To produce the Native AOT binary: install the VS 2022 C++ build tools (Desktop development with C++ workload) so `link.exe`/`vswhere.exe` are available, then re-run `dotnet publish SwtorLogParser.Native.Cli -c Release`.

## Next Phase Readiness
- DEP-03 + INFRA-02 complete: no preview/alpha/beta dependencies remain in the CLI hosts; the only new dependency is GA Spectre.Console 0.57.0 (managed host only). Phase 5 ROADMAP criteria 3 (Spectre.Console live table without System.CommandLine.Rendering) and 4 (no DockerDefaultTargetOS) satisfied.
- Native AOT host stays Spectre-free with `PublishAot=true`; AOT publishability is code-ready pending the local MSVC toolchain.
- No code blockers for Phase 06 (CI).

## Self-Check: PASSED

- SwtorLogParser.Native.Cli/Program.cs — FOUND
- SwtorLogParser.Cli/Program.cs — FOUND
- SwtorLogParser.Native.Cli/SwtorLogParser.Native.Cli.csproj — FOUND
- SwtorLogParser.Cli/SwtorLogParser.Cli.csproj — FOUND
- .planning/phases/05-dependency-upgrades/05-02-SUMMARY.md — FOUND
- Commit 30ac67d (Task 1) — FOUND
- Commit 94c6db2 (Task 2) — FOUND

---
*Phase: 05-dependency-upgrades*
*Completed: 2026-06-12*
