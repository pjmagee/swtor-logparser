---
phase: 04-performance
plan: 02
subsystem: native-cli-render
tags: [performance, console, render, PERF-02, aot]
requires:
  - SwtorLogParser.Native.Cli host (monitor command)
  - CombatLogsMonitor.PlayerStats / SlidingExpirationList.Items
provides:
  - Flicker-free in-place cursor render for the Native CLI monitor command
affects:
  - SwtorLogParser.Native.Cli/Program.cs (Update render path only)
tech-stack:
  added: []
  patterns:
    - "In-place console row overwrite (SetCursorPosition + pad-to-width, no Console.Clear)"
    - "Cursor-home vacated-row clearing via host-local _lastRowCount render state"
    - "IsOutputRedirected guard with plain-WriteLine fallback"
key-files:
  created: []
  modified:
    - SwtorLogParser.Native.Cli/Program.cs
decisions:
  - "Console.Clear() removed; stats rows overwritten in place starting at row 1 (row 0 stays the OnCombatLogAdded filename header)"
  - "Extracted FormatRow(PlayerStats) helper so the interactive and redirected paths share the byte-identical format string"
  - "Clamp width = Max(1, WindowWidth-1) and bottom row to WindowHeight-1 to avoid SetCursorPosition out-of-range on tiny/odd windows"
metrics:
  duration: 7min
  tasks: 1
  files: 1
  completed: 2026-06-12
---

# Phase 4 Plan 02: PERF-02 Flicker-Free Native CLI Render Summary

Replaced the `Console.Clear()` + full redraw per stats event in the Native AOT CLI `monitor` command with in-place cursor repositioning (`SetCursorPosition` + pad-to-width overwrite), eliminating the full-screen repaint flicker while keeping the rendered text byte-for-byte identical.

## What Was Built

`SwtorLogParser.Native.Cli/Program.cs` `Update(SlidingExpirationList, PlayerStats)` was refactored:

- `list.AddOrUpdate(playerStats)` stays the first line (unchanged data flow).
- `Console.Clear()` removed entirely — this was the per-event flicker source and the whole point of PERF-02.
- Added a `private static int _lastRowCount;` host-local render-state field.
- **Redirected-output guard:** when `Console.IsOutputRedirected`, the method writes each row via plain `Console.WriteLine` (no cursor calls) and returns without touching cursor state or `_lastRowCount` — `SetCursorPosition`/`WindowWidth`/`WindowHeight` throw or misbehave when stdout has no console buffer (04-RESEARCH Pitfall 5 / threat T-04-04).
- **Interactive path:** computes `width = Math.Max(1, Console.WindowWidth - 1)` and `maxRow = Math.Max(1, Console.WindowHeight - 1)`. For each item it repositions to `(0, 1 + row)` — row 0 stays the filename header from `OnCombatLogAdded` — and writes the row text truncated/padded to `width` so a previously longer row is fully overwritten.
- **Vacated-row clearing:** after the loop, rows `[row, _lastRowCount)` are blanked with spaces, then `_lastRowCount = row;`, so a shrinking row count leaves no stale lines.
- **Height clamp:** target rows beyond `maxRow` are skipped (`break`) so `SetCursorPosition` cannot exceed the buffer height on a short window.
- Extracted a `FormatRow(CombatLogsMonitor.PlayerStats)` helper used by both the interactive and redirected paths, holding the **identical** format string `"{0}: DPS: {1} ({2}%); HPS: {3} ({4}%)"` with the same `"N"` formatting and `"-"` for null values.

`OnCombatLogAdded` (row-0 header), `ListCombatLogs`, and `MonitorCombatLogs` wiring are untouched. The managed CLI (`SwtorLogParser.Cli`) is out of scope (Phase 5).

## Verification

| Check | Result |
|-------|--------|
| `dotnet build SwtorLogParser.slnx -c Debug --nologo` | Build succeeded, 0 errors (1 pre-existing unrelated CS0108 warning in Overlay project — out of scope) |
| `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj --nologo` | Passed: 105, Failed: 0, Skipped: 0 (baseline 105, unaffected) |
| Grep guard: no `Console.Clear()` in Native.Cli/Program.cs | Confirmed (no match) |
| `dotnet run --project SwtorLogParser.Native.Cli -- list` smoke | Prints log list as before (untouched path) |
| `dotnet run ... -- monitor > out.txt` (redirected) smoke | Started, no exception on stderr — IsOutputRedirected guard holds |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] FormatRow helper typed to `PlayerStats`, not `Entry`**
- **Found during:** Task 1
- **Issue:** The 04-PATTERNS canonical example named the loop variable `item` and the underlying `SlidingExpirationList` has an `Entry` type, but `SlidingExpirationList.Items` is actually `IReadOnlyList<CombatLogsMonitor.PlayerStats>` (the original code iterated `PlayerStats` members directly: `item.Player.Name`, `item.DPS`, etc.). The extracted helper initially declared `Entry item`, which would not compile.
- **Fix:** Typed the helper as `FormatRow(CombatLogsMonitor.PlayerStats item)` to match the real `Items` element type. No behavior change — same members read, same format.
- **Files modified:** SwtorLogParser.Native.Cli/Program.cs
- **Commit:** 7dc0ca3

## Human-Verify Item (manual flicker check — NOT blocked on)

Console rendering is not unit-testable; per 04-VALIDATION.md there is no automated flicker test. The following manual check is recorded for the orchestrator/human and was NOT blocked on (code change confirmed building and the Native CLI starts):

1. Run `dotnet run --project SwtorLogParser.Native.Cli -- monitor` in an interactive terminal with a live/recent SWTOR combat log present.
2. Confirm stats rows overwrite in place with NO full-screen blink/flicker on each event; row 0 still shows the log file name (header).
3. Optional: `dotnet run --project SwtorLogParser.Native.Cli -- monitor > out.txt` does not throw (IsOutputRedirected fallback) — smoke-tested clean here.
4. Confirm `dotnet run --project SwtorLogParser.Native.Cli -- list` still prints the log list (untouched path) — smoke-tested clean here.

Resume signal for the checkpoint: "approved".

## Known Stubs

None.

## Self-Check: PASSED

- FOUND: SwtorLogParser.Native.Cli/Program.cs (modified, builds)
- FOUND: commit 7dc0ca3 (perf(04-02) ...)
- FOUND: no `Console.Clear()` remains in Native.Cli/Program.cs
- FOUND: contains `SetCursorPosition` (artifact requirement met)
