---
phase: 01-parser-safety-net
plan: 02
subsystem: SwtorLogParser.Tests
tags: [tests, characterization, parser-safety, lazy-parse, locale, theory]
requires:
  - "SwtorLogParser.Model.Actor / Threat / Value Parse factories + property getters"
provides:
  - "Actor LAZY Health/Id throw characterization (Assert.Throws<FormatException> on access)"
  - "Actor delimiter-in-name [Theory] (no IndexOutOfRange escape from Parse/Name)"
  - "Actor position InvariantCulture (f,f,f,f) tuple golden (BUG-03 lock)"
  - "Threat guard-null [Theory] + .Value LAZY throw characterization (BUG-05)"
  - "Value guard-null [Theory] + .Id LAZY throw characterization (BUG-05)"
affects:
  - "Phase 2 BUG-05 (TryParse) — these Assert.Throws tests will be inverted to Assert.Null/graceful"
  - "Phase 2 BUG-03 (InvariantCulture) — position tuple golden locks the already-correct behavior"
tech-stack:
  added:
    - "xUnit [Theory]/[InlineData] (first use in repo)"
    - "Assert.Throws<FormatException> characterization (first use in repo)"
  patterns:
    - "Pattern B: [Theory] with string in, .AsMemory() inside body"
    - "Pattern C: guard-null matrix (Assert.Null where guards are graceful today)"
    - "Pattern E: LAZY throw characterized ON THE PROPERTY (not on Parse)"
    - "Pattern F: tuple position equality (locale lock)"
key-files:
  created: []
  modified:
    - "SwtorLogParser.Tests/ActorTests.cs"
    - "SwtorLogParser.Tests/ThreatTests.cs"
    - "SwtorLogParser.Tests/ValueTests.cs"
decisions:
  - "All malformed-numeric inputs characterized via Assert.Throws on property access (zero skips), never Assert.Null(Parse(...)) — avoids the lazy-null trap (RESEARCH Pitfall 1)"
  - "Distinct string literals per test to keep inputs unambiguous (Actor/Threat/Value do not cache, but convention preserved)"
metrics:
  duration: 6min
  completed: 2026-06-11
  tasks: 2
  files: 3
  tests_added: 14
  test_results: "62 passed / 0 failed / 0 skipped (exit 0)"
---

# Phase 01 Plan 02: Actor / Threat / Value LAZY-Parse Characterization Summary

Characterized the three LAZY-throwing parse factories (Actor, Threat, Value) by asserting the
`FormatException` on the property getter — not on `Parse` — plus guard-null `[Theory]` matrices,
an Actor delimiter-in-name `[Theory]`, and an InvariantCulture position-tuple golden. Test-only;
no production code touched. Full suite GREEN at 62 passed, 0 skipped.

## What Was Built

### Task 1 — ActorTests.cs (commit eaf42c2)
- `Actor_Name_With_Delimiters_Does_Not_Throw_From_Parse` `[Theory]` (4 rows: `[ ]`, `{ }`, `@`, `:`) —
  proves no `IndexOutOfRangeException` escapes from `Parse` or `Name` (GetName try/catch, Actor.cs:43-60). TEST-03 criterion 3.
- `Actor_Position_Comma_Is_Field_Separator_Invariant` `[Fact]` — locks the `(4641.05f, 4529.71f, 694.02f, -124.45f)`
  tuple; ExtractPosition uses `CultureInfo.InvariantCulture` (Actor.cs:147) so `,` separates fields and `.` is the decimal. TEST-03 criterion 2.
- `Actor_NonNumeric_Health_Throws_On_Access_Today` `[Fact]` — `Parse` succeeds (LAZY), `int.Parse("x")` throws on `.Health` (Actor.cs:64). BUG-05.
- `Actor_NonNumeric_Id_Throws_On_Access_Today` `[Fact]` — NPC line with `{abc}` id, `long.Parse("abc")` throws on `.Id` (Actor.cs:107). BUG-05.

### Task 2 — ThreatTests.cs + ValueTests.cs (commit b4189eb)
- `Threat_Parse_Rejects_Cleanly` `[Theory]` (`"<>"`, `""`, `"<vfoo>"`) — length/empty/'v'-prefix guards return null today (Threat.cs:23-34).
- `Threat_NonNumeric_Value_Throws_On_Access_Today` `[Fact]` — scope `"abc"` passes the non-'v' guard, `int.Parse` throws on `.Value` (Threat.cs:14). BUG-05.
- `Value_Parse_Rejects_Cleanly` `[Theory]` (`"(he)"`, `"no parens"`) — HeroEngine prefix / missing parens return null today (Value.cs:64-70).
- `Value_NonNumeric_Id_Throws_On_Access_Today` `[Fact]` — `(123 {abc})` parses, `ulong.Parse("abc")` throws on `.Id` (Value.cs:47). BUG-05.

## Behavior Confirmation (observed == predicted)

All four LAZY-throw literals produced exactly the plan's predicted `FormatException` on property access:
- `Actor.Health` (int.Parse), `Actor.Id` (long.Parse), `Threat.Value` (int.Parse), `Value.Id` (ulong.Parse).

No literal required falling back to a different observed behavior. The Actor health literal `"@Name#123|(0,0,0,0)|(x/y)"`
yields exactly 3 pipe sections so `GetHealth`'s `Roms.Count == 3` precondition is met and the throw fires as characterized.

## Deviations from Plan

None — plan executed exactly as written. The optional parallel `Actor.Id` characterization
mentioned in Task 1 was authored (clean NPC `{abc}` literal) and throws as predicted.

## Verification

```
dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj
Passed!  - Failed: 0, Passed: 62, Skipped: 0, Total: 62  (exit 0)
```

- Full suite GREEN, zero skips (48 baseline from 01-01 + 14 new this plan).
- `git diff --stat HEAD~2 HEAD`: only ActorTests.cs, ThreatTests.cs, ValueTests.cs changed.
- No `SwtorLogParser/Model/*.cs` production file modified (verified via `git diff --name-only`).
- `Player_Is_Local_Is_True` untouched.

## Notes for Phase 2

Each `Assert.Throws<FormatException>` here is the exact test BUG-05 (TryParse) will invert to
`Assert.Null` / a graceful value once `int.Parse`/`long.Parse`/`ulong.Parse` become `TryParse` in
Actor/Threat/Value getters. The position tuple golden locks BUG-03's already-correct InvariantCulture site.

## Self-Check: PASSED

- Files: ActorTests.cs, ThreatTests.cs, ValueTests.cs, 01-02-SUMMARY.md — all FOUND.
- Commits: eaf42c2 (Task 1), b4189eb (Task 2) — all FOUND.

