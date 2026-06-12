# Phase 1: Parser Safety Net - Pattern Map

**Mapped:** 2026-06-11
**Files analyzed:** 7 (all extend existing per-model `*Tests.cs`)
**Analogs found:** 7 / 7 (every target file is its own closest analog — extend in place)

## Overview

This phase is **test-only**. No `Model/*.cs` production file is touched. Every new test
extends an existing `SwtorLogParser.Tests/{Type}Tests.cs` file. The closest analog for each
new test is the *existing `[Fact]` set in the same file* — copy its parse-then-assert shape,
its `.AsMemory()` input convention, and its `Snake_Case` naming. `[Theory]`/`[InlineData]`
is new to the project (introduced this phase); use the canonical xUnit shape shown below.

**Parse factory signature (all 7 models, verified by source read):**
```csharp
public static {Type}? Parse(ReadOnlyMemory<char> rom)
```
- `Value.cs:58`, `Threat.cs:21`, `GameObject.cs:101`, `CombatLogLine.cs:38`,
  `Actor.cs:113`, `Action.cs:45`, `Ability.cs:10` (`public static new Ability? Parse`).
- Inputs are always built as `"literal".AsMemory()` **inside the test body** —
  `ReadOnlyMemory<char>` is not a compile-time constant and cannot appear in `[InlineData]`.

## File Classification

| Target test file | Role | Data flow under test | Closest analog | Match Quality |
|------------------|------|----------------------|----------------|---------------|
| `SwtorLogParser.Tests/AbilityTests.cs` | golden + lazy-throw matrix | transform (string→model), LAZY id | self (`AbilityTests.cs:7-16`) | exact |
| `SwtorLogParser.Tests/ActionTests.cs` | golden + graceful-null matrix | transform, guarded try/catch | self (`ActionTests.cs:7-20`) | exact |
| `SwtorLogParser.Tests/ActorTests.cs` | golden + delimiter `[Theory]` + locale + lazy-throw | transform, LAZY health/id, guarded name | self (`ActorTests.cs:8-23`, `39-54`) | exact |
| `SwtorLogParser.Tests/CombatLogLineTests.cs` | golden lines + timestamp locale characterization | request-response (full line), EAGER ctor | self (`CombatLogLineTests.cs:60-91`) | exact |
| `SwtorLogParser.Tests/GameObjectTests.cs` | golden + EAGER-throw + brace/delimiter `[Theory]` | transform, EAGER id | self (`GameObjectTests.cs:7-18`, `30-38`) | exact |
| `SwtorLogParser.Tests/ThreatTests.cs` | golden + guard-null `[Theory]` + lazy-throw | transform, LAZY value | self (`ThreatTests.cs:7-49`) | exact |
| `SwtorLogParser.Tests/ValueTests.cs` | golden + guard-null `[Theory]` + lazy-Id throw + locale | transform, guarded Parse, LAZY id | self (`ValueTests.cs:7-13`, `135-140`) | exact |

**Do NOT touch (Phase 3, filesystem-backed):**
- `CombatLogLineTests.All_Logs_Are_Not_Null` (`CombatLogLineTests.cs:8-44`)
- `ActorTests.Player_Is_Local_Is_True` (`ActorTests.cs:25-37`) — uses `CombatLogs.PlayerNames`.

## Shared Patterns

### Pattern A — Existing `[Fact]` parse-then-assert (copy this shape)
**Source:** `SwtorLogParser.Tests/AbilityTests.cs:7-16`
**Apply to:** every new golden-line `[Fact]`
```csharp
[Fact]
public void Ability_With_Name_And_Id_Parsed()
{
    var ability = Ability.Parse("Overlord's Command Throne {3039943492370432}".AsMemory());

    Assert.NotNull(ability);
    Assert.False(ability.IsNested);
    Assert.Equal("Overlord's Command Throne", ability.Name);
    Assert.Equal(3039943492370432u, ability.Id);   // note unsigned literal suffix `u`
}
```

### Pattern B — `[Theory]`/`[InlineData]` (NEW — string in, `.AsMemory()` inside)
**Source:** none in repo yet; canonical xUnit shape (RESEARCH.md Pattern 1).
**Apply to:** delimiter-in-name matrices, brace edge cases, guard-null matrices, locale rows.
```csharp
[Theory]
[InlineData("@Name[bracket]#1|(0,0,0,0)|(1/2)")]
[InlineData("@Name{brace}#1|(0,0,0,0)|(1/2)")]
[InlineData("@Name@at#1|(0,0,0,0)|(1/2)")]
public void Actor_Name_With_Delimiters_Does_Not_Throw_From_Parse(string raw)
{
    var a = Actor.Parse(raw.AsMemory());   // .AsMemory() inside the body, never in [InlineData]
    Assert.NotNull(a);
    _ = a.Name;                            // guarded getter → string or null, never throws
}
```

### Pattern C — Guard-null assertion (already green today)
**Source:** `SwtorLogParser.Tests/ThreatTests.cs:37-49`, `ValueTests.cs:135-140`
**Apply to:** `Threat.Parse` / `Value.Parse` clean-rejection matrices.
```csharp
[Fact]
public void No_Threat_Is_Null()
{
    var threat = Threat.Parse("".AsMemory());
    Assert.Null(threat);
}
```
Convert to a `[Theory]` matrix (`"<>"`, `""`, `"<vfoo>"` for Threat; `"(he)"`, `"no parens"` for Value).

### Pattern D — Characterize EAGER throw (`Assert.Throws`)
**Source:** no repo analog; RESEARCH.md Pattern 2. Closest existing null-guard analog: `GameObjectTests.cs:30-38`.
**Apply to:** `GameObject.Parse("Name {abc}")` (EAGER `ulong.Parse`, `GameObject.cs` id read in `Parse`),
`CombatLogLine` with a non-parseable timestamp (EAGER ctor `DateTime.Parse`).
```csharp
[Fact]
public void GameObject_NonNumeric_Id_Throws_Today() // BUG-05: Phase 2 flips to Assert.Null
{
    Assert.Throws<FormatException>(() => GameObject.Parse("Name {abc}".AsMemory()));
}
```

### Pattern E — Characterize LAZY throw (throw on property access)
**Source:** no repo analog; RESEARCH.md Pattern 3. Closest analog: `ThreatTests.cs:7-15` (reads `.Value`).
**Apply to:** `Threat.Value`, `Actor.Health/MaxHealth/Id`, `Value.Id`, `Ability.Id` with malformed fields.
```csharp
[Fact]
public void Threat_NonNumeric_Value_Throws_On_Access_Today() // BUG-05: Phase 2 → graceful
{
    var threat = Threat.Parse("<abc>".AsMemory());
    Assert.NotNull(threat);                                  // Parse is lazy — succeeds today
    Assert.Throws<FormatException>(() => _ = threat.Value);  // int.Parse on access (Threat.cs:14)
}
```

### Pattern F — Tuple position equality (locale lock)
**Source:** `SwtorLogParser.Tests/ActorTests.cs:22`
**Apply to:** Actor position invariant-culture characterization.
```csharp
Assert.Equal((4641.05f, 4529.71f, 694.02f, -124.45f), actor.Position);
```

## Pattern Assignments (per file)

### `AbilityTests.cs` (golden + lazy-Id)
**Analog:** self, `AbilityTests.cs:7-27`.
- Golden `[Fact]`: copy `Ability_With_Name_And_Id_Parsed` shape (Pattern A) — already present; add one more if needed.
- Lazy-Id throw: Pattern E around `.Id` with `Ability.Parse("Name {abc}")` (Parse does not read `.Id` → non-null, throws on access).
- Reuse `Assert.Equal(<id>u, ability.Id)` unsigned-literal convention from line 15.

### `ActionTests.cs` (golden + graceful-null)
**Analog:** self, `ActionTests.cs:7-50`.
- Golden `[Fact]`: copy `Event_And_Effect_Parsed` (`:7-20`) and nested `:22-41`.
- Graceful-null: `Action.Parse` wraps construction in try/catch → `Assert.Null` works TODAY for a `{abc}` child (Pattern C). Use a `[Theory]` of malformed inner fragments.
- Reuse `using Action = SwtorLogParser.Model.Action;` alias from line 1 (Action collides with `System.Action`).

### `ActorTests.cs` (golden + delimiter `[Theory]` + locale + lazy-throw)
**Analog:** self, `ActorTests.cs:8-23` (player), `39-54` (npc), `56-72` (companion).
- Golden `[Fact]`s already exist for player/npc/companion — extend with a dedicated all-fields golden if desired (Pattern A + F).
- Delimiter-in-name `[Theory]`: Pattern B — `Actor.GetName` try/catch makes these green today.
- Locale: Pattern F — position uses `InvariantCulture` (`Actor.cs:147`); comma is field separator. Assert `(f,f,f,f)` tuple.
- Lazy throw: Pattern E around `.Health` / `.MaxHealth` / `.Id` with malformed numeric fields.
- **Leave `Player_Is_Local_Is_True` (`:25-37`) untouched** (Phase 3).

### `CombatLogLineTests.cs` (golden lines + timestamp locale)
**Analog:** self, `CombatLogLineTests.cs:60-91` (valid-sections goldens), `46-58` (null guards).
- Golden lines: copy `Line_With_Valid_Sections_Is_Parsed` (`:60-74`) and the threat variant (`:76-91`). Time-only stamps (`[18:12:13]`, `[20:33:17.759]`) are culture-robust.
- Null guards: copy `Empty_Line_Is_Null` (`:46-51`), `Line_With_No_Timestamp_Is_Null` (`:53-58`).
- Timestamp locale: Pattern D — EAGER `DateTime.Parse` in ctor; characterize current throw on a non-parseable/ambiguous stamp (run once, assert observed; annotate BUG-03).
- **Leave `All_Logs_Are_Not_Null` (`:8-44`) untouched** (Phase 3).

### `GameObjectTests.cs` (golden + EAGER-throw + brace/delimiter `[Theory]`)
**Analog:** self, `GameObjectTests.cs:7-18` (golden), `30-38` (missing-brace null), `40-50` (nested).
- Golden `[Fact]`: copy `Name_And_Id_Parsed` (`:7-18`); note interpolation `$"{name} {{{id}}}"` to emit literal braces.
- Missing/one-brace null: copy `Invalid_Id_Not_Parsed` (`:30-38`) — `Assert.Null` (guarded by `IndexOf != -1`).
- EAGER non-numeric id: Pattern D — `GameObject.Parse("Name {abc}")` → `Assert.Throws<FormatException>` (id read eagerly in `Parse`).
- Delimiter-in-name `[Theory]`: Pattern B — `[InlineData("Name [bracket] {836045448945477}", "Name [bracket]")]` etc., assert `Name` slice.

### `ThreatTests.cs` (golden + guard-null `[Theory]` + lazy-throw)
**Analog:** self, `ThreatTests.cs:7-15` (golden, reads `.Value`/`.IsPositive`), `37-49` (null guards).
- Golden `[Fact]`s present (`Zero_Is_Positive`, `Positive/Negative`).
- Guard-null `[Theory]`: Pattern C — rows `"<>"`, `""`, `"<vfoo>"` → `Assert.Null(Threat.Parse(raw.AsMemory()))` (green today).
- Lazy throw: Pattern E — `Threat.Parse("<abc>")` non-null, `Assert.Throws` on `.Value`.

### `ValueTests.cs` (golden + guard-null `[Theory]` + lazy-Id + locale)
**Analog:** self, `ValueTests.cs:7-13` (golden), `135-140` (HeroEngine null guard).
- Golden `[Fact]`s present (zero, miss, absorbed, critical, parry, dodge, etc.) — strong existing locks.
- Guard-null `[Theory]`: Pattern C — `"(he)"`, `"no parens"` → `Assert.Null`.
- Lazy `.Id` throw: Pattern E — `Value.Parse` non-null, `Assert.Throws` on `.Id` with bad brace content (`ulong.Parse`, `Value.cs:47`).
- Integer/Tilde/Total are safe (manual char loops) → `Assert.Equal`/`Assert.Null` work today.

## No Analog Found

None. Every target file has an in-file `[Fact]` analog. The only *new construct* is
`[Theory]`/`[InlineData]` (no prior repo usage) and `Assert.Throws` characterization
(no prior repo usage) — both covered by Patterns B, D, E above with canonical xUnit shapes.

## Anti-Patterns (from RESEARCH.md — enforce in plans)

- Do NOT `Assert.Null(Threat.Parse("<abc>"))` / `Assert.Null(Actor.Parse(garbage))` — these `Parse` are LAZY and return non-null. Use Pattern E.
- Do NOT put `ReadOnlyMemory<char>` in `[InlineData]` — pass `string`, call `.AsMemory()` in the body.
- Do NOT touch `All_Logs_Are_Not_Null` / `Player_Is_Local_Is_True` — Phase 3.
- Do NOT add any `Model/*.cs` change to make a test green — Phase 2.
- Use a **distinct literal per test** — `GameObject`/`Action`/`Ability` `Parse` cache on `Rom.GetHashCode()`; shared-literal reuse can serve a cached object and skip the parse path.

## Metadata

**Analog search scope:** `SwtorLogParser.Tests/` (all 7 `*Tests.cs` + `GlobalUsings.cs`), `SwtorLogParser/Model/` (Parse signatures).
**Files scanned:** 8 test files + 7 model Parse sites.
**Global usings:** `global using Xunit;` (`GlobalUsings.cs`) — no per-file `using Xunit;` needed; `using SwtorLogParser.Model;` is per-file.
**Pattern extraction date:** 2026-06-11
