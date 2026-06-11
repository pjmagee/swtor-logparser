---
phase: 02-correctness-bugs
plan: 01
subsystem: parser-model
tags: [bug-fix, input-validation, tryparse, BUG-05]
requires: []
provides:
  - "Threat.Value (int?) graceful on non-numeric scope"
  - "Actor.Health/MaxHealth/Id graceful on non-numeric content"
  - "Value.Id graceful on non-numeric brace content"
affects:
  - SwtorLogParser/Model/Threat.cs
  - SwtorLogParser/Model/Actor.cs
  - SwtorLogParser/Model/Value.cs
tech_stack:
  added: []
  patterns:
    - "BCL TryParse(span, out var v) ? v : (T?)null ternary in lazy getters (mirrors existing float.TryParse at Actor.cs)"
    - "C# relational pattern on nullable (Value is >= 0) for null-safe IsPositive/IsNegative"
key_files:
  created: []
  modified:
    - SwtorLogParser/Model/Threat.cs
    - SwtorLogParser/Model/Actor.cs
    - SwtorLogParser/Model/Value.cs
    - SwtorLogParser.Tests/ThreatTests.cs
    - SwtorLogParser.Tests/ActorTests.cs
    - SwtorLogParser.Tests/ValueTests.cs
decisions:
  - "Threat.Value flip target is int? (not a sentinel 0) per 02-RESEARCH — avoids conflating a real zero threat with a parse failure"
  - "Integer/long/ulong TryParse sites take no culture argument (culture-invariant inputs) matching the repo analog; only float parsing needs InvariantCulture"
metrics:
  duration: 12min
  completed: 2026-06-11
requirements: [BUG-05]
---

# Phase 2 Plan 01: Harden Threat/Actor/Value Lazy Numeric Getters Summary

Replaced unguarded `int`/`long`/`ulong.Parse` in the lazy numeric getters of `Threat`, `Actor`, and `Value` with BCL `TryParse` returning `null` on malformed input (BUG-05), so a non-numeric log token is skipped rather than crashing the background reader task — landing each production edit in the same commit as the inversion of its Phase-1 characterization test, suite green every commit.

## What Was Built

- **Actor.cs (5 sites):** `GetHealth`, `GetMaxHealth`, and the three `GetId` branches (Companion/Player/Npc) now use `int.TryParse`/`long.TryParse` → `null` on bad input. `GetHealth` additionally guards the `IndexOf('/') == -1` case (a missing slash would have produced a negative slice length).
- **Value.cs (1 site):** `Id` getter uses `ulong.TryParse` for the `{...}` brace content; the surrounding `if (start != -1 && end != -1)` guard is retained.
- **Threat.cs:** `Value` converted from `int` to `int?` via `int.TryParse`; `IsPositive`/`IsNegative` rewritten as null-aware relational patterns (`Value is >= 0` / `Value is < 0`), so a null Value reads as neither positive nor negative.
- **Tests:** 4 Phase-1 characterization tests flipped from `Assert.Throws<FormatException>` to `Assert.Null` (Actor Health + Id, Value Id, Threat Value), comments updated to "Phase 2: now graceful (BUG-05)". All positive goldens and guard matrices retained unchanged.

## Task Commits

| Task | Description | Commit |
| ---- | ----------- | ------ |
| 1 | Guard Actor + Value lazy getters with TryParse, flip 3 tests | `6a9fbfc` |
| 2 | Convert Threat.Value to int? with TryParse, null-harden IsPositive/IsNegative, flip 1 test | `fd28091` |
| 3 | Full-suite green gate (verification only, no code change) | (no commit) |

## Verification

- Task 1 filter (`ActorTests`/`ValueTests`): 28 passed, 0 failed, 0 skipped.
- Task 2 filter (`ThreatTests`): 9 passed, 0 failed, 0 skipped.
- Task 3 full suite (`dotnet test SwtorLogParser.Tests`): **66 passed, 0 failed, 0 skipped**, exit 0 — confirmed green on two consecutive runs.
- Grep confirms no bare `int.Parse`/`long.Parse`/`ulong.Parse` remains in the edited getters; the only `*.Parse` left are the static `Parse(...)` factories and the pre-existing `float.TryParse`.

## Deviations from Plan

### Auto-fixed Issues

None affecting production logic beyond what the plan specified. The plan's GetHealth action explicitly called for guarding the `IndexOf('/') == -1` slice case; implemented as written (Rule 2 / per-plan instruction).

## Flaky Test Note (not a regression)

During the first Task-3 full-suite run, `CombatLogLineTests.All_Logs_Are_Not_Null` threw a `FormatException` from `CombatLogLine` constructor's eager `DateTime.Parse` (CombatLogLine.cs:9) while enumerating the **live** SWTOR `CombatLogs` folder. This test is filesystem-gated (reads real, possibly concurrently-written log files) and is slated for Phase 3 (BUG-03 / TEST-03). It passed in isolation and the full suite was green on the two subsequent runs.

This is NOT a regression from this plan:
- The failure originates in `CombatLogLine.cs` (eager `DateTime.Parse`), a file outside this plan's `files_modified` scope.
- This plan's edits only relax behavior (throw → null) in Threat/Actor/Value getters, which cannot introduce a new `FormatException`.

Per SCOPE BOUNDARY, the pre-existing eager `DateTime.Parse` in CombatLogLine was left untouched (BUG-03, Phase 3 territory). No action taken here.

## Known Stubs

None. All getters wired to real TryParse logic; no placeholder/empty-value patterns introduced.

## Self-Check: PASSED

- Modified files exist: Threat.cs, Actor.cs, Value.cs, ThreatTests.cs, ActorTests.cs, ValueTests.cs — all present.
- Commits exist: `6a9fbfc`, `fd28091` — both in `git log`.
- Suite green, zero skips (66/66).
