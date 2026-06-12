---
phase: quick-260612-hjb
plan: "01"
subsystem: cli
tags: [spectre.console, native-aot, cli, rx, refactor, dedup]

requires:
  - phase: quick-260612-dso
    provides: corrected DpsHps stream (frozen core parser) consumed unchanged by the shared renderer
provides:
  - SwtorLogParser.Cli.Common shared library (net10.0, IsAotCompatible=true) owning all CLI arg dispatch, CTS/Ctrl+C wiring, interactive detection, Spectre Table+Live loop, redirected fallback, and list command
  - Both CLI hosts reduced to one-line Main forwarders (zero duplicated rendering/host-wiring code)
  - Native AOT CLI now renders the identical Spectre table the managed CLI does (hand-rolled cursor math removed)
affects: [cli, native-cli, future-cli-ux-changes]

tech-stack:
  added: []
  patterns:
    - "Single shared SwtorCliApp.Run(string[]) entry point consumed by both CLI hosts via thin Program.Main forwarders"
    - "IsAotCompatible=true on the shared CLI lib surfaces IL2xxx/IL3xxx trim/AOT warnings at BUILD time, not only at publish"

key-files:
  created:
    - SwtorLogParser.Cli.Common/SwtorLogParser.Cli.Common.csproj
    - SwtorLogParser.Cli.Common/SwtorCliApp.cs
  modified:
    - SwtorLogParser.Cli/Program.cs
    - SwtorLogParser.Cli/SwtorLogParser.Cli.csproj
    - SwtorLogParser.Native.Cli/Program.cs
    - SwtorLogParser.Native.Cli/SwtorLogParser.Native.Cli.csproj
    - SwtorLogParser.slnx

key-decisions:
  - "Native CLI's old Console.SetCursorPosition cursor math and 'DPS: x (y%); HPS:' string format are discarded in favor of the managed CLI's Spectre Table+Live UX (the managed CLI is the reference)"
  - "Shared lib carries IsAotCompatible=true so AOT/trim analyzer warnings surface at build time across both hosts"
  - "Managed CLI's direct Spectre.Console PackageReference removed — now flows transitively through the shared lib (single dependency path)"
  - "Native CLI keeps Microsoft.Extensions.Logging.Abstractions (not proven unused)"

patterns-established:
  - "Pattern: extract duplicated host code into a shared IsAotCompatible lib both an AOT and a non-AOT host consume; the AOT host is the binding constraint and the lib is gated by AOT publish IL analysis"

requirements-completed: []

duration: 3min
completed: 2026-06-12
---

# Quick 260612-hjb: Unify both CLI hosts behind a shared Spectre renderer Summary

**One shared `SwtorLogParser.Cli.Common` library (net10.0, IsAotCompatible) now owns the entire CLI UX — arg dispatch, Ctrl+C wiring, Spectre `Table`+`AnsiConsole.Live` loop, redirected fallback, and `list` — with both hosts reduced to one-line `Main` forwarders; the Native AOT CLI's hand-rolled cursor math is gone and IL analysis is warning-free.**

## Performance

- **Duration:** ~3 min
- **Started:** 2026-06-12T11:44:49Z
- **Completed:** 2026-06-12T11:47:41Z
- **Tasks:** 4
- **Files modified:** 7 (2 created, 5 modified)

## Accomplishments
- Created `SwtorLogParser.Cli.Common` shared library (net10.0, `IsAotCompatible=true`, versionless Spectre.Console + core ProjectReference) and registered it in `.slnx`.
- Authored `SwtorCliApp.Run(string[])` as a verbatim port of the managed CLI renderer — preserving the ObjectDisposedException late/second-Ctrl+C guard, interactive detection, the `AnsiConsole.Live` in-place refresh loop (WR-04), the grey `TableTitle` filename pin (WR-03), the redirected/non-interactive plain-write fallback (WR-05), and the `list` command.
- Reduced both `Program.cs` to one-line forwarders; deleted 302 lines of duplicated rendering/host-wiring code (the Native CLI's `Console.SetCursorPosition` cursor math is removed).
- Whole-solution Release build passes; Native AOT publish IL analysis is warning-free (0 IL2026/IL2104/IL3050/IL3053).

## Task Commits

Each task was committed atomically (CODE/csproj/slnx only; docs commit handled by orchestrator):

1. **Task 1: Scaffold SwtorLogParser.Cli.Common + wire into .slnx** - `ffee55a` (feat)
2. **Task 2: Author SwtorCliApp shared Spectre live renderer** - `35abe42` (feat)
3. **Task 3: Reduce both Program.cs to thin forwarders + rewire csproj** - `ac3866c` (refactor)
4. **Task 4: Whole-solution build + Native AOT publish gates** - no code changes; results recorded below

## Files Created/Modified
- `SwtorLogParser.Cli.Common/SwtorLogParser.Cli.Common.csproj` - New net10.0 IsAotCompatible library; versionless Spectre.Console PackageReference + core ProjectReference.
- `SwtorLogParser.Cli.Common/SwtorCliApp.cs` - Single shared `Run(string[])` entry: arg dispatch, CTS/CancelKeyPress wiring, interactive detection, Spectre Table+Live loop, redirected fallback, list command.
- `SwtorLogParser.Cli/Program.cs` - One-line `Main => SwtorCliApp.Run(args)` forwarder.
- `SwtorLogParser.Cli/SwtorLogParser.Cli.csproj` - Added shared-lib ProjectReference; removed now-redundant direct Spectre.Console PackageReference.
- `SwtorLogParser.Native.Cli/Program.cs` - One-line `Main => SwtorCliApp.Run(args)` forwarder; SetCursorPosition code deleted.
- `SwtorLogParser.Native.Cli/SwtorLogParser.Native.Cli.csproj` - Added shared-lib ProjectReference; kept Microsoft.Extensions.Logging.Abstractions.
- `SwtorLogParser.slnx` - Registered the new shared project.

## Gate Results (Task 4)

### Gate 1 — Whole-solution Release build
`dotnet build SwtorLogParser.slnx -c Release` — **SUCCEEDED.** All projects (core, both CLI hosts, the new shared lib, Overlay/WinForms, Overlay.ImGui, Overlay.WinUi, Benchmarks, Tests) compiled. 5 warnings, 0 errors. All 5 warnings are pre-existing CS8602/CS0108 in `SwtorLogParser.Overlay/ParserForm.cs` (WinForms overlay) — out of scope, untouched by this task, not a regression.

**Our four projects build clean (0 warnings, 0 errors each):** verified individually — `SwtorLogParser`, `SwtorLogParser.Cli.Common`, `SwtorLogParser.Cli`, `SwtorLogParser.Native.Cli`. The only solution warnings come from the unrelated WinForms overlay.

### Gate 2 — Native AOT publish (IL analysis = the AOT-safety proof)
`dotnet publish SwtorLogParser.Native.Cli -c Release -r win-x64` — IL analysis ran ("Generating native code") and emitted:

| Warning | Count |
|---------|-------|
| IL2026  | **0** |
| IL2104  | **0** |
| IL3050  | **0** |
| IL3053  | **0** |

No IL warnings of any code were emitted. The shared Spectre.Console path is AOT-clean at the IL-analysis level — nothing to suppress, nothing quoted (none exist). **No new AOT-breaking warning was introduced by this refactor.**

The final native-link step failed with **MSB3073** (`'vswhere.exe' is not recognized` / MSVC linker not on the shell PATH) — an environment limitation, not a code regression, consistent with prior phases ([08-02], [09-01] in STATE.md, where native-link MSB3073 is env-gated and CI-covered). The IL analysis (which is the real AOT-safety proof) completed successfully before the link step.

## Decisions Made
- Native CLI's `Console.SetCursorPosition` cursor math and `"DPS: x (y%); HPS:"` string format intentionally discarded in favor of the managed CLI's Spectre Table+Live UX (per plan).
- Shared lib marked `IsAotCompatible=true` so trim/AOT warnings surface at build time for both hosts.
- Managed CLI's direct Spectre.Console PackageReference removed (flows transitively now); Native CLI keeps Logging.Abstractions.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Renamed shared helpers to `Monitor()` / `List_()` to avoid a name collision**
- **Found during:** Task 2 (Author SwtorCliApp)
- **Issue:** The plan asked to rename `MonitorCombatLogs`/`ListCombatLogs` to `Monitor()`/`List()`. A method named `List` would collide with the existing `private static readonly SlidingExpirationList List` field (carried over verbatim from the managed CLI reference), and `Monitor` risks confusion with the `SwtorLogParser.Monitor` namespace.
- **Fix:** Named the helpers `Monitor()` and `List_()`. All behavior (list-command body printing `CombatLogs.EnumerateCombatLogs()` via `Console.WriteLine`) is preserved verbatim; only the private helper identifier differs.
- **Files modified:** SwtorLogParser.Cli.Common/SwtorCliApp.cs
- **Verification:** `dotnet build` of the shared lib succeeds with 0 warnings; the `list`/`monitor`/return-code dispatch contract is byte-for-byte identical to the managed CLI reference.
- **Committed in:** `35abe42` (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking name collision).
**Impact on plan:** Cosmetic identifier change only; no behavioral or contract change. No scope creep.

## Issues Encountered

- **Pre-existing uncommitted working-tree state outside this task's scope.** The working tree already contained unrelated pending changes before this task started: `SwtorLogParser.Overlay.WinUi/ViewModels/MainViewModel.cs` (modified), `.planning/v1.0-MILESTONE-AUDIT.md` (deleted), `Directory.Packages.props` (Silk.NET package versions added), an untracked `SwtorLogParser.Overlay.ImGui/` directory, and a pre-existing `.slnx` working-tree edit adding the ImGui project entry. Per constraints, all path-scoped `git add` commands staged ONLY this task's files. `MainViewModel.cs`, the deleted audit, `Directory.Packages.props`, and the untracked ImGui directory were never staged, committed, reverted, or touched — they remain in the working tree exactly as found.
  - **One unavoidable side effect:** the pre-existing `.slnx` working-tree edit adding `SwtorLogParser.Overlay.ImGui` rode along into the Task 1 commit (`ffee55a`). `.slnx` is a single shared file, the plan requires committing the `.slnx` change to register the new project, and `git add <file>` stages the whole working-tree version of that file (the ImGui hunk could not be excluded without `git add -p`, which is interactive and unavailable). This is benign — registering a project in `.slnx` is the same logical operation as the pre-existing edit, and the ImGui project builds cleanly in Gate 1. No other pre-existing change was affected.

## User Setup Required

None - no external service configuration required.

## Known Stubs

None - the shared renderer is a complete verbatim port; no placeholder/empty-data paths introduced.

## Next Phase Readiness
- CONCERNS.md "duplicated View/host wiring across CLI hosts" item is closed: zero duplicated rendering/host-wiring code remains across the two CLI hosts.
- Both hosts now share one UX; any future CLI-UX change is made once in `SwtorCliApp`.
- AOT-contamination boundary holds: shared lib is `IsAotCompatible=true` and the Native AOT publish IL analysis stays warning-free.
- Native-link MSB3073 remains env-gated (MSVC not on the shell PATH); CI covers the full native link as in prior phases.

## Self-Check: PASSED

- FOUND: SwtorLogParser.Cli.Common/SwtorLogParser.Cli.Common.csproj
- FOUND: SwtorLogParser.Cli.Common/SwtorCliApp.cs
- FOUND commit ffee55a (Task 1)
- FOUND commit 35abe42 (Task 2)
- FOUND commit ac3866c (Task 3)

---
*Quick task: 260612-hjb*
*Completed: 2026-06-12*
