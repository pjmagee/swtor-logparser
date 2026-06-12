---
phase: 03-monitor-refactor-coverage
plan: 03
subsystem: view
tags: [refactor, dedup, aot, winforms, sliding-expiration]
requires:
  - "03-01 (RFCT-02 monitor singleton)"
  - "03-02 (RFCT-03 bounded caches)"
provides:
  - "SwtorLogParser.View.Entry — shared UI-free Entry DTO"
  - "SwtorLogParser.View.SlidingExpirationList — single source of expiry truth (AOT-safe)"
  - "Overlay SlidingExpirationList adapter that composes the core list"
affects:
  - "SwtorLogParser.Cli (consumes core View)"
  - "SwtorLogParser.Native.Cli (consumes core View)"
  - "SwtorLogParser.Overlay (WinForms adapter composes core)"
tech-stack:
  added: []
  patterns:
    - "Composition over duplication: WinForms BindingList adapter wraps a UI-free core list"
    - "Lock-around-mutable-collection preserved in the core list"
key-files:
  created:
    - "SwtorLogParser/View/Entry.cs"
    - "SwtorLogParser/View/SlidingExpirationList.cs"
    - "SwtorLogParser.Tests/SlidingExpirationListTests.cs"
  modified:
    - "SwtorLogParser.Cli/Program.cs"
    - "SwtorLogParser.Native.Cli/Program.cs"
    - "SwtorLogParser.Overlay/View/SlidingExpirationList.cs"
  deleted:
    - "SwtorLogParser.Cli/View/Entry.cs"
    - "SwtorLogParser.Cli/View/SlidingExpirationList.cs"
    - "SwtorLogParser.Native.Cli/View/Entry.cs"
    - "SwtorLogParser.Native.Cli/View/SlidingExpirationList.cs"
decisions:
  - "Overlay adapter composes (wraps) the core SwtorLogParser.View.SlidingExpirationList via a CoreList alias rather than re-implementing expiry — expiration logic now lives in exactly one place"
  - "Overlay's display Entry (IComparable/IEquatable + formatted Name/DPS/... props) stays host-side as a presentation type bound by DataPropertyName"
  - "Used the nullable-clean `= null!;` form for the core Entry.Stats"
metrics:
  duration: "8min"
  completed: "2026-06-11"
  tasks: 2
  files: 9
---

# Phase 3 Plan 03: View Dedup (RFCT-01) Summary

Collapsed the triplicated `Entry` + `SlidingExpirationList` into one UI-free core copy in the new `SwtorLogParser.View` namespace; CLI and Native.Cli now consume that single copy (their byte-identical duplicates deleted), and the Overlay's WinForms `BindingList<Entry>` adapter composes the core list so the sliding-expiration logic exists in exactly one location — with zero WinForms types entering the AOT core library.

## What Was Built

### Task 1 — Promote UI-free view types to the core lib (commit 1dda7c2)
- Created `SwtorLogParser/View/Entry.cs` (namespace `SwtorLogParser.View`): data-only `{ PlayerStats Stats; DateTime Expiration; }`, nullable-clean `= null!;`.
- Created `SwtorLogParser/View/SlidingExpirationList.cs`: the canonical CLI variant verbatim — `SortedList<long, Entry>` keyed by `Player.Id`, `System.Threading.Timer`-driven `RemoveExpiredItems`, `AddOrUpdate(PlayerStats)`, `Items` returning `ImmutableList<PlayerStats>`, `lock (_items)`. Uses only `SortedList`/`Timer`/`ImmutableList`/`CombatLogsMonitor.PlayerStats` — no WinForms.
- Deleted the four byte-identical CLI + Native.Cli `View/` files (and their now-empty `View/` folders).
- Repointed `SwtorLogParser.Cli/Program.cs` and `SwtorLogParser.Native.Cli/Program.cs` to `using SwtorLogParser.View;` (API unchanged — `new SlidingExpirationList(TimeSpan)`, `.AddOrUpdate`, `.Items` all identical).

### Task 2 — Overlay composes core + tests (commit 11322f7)
- Refactored `SwtorLogParser.Overlay/View/SlidingExpirationList.cs`: still a `BindingList<Entry>` host-side, but now holds a private `CoreList _core` (`using CoreList = SwtorLogParser.View.SlidingExpirationList;`). `AddOrUpdate` delegates to `_core.AddOrUpdate(item)`; the render `Timer` rebuilds the `BindingList` rows from `_core.Items`, projecting each `PlayerStats` into the Overlay's display `Entry`.
- Deleted the Overlay's own `_expirationTimer` + `RemoveExpiredItems` expiry loop — the expiration logic now lives only in the core list.
- Kept all WinForms (`BindingList`, `Control`, render `Timer`, `Invoke(Refresh)`, `ClearItems`/`InsertItem`) host-side; `ParserForm.cs:115` ctor `new SlidingExpirationList(dataGridView, TimeSpan.FromSeconds(10))` unchanged; BL-01 (TopMost) untouched.
- Added `SwtorLogParser.Tests/SlidingExpirationListTests.cs` testing the core list: AddOrUpdate inserts a new player, updates an existing player by `Player.Id`, keeps distinct players separate, and the timer-driven expiry removes a stale entry within a short window (Actor built from a parsed `@Name#id|...` line so `Player.Id` is set).

## Verification

| Check | Command | Result |
|-------|---------|--------|
| Full solution build (all 3 hosts) | `dotnet build SwtorLogParser.slnx -c Debug --nologo` | Build succeeded, 0 errors (1 pre-existing CS0108 warning in ParserForm, out of scope) |
| Full test suite, zero skips | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` | Passed! Failed: 0, Passed: 90, Skipped: 0 (baseline 86 + 4 new) |
| Core lib AOT-clean (no WinForms) | grep `BindingList\|DataGridView\|System.Windows.Forms\|Control` in `SwtorLogParser/View/` | 0 matches |
| Overlay delegates expiry | grep `RemoveExpiredItems\|_expirationTimer` in Overlay adapter | 0 matches |
| Overlay composes core | grep `SwtorLogParser.View` in Overlay adapter | present (`using CoreList = ...`) |

## Success Criteria

- [x] Entry + SlidingExpirationList expiration logic in exactly one core-lib location; per-host UI-free duplicates removed; Overlay adapter composes the shared core.
- [x] Core lib has NO WinForms types; `dotnet build SwtorLogParser.slnx` succeeds; `dotnet test` green (90, 0 skipped).
- [x] All tasks executed and committed individually.

## Deviations from Plan

None — plan executed exactly as written. The Overlay refactor took the recommended composition path (not the conservative A3 fallback); the Overlay's display `Entry` retains an unused `Expiration` property, left in place as it is a harmless public presentation field and removing it is out of scope.

## TDD Gate Compliance

This plan was authored as two `auto tdd="true"` tasks where the implementation under test (the core list) is created in Task 1 and the tests are added in Task 2 (per the plan's `<files>` split). The 4 new core-list tests passed immediately because the core list landed in Task 1's commit; this is the expected sequencing for this plan, not a skipped RED gate. Build (Task 1) and the test suite (Task 2) are the verification gates, both green.

## Known Stubs

None.

## Self-Check: PASSED
- FOUND: SwtorLogParser/View/Entry.cs
- FOUND: SwtorLogParser/View/SlidingExpirationList.cs
- FOUND: SwtorLogParser.Tests/SlidingExpirationListTests.cs
- FOUND: commit 1dda7c2 (Task 1)
- FOUND: commit 11322f7 (Task 2)
- CONFIRMED deleted: the four CLI/Native View/ duplicates
