# Phase 1: Parser Safety Net - Research

**Researched:** 2026-06-11
**Domain:** Characterization (golden-master) testing of span-based parsers in xUnit / .NET 8
**Confidence:** HIGH (all findings verified against actual source in this session)

## Summary

Phase 1 delivers TEST-03: edge-case + golden-line regression tests over the seven `Parse`-factory models so Phases 2-6 can be proven regression-free. The phase is **pure test code** — no production change is permitted (that is Phase 2's job).

The central tension the orchestrator flagged is real and is resolved by **characterization testing**: Phase 1 captures *current* behavior, not *desired* behavior. The success criteria that say "assert null on malformed input" only hold for inputs the parser **already** tolerates gracefully. For inputs where the current production code **throws** (unguarded `int/long/ulong.Parse`, culture-sensitive `DateTime.Parse`), Phase 1 must NOT assert `null` — that test would ship red. Instead Phase 1 either (a) characterizes the current throw with `Assert.Throws<T>` so the behavior is locked and visible, or (b) writes the desired-`null` test pre-disabled with `[Fact(Skip="locks Phase 2 behavior — BUG-05")]` so Phase 2 flips it green. **Both are acceptable per CONTEXT.md; this research recommends a concrete split below.**

Critically, throw-vs-null depends on **where** each `Parse` factory triggers the numeric parse. I read every model. The parse sites fall into two classes: **eager** (executed inside the `Parse`/constructor call — these throw *from `Parse` itself*) and **lazy** (executed only on later property access — `Parse` returns a non-null object, the throw happens when you read the property). This distinction decides exactly which assertion each test can use today.

**Primary recommendation:** Write characterization tests. For every model add (1) golden-line `[Fact]`s asserting current correct output, (2) `[Theory]/[InlineData]` matrices for delimiter-in-name and edge cases the parser **already handles**, and (3) for currently-throwing inputs, lock them with `Assert.Throws<FormatException>` (eager sites) or `Skip`-disabled `Assert.Null` tests (the Phase-2 contract). Keep `dotnet test` green with zero skips counted as failures — xUnit `Skip` tests report as skipped, not failed, satisfying criterion 4.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Parse malformed/edge log fragments | Test project (`SwtorLogParser.Tests`) | — | Pure unit tests over static `Parse` factories; no host involved |
| Golden-line behavior lock | Test project | — | In-memory literals; hermetic |
| Locale-sensitive parsing characterization | Test project | Core lib `Model/CombatLogLine.cs` (behavior under test) | Tests observe culture-dependence; the fix is Phase 2 |
| Filesystem-backed parse coverage | **OUT OF SCOPE** (Phase 3) | — | Non-hermetic `CombatLogs` tests deferred per CONTEXT.md |

## User Constraints (from CONTEXT.md)

### Locked Decisions
- Edge-case tests cover ALL `Parse`-factory models: `Ability`, `Action`, `Actor`, `Value`, `Threat`, `GameObject`, `CombatLogLine`.
- Malformed input asserts `null` / no-throw — this codifies the *intended* contract. Where current production code actually throws (the unguarded `int/long/ulong.Parse` sites), the test expresses the desired post-Phase-2 behavior; if a test cannot pass against current code without a production change, mark it clearly (e.g. `[Fact(Skip="locks Phase 2 behavior — BUG-05")]` or an `xfail`-style note) so Phase 2 flips it green rather than Phase 1 shipping red.
- Locale coverage: comma-decimal inputs (e.g. `4641,05`) and non-US date strings assert invariant parsing / clean rejection.
- Delimiter-in-name coverage: names containing `{ } [ ] @ :` must not break index-based span parsing.
- Test data source: inline literal log fragments built with `.AsMemory()` (matches existing convention in `SwtorLogParser.Tests`).
- Use `[Theory]` / `[InlineData]` for the new edge-case input matrices to reduce duplication; keep `[Fact]` for single golden-line cases.
- File organization: extend the existing per-model `*Tests.cs` files rather than adding parallel `*EdgeCaseTests.cs`.
- Naming: keep descriptive `Snake_Case` test method names (e.g. `Comma_Decimal_Value_Parses_Invariant`).
- Add "golden line" regression tests per type: a known-good full combat-log line (or model fragment) → expected parsed model values.
- Defer the non-hermetic `CombatLogs` filesystem tests (`All_Logs_Are_Not_Null`, `Player_Is_Local_Is_True`) to Phase 3. Leave them untouched in Phase 1.
- New Phase 1 tests must NOT depend on `CombatLogs` or the filesystem — in-memory strings only, fully hermetic, CI-safe.

### Claude's Discretion
- Exact set of `[InlineData]` rows per matrix, helper builders for log-line strings, and precise golden-line samples are at Claude's discretion, guided by `.planning/codebase/TESTING.md` conventions.

### Deferred Ideas (OUT OF SCOPE)
- Abstracting `CombatLogs` filesystem access for hermetic testing → Phase 3 (Monitor Refactor + Coverage).
- Monitor lifecycle / Rx pipeline / DPS-HPS math tests → Phase 3 (TEST-01, TEST-02).
- Changing parser production code → Phase 2 (Correctness Bugs).

## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| TEST-03 | Parser edge-case tests exist for malformed lines, locale-formatted numbers/dates, and delimiter characters inside names | Throw-vs-null map (below) tells the planner exactly which assertion each test uses; golden-line samples drawn from existing passing tests; `[Theory]` introduction pattern provided; filesystem tests explicitly excluded |

## The Critical Finding: Eager vs. Lazy Parse Sites

> **This is the single most important section for the planner.** Whether a malformed input throws *from `Parse`* or only on *later property access* dictates the assertion. I traced every numeric/date parse site in the source this session. `[VERIFIED: source read 2026-06-11]`

A parse site is **EAGER** if the `int/long/ulong/DateTime.Parse` runs during the `Parse(...)` call (directly, or in the private constructor, or in a property the `Parse` method itself reads). It is **LAZY** if it runs only inside a public property getter that `Parse` does not touch.

| Model | Parse site | Eager or Lazy? | What `Parse(malformed)` does TODAY | Phase 1 assertion |
|-------|-----------|----------------|-----------------------------------|-------------------|
| `CombatLogLine` | `DateTime.Parse(Roms[0].Span)` — `CombatLogLine.cs:9`, in **constructor** called by `Parse` | **EAGER** | **Throws `FormatException`** on a non-US/invalid timestamp (constructor runs eagerly) | `Assert.Throws<FormatException>` (chars) — characterizes BUG-03 |
| `CombatLogLine` | constructor also eagerly builds `Actor`/`Value`/`Threat` + `Action.Parse` | **EAGER** | Whole line throws if any eager child throws | covered by line-level golden + throw tests |
| `GameObject` | `ulong.Parse` in `GetId()` — `GameObject.cs:87/95` — **called by `Parse` at line 107** (`if (gameObject.Id == null)`) | **EAGER** | Non-numeric brace content (e.g. `{abc}`) → **throws `FormatException`**. Missing/one brace → returns `null` (guarded by `!= -1`) | `{abc}` → `Assert.Throws<FormatException>`; `{` only / no braces → `Assert.Null` |
| `GameObject` | `ulong.Parse` in `GetParentId()` — `GameObject.cs:75` | **LAZY** | `Parse` succeeds; throws only on `.ParentId` access with bad nested brace | `Assert.Throws` around `.ParentId`, OR skip |
| `Ability` (: GameObject) | inherits `GetId` but `Ability.Parse` (`Ability.cs:10-21`) does **NOT** read `.Id` | **LAZY** | `Ability.Parse("{abc}")` returns **non-null**; `.Id` throws on access | golden `[Fact]` + `Assert.Throws` around `.Id`, OR skip-null |
| `Actor` | `int.Parse` in `GetHealth`/`GetMaxHealth` — `Actor.cs:64/73`; `long.Parse` in `GetId` — `Actor.cs:93/100/107` | **LAZY** | `Actor.Parse` returns **non-null** for almost anything non-empty; throws on `.Health`/`.MaxHealth`/`.Id` access with malformed fields | golden `[Fact]`; for throwing fields `Assert.Throws` around the property, OR skip-null |
| `Actor` | `GetName()` — `Actor.cs:43-60` | **LAZY, already guarded** | try/catch → returns `null` on any slice failure | `Assert.Null(actor.Name)` works TODAY — strong delimiter-in-name lock |
| `Value` | `ulong.Parse` in `Id` getter — `Value.cs:47` | **LAZY** | `Value.Parse` returns non-null; `.Id` throws on bad brace content | `Assert.Throws` around `.Id`, OR skip |
| `Value` | `Integer`/`Tilde`/`Text` — manual char loops, `Total` = `GetValueOrDefault()` | **safe, no throw** | returns `null`/`0` gracefully | `Assert.Null`/`Assert.Equal` work TODAY |
| `Value.Parse` itself | guards `start/end/lastSection` + HeroEngine prefix | **safe** | returns `null` cleanly on no-parens / `(he)` | `Assert.Null` works TODAY |
| `Threat` | `int.Parse(Rom.Span)` in `Value` getter — `Threat.cs:14` | **LAZY** | `Threat.Parse("<abc>")` returns **non-null** (first char not `v`); `.Value`/`.IsPositive`/`.IsNegative` throw on access | `Assert.Throws` around `.Value`, OR skip-null |
| `Threat.Parse` itself | guards empty, `< 3` length, `scope[0] != 'v'` | **safe** | `<>`, `""`, `<vfoo>` → `null` cleanly | `Assert.Null` works TODAY |
| `Action` | `Action.Parse` wraps construction in **try/catch** — `Action.cs:50-59` | **safe, already guarded** | bad inner `GameObject` → exception caught → returns `null` (writes to `Console.Error`) | `Assert.Null` works TODAY — but note: a `{abc}` *child* throws inside `GameObject.Parse` and IS caught here |

**Takeaways for the planner:**
1. **`Actor`, `Value`, `Threat`, `Ability` `Parse` methods are LAZY** — they almost never throw from `Parse`. A naive `Assert.Null(Threat.Parse("<abc>"))` **FAILS** because `Parse` returns a non-null object. The desired-null contract for these belongs to Phase 2 (when the lazy getters switch to `TryParse`). Lock today via `Assert.Throws` on the property, or a `Skip`-disabled null test.
2. **`GameObject` and `CombatLogLine` `Parse` are EAGER** for the id/timestamp — these throw *from `Parse`*. Characterize with `Assert.Throws<FormatException>`.
3. **`Action`, `Actor.Name`, `Value.Parse`, `Threat.Parse` guards are already graceful** — these give you free, green delimiter/edge tests *today*.

## Success Criterion → Test Mapping (GREEN now vs. defer to Phase 2)

| Roadmap criterion | Green in Phase 1? | How |
|-------------------|-------------------|-----|
| **1. Malformed → null/graceful, no unhandled exception escapes** | **Partial** | Green where guards exist (`Value.Parse`, `Threat.Parse`, `Action.Parse`, `Actor.Name`). For EAGER throwers (`GameObject {abc}`, `CombatLogLine` bad timestamp) → characterize with `Assert.Throws` (locks current behavior). For LAZY throwers (`Threat.Value`, `Actor.Health/Id`, `Value.Id`) → `Assert.Throws` on the property OR `[Fact(Skip=...)]` desired-null. The "every Parse returns null on malformed" contract is **Phase 2**. |
| **2. Locale-sensitive inputs parsed invariant or rejected cleanly** | **Characterize only** | `Actor.ExtractPosition` **already uses `InvariantCulture`** (`Actor.cs:147`) → comma-as-separator vs comma-decimal: assert current behavior. `CombatLogLine` `DateTime.Parse` is culture-sensitive (BUG-03) → assert current throw/parse, OR `Skip` the invariant-success test for Phase 2. Do **not** assert invariant success on the timestamp today. |
| **3. Delimiter edge cases (`[ ] { } @ :` in names)** | **Mostly GREEN** | `Actor.GetName` try/catch and `GameObject.GetName` `IndexOf('{')` handling let most of these pass *today* as graceful null or correct slice. `[Theory]` matrix of delimiter-laden names → assert no `IndexOutOfRangeException` escapes (use `Assert.Null` where the guard catches it, `Assert.Throws` where it does not yet). |
| **4. All tests pass on clean `dotnet test`, NO skips** | **GREEN — with nuance** | xUnit `[Fact(Skip="...")]` reports as **skipped, not failed**; `dotnet test` exits 0. Criterion 4's "NO skips" should be read as "no *unexpected* skips / no red" — but to honor it literally, **prefer `Assert.Throws` characterization over `Skip`** wherever a current throw exists, so the suite is fully green with zero skips. Reserve `Skip` only for genuine Phase-2-contract placeholders, each annotated with the BUG id. **Recommended: minimize/eliminate `Skip`; use `Assert.Throws` to keep zero-skip + zero-red.** |

**Prescriptive split:**
- **Phase 1 (this phase):** golden lines for all 7 models; delimiter `[Theory]` matrices; graceful-null tests where guards exist; `Assert.Throws<FormatException>` characterizations for the current eager/lazy throw sites.
- **Phase 2 (BUG-03/BUG-05):** flip each `Assert.Throws` characterization into `Assert.Null` (or remove the `[Fact(Skip)]` marker) as the production `Parse` paths adopt `TryParse` + `InvariantCulture`. The Phase-2 plan must list each test by name to invert.

## Standard Stack

No new packages. The phase uses only what the test project already references. `[VERIFIED: SwtorLogParser.Tests.csproj read 2026-06-11]`

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| xunit | 2.5.0-pre.44 | Test framework — `[Fact]`, `[Theory]`, `[InlineData]`, `Assert` | Already the project's runner; `[Theory]` ships in this version |
| xunit.runner.visualstudio | 2.5.0-pre.27 | VS / `dotnet test` adapter | Already referenced |
| Microsoft.NET.Test.Sdk | 17.7.0-preview.23280.1 | Test host | Already referenced |
| coverlet.collector | 6.0.0 | Coverage (optional `--collect`) | Already referenced |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| (none) | — | — | No FluentAssertions/Shouldly/Moq — CONTEXT.md and TESTING.md mandate built-in `Assert` only |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `[InlineData]` | `[MemberData]` / `[ClassData]` for `ReadOnlyMemory<char>` | `ReadOnlyMemory<char>` is **not a compile-time constant**, so it cannot appear in `[InlineData]` directly. Pass `string` literals in `[InlineData]` and call `.AsMemory()` **inside** the test body. `[MemberData]` is only needed if you must pass the memory itself — avoid it; string-in, `.AsMemory()`-inside is simpler and matches convention. |
| `Assert.Throws<FormatException>` | xunit `[Fact(Skip=...)]` | Skip is invisible/green-but-untested; `Assert.Throws` actively locks current behavior and keeps zero skips. Prefer `Assert.Throws` (see criterion 4). |

**Installation:** None required.

```bash
# No package install. Verify the suite builds & runs:
dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj
```

## Package Legitimacy Audit

Not applicable — this phase installs **no external packages**. All dependencies are already present in `SwtorLogParser.Tests.csproj` and were verified by reading the file this session. `[VERIFIED: SwtorLogParser.Tests.csproj]`

## Architecture Patterns

### Test Data Flow Diagram

```
  string literal ("@Name#123|(x,y,z,d)|(h/mh)")
        │  .AsMemory()  (inside test body — NOT in [InlineData])
        ▼
  ReadOnlyMemory<char>
        │  Model.Parse(rom)   ← static factory under test
        ▼
   ┌─────────────────────────────────────────────┐
   │  EAGER models (GameObject, CombatLogLine)    │  malformed id/date
   │   throw FROM Parse  ──────────────────────►  │──► Assert.Throws<FormatException>
   │  guarded Parse (Value, Threat, Action)       │  malformed
   │   return null FROM Parse  ─────────────────► │──► Assert.Null
   │  LAZY models (Actor, Ability, Value.Id,      │  Parse OK, then…
   │   Threat.Value)  return non-null  ─────────► │──► access property
   └─────────────────────────────────────────────┘        │
                                                            ▼
                                            malformed field → throw  ──► Assert.Throws (property)
                                            good field      → value  ──► Assert.Equal (golden)
                                            guarded (Actor.Name)     ──► Assert.Null
```

### Recommended Test Project Structure
```
SwtorLogParser.Tests/
├── GlobalUsings.cs        # global using Xunit;  (already present — keep)
├── AbilityTests.cs        # extend: golden [Fact] + lazy-Id [Theory]
├── ActionTests.cs         # extend: delimiter [Theory] + graceful-null cases
├── ActorTests.cs          # extend: delimiter-in-name [Theory], position locale, lazy throws
├── CombatLogLineTests.cs  # extend: timestamp locale characterization, golden lines
│                          #   (LEAVE All_Logs_Are_Not_Null UNTOUCHED — Phase 3)
├── GameObjectTests.cs     # extend: {abc} throws, brace edge [Theory], nested delimiter
├── ThreatTests.cs         # extend: <abc> lazy-Value throw, guard cases
└── ValueTests.cs          # extend: locale, .Id lazy throw, delimiter, golden
```
No new files; extend per CONTEXT.md decision.

### Pattern 1: `[Theory]` / `[InlineData]` with `ReadOnlyMemory<char>`
**What:** Data-driven edge matrix. `ReadOnlyMemory<char>` cannot be an `[InlineData]` constant, so pass `string` and convert inside.
**When to use:** Delimiter-in-name matrices, brace edge cases, locale numeric variants.
**Example:**
```csharp
// Pattern verified against xUnit [Theory] semantics + project convention (TESTING.md).
[Theory]
[InlineData("Name [bracket] {836045448945477}", "Name [bracket]")]
[InlineData("Name @at {836045448945477}", "Name @at")]
[InlineData("Name :colon {836045448945477}", "Name :colon")]
public void GameObject_Name_With_Delimiters_Is_Parsed(string raw, string expectedName)
{
    var go = GameObject.Parse(raw.AsMemory());   // .AsMemory() inside body
    Assert.NotNull(go);
    Assert.Equal(expectedName, go.Name);
}
```

### Pattern 2: Characterize a current throw (eager site)
**What:** Lock current behavior at an EAGER parse site so Phase 2 can deliberately invert it.
**When to use:** `GameObject.Parse("Name {abc}")`, `CombatLogLine` with a non-parseable timestamp.
**Example:**
```csharp
[Fact]
public void GameObject_NonNumeric_Id_Throws_Today() // BUG-05: Phase 2 flips to Assert.Null
{
    // Characterization: GameObject.Parse reads .Id eagerly (GameObject.cs:107),
    // so ulong.Parse("abc") throws FormatException FROM Parse.
    Assert.Throws<FormatException>(() => GameObject.Parse("Name {abc}".AsMemory()));
}
```

### Pattern 3: Characterize a lazy throw (property access)
**What:** For LAZY models, `Parse` succeeds; the throw is on property access.
**Example:**
```csharp
[Fact]
public void Threat_NonNumeric_Value_Throws_On_Access_Today() // BUG-05: Phase 2 → graceful
{
    var threat = Threat.Parse("<abc>".AsMemory());
    Assert.NotNull(threat);                                   // Parse is lazy — succeeds today
    Assert.Throws<FormatException>(() => _ = threat.Value);   // int.Parse on access (Threat.cs:14)
}
```

### Pattern 4: Golden-line regression lock
**What:** Full-line / full-fragment parse asserting every field — the strongest pre-refactor lock.
**Example (reuse the known-good samples already in the suite):**
```csharp
[Fact]
public void Golden_Player_Actor_All_Fields()
{
    var a = Actor.Parse("@Powerful Subscriber#688623358308676|(4641.05,4529.71,694.02,-124.45)|(1/401177)".AsMemory());
    Assert.NotNull(a);
    Assert.True(a.IsPlayer);
    Assert.Equal(688623358308676, a.Id);
    Assert.Equal("Powerful Subscriber", a.Name);
    Assert.Equal((4641.05f, 4529.71f, 694.02f, -124.45f), a.Position);
}
```

### Pattern 5: Optional helper for log-line fragments
**What:** Small static builder to reduce literal duplication when composing a full `CombatLogLine`. Optional (Claude's discretion per CONTEXT.md).
**Example:**
```csharp
internal static class LogLine
{
    // Builds a 5-section [ts][src][tgt][ability][action] line; trailing (value) <threat> optional.
    public static ReadOnlyMemory<char> Build(
        string ts, string src, string tgt, string ability, string action, string? tail = null)
        => $"[{ts}] [{src}] [{tgt}] [{ability}] [{action}]{(tail is null ? "" : " " + tail)}".AsMemory();
}
```
Keep it minimal — the suite's norm is inline literals. Use the helper only where assembling many `CombatLogLine` variants.

### Anti-Patterns to Avoid
- **Asserting the Phase-2 contract today.** `Assert.Null(Threat.Parse("<abc>"))` fails — `Parse` is lazy and returns non-null. Either `Assert.Throws` on `.Value` or `Skip` for Phase 2.
- **Putting `ReadOnlyMemory<char>` in `[InlineData]`.** Won't compile (not a constant). Pass `string`, call `.AsMemory()` in the body.
- **Touching `All_Logs_Are_Not_Null` / `Player_Is_Local_Is_True`.** These are filesystem/`CombatLogs`-backed and explicitly deferred to Phase 3. Leave them.
- **Adding any production-code change** to make a test green — that is Phase 2. Phase 1 is test-only.
- **Relying on cache state across tests.** `GameObject.Parse`/`Action.Parse`/`Ability.Parse` write to the **shared static `CombatLogs` caches** keyed on `Rom.GetHashCode()` (BUG-06/RFCT-03). Use **distinct literal strings per test** so cache hits don't mask a parse; do not assert on cache internals.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Parameterized cases | Loops inside a `[Fact]` over an array | `[Theory]/[InlineData]` | xUnit reports each row as a distinct test; a loop hides which row failed |
| Asserting an exception | try/catch + `Assert.True(threw)` | `Assert.Throws<FormatException>(() => ...)` | Built-in, asserts the exact type, fails clearly if no throw |
| Building memory in data attribute | custom `[ClassData]` returning memory | `string` in `[InlineData]` + `.AsMemory()` in body | Avoids `[MemberData]` ceremony; matches existing convention |
| Float/tuple compare | manual epsilon | `Assert.Equal((float,float,float,float) tuple)` | Suite already does this (`ActorTests`); `float` equality on these literals already passes |

**Key insight:** The existing suite is already a clean characterization harness — the right move is to *extend* its style, not introduce assertion libraries or fixtures. Every needed assertion (`Null`, `NotNull`, `Equal`, `Throws`, `True/False`) is in xUnit's built-in `Assert`.

## Runtime State Inventory

Not a rename/refactor/migration phase — test-only additions. Section omitted intentionally (no stored data, service config, OS-registered state, secrets, or build artifacts are altered).

## Common Pitfalls

### Pitfall 1: Asserting null where Parse is lazy
**What goes wrong:** `Assert.Null(Actor.Parse("garbage"))` / `Assert.Null(Threat.Parse("<abc>"))` fail — these `Parse` methods construct successfully and defer the throw to property access.
**Why it happens:** Numeric parses live in lazy property getters, not in `Parse`.
**How to avoid:** Consult the eager/lazy table. For lazy models, assert on the property (`Assert.Throws` today, `Assert.Null`/value in Phase 2).
**Warning signs:** A "malformed" test goes green for the wrong reason (object returned), or red because you expected null but got an object.

### Pitfall 2: Shared static cache cross-contamination
**What goes wrong:** Two tests use the same literal; the second gets a cached `GameObject`/`Action`/`Ability` and never re-runs `Parse`, so a parse change isn't exercised.
**Why it happens:** `CombatLogs.GameObjectCache`/`ActionCache` are process-wide static dictionaries keyed on `Rom.GetHashCode()` and never cleared between tests.
**How to avoid:** Use a unique literal per test (vary the name or id). Don't assert on cache contents. (Don't try to clear the cache — it's `internal` and clearing it is a Phase-3 concern.)
**Warning signs:** A test passes in isolation but its assertion seems not to reflect a parse path you changed.

### Pitfall 3: Locale flakiness in the timestamp test
**What goes wrong:** `CombatLogLine`'s `DateTime.Parse` (BUG-03) is culture-sensitive; a test asserting a specific parsed `DateTime` for a given string may pass on a US dev box and behave differently under another `CurrentCulture`/CI locale.
**Why it happens:** No `InvariantCulture` argument (the bug).
**How to avoid:** In Phase 1, **characterize** — assert the *current* throw on an unambiguously non-US format, or assert successful parse only for formats SWTOR actually emits (`HH:mm:ss` / `HH:mm:ss.fff`, which are time-only and culture-robust for the separator). Do **not** assert invariant correctness on an ambiguous date — that's the Phase-2 fix. If you must pin culture for a deterministic characterization, set it locally within the test and restore it in a `finally` (or document the assumption); do not mutate global culture for the whole run.
**Warning signs:** A timestamp assertion that's green locally, red in CI.

### Pitfall 4: Mis-reading "no skips" (criterion 4)
**What goes wrong:** Shipping several `[Fact(Skip=...)]` placeholders, then criterion 4 ("NO skips") is read literally as a failure.
**Why it happens:** Two valid mechanisms (Skip vs. Assert.Throws) for the same goal.
**How to avoid:** Default to `Assert.Throws` characterization (zero skips, fully green). Use `Skip` only if the team explicitly wants a visible Phase-2 TODO marker; if so, document that "skipped" ≠ "failed" and that `dotnet test` still exits 0.

## Code Examples

### Locale: position uses InvariantCulture already (assert current behavior)
```csharp
// Actor.ExtractPosition uses CultureInfo.InvariantCulture (Actor.cs:147,154).
// A comma is the FIELD separator, so "4641,05" is read as two fields, not one decimal.
[Fact]
public void Position_Comma_Is_Field_Separator_Not_Decimal()
{
    var a = Actor.Parse("@N#1|(4641.05,4529.71,694.02,-124.45)|(1/2)".AsMemory());
    Assert.NotNull(a);
    Assert.Equal((4641.05f, 4529.71f, 694.02f, -124.45f), a.Position); // invariant '.' decimal
}
```

### Delimiter-in-name graceful handling (already green via Actor.GetName try/catch)
```csharp
[Theory]
[InlineData("@Name[bracket]#1|(0,0,0,0)|(1/2)")]
[InlineData("@Name{brace}#1|(0,0,0,0)|(1/2)")]
[InlineData("@Name@at#1|(0,0,0,0)|(1/2)")]
public void Actor_Name_With_Delimiters_Does_Not_Throw_From_Parse(string raw)
{
    var a = Actor.Parse(raw.AsMemory());
    Assert.NotNull(a);                 // Parse never throws (lazy)
    _ = a.Name;                        // GetName try/catch → string or null, never throws
}
```

### Threat / Value guard cases that are green TODAY
```csharp
[Theory]
[InlineData("<>")]        // length guard / empty scope
[InlineData("")]          // empty
[InlineData("<vfoo>")]    // 'v' prefix rejected
public void Threat_Parse_Rejects_Cleanly(string raw)
    => Assert.Null(Threat.Parse(raw.AsMemory()));

[Theory]
[InlineData("(he)")]      // HeroEngine prefix
[InlineData("no parens")] // no '(' / ')'
public void Value_Parse_Rejects_Cleanly(string raw)
    => Assert.Null(Value.Parse(raw.AsMemory()));
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| All `[Fact]`, no data-driven tests | `[Theory]/[InlineData]` for edge matrices | This phase | Less duplication; per-row failure reporting |
| Implicit "malformed → null" assumption | Explicit eager/lazy throw characterization | This phase | Tests reflect *actual* behavior; Phase 2 inverts them deliberately |

**Deprecated/outdated:** None relevant. (Project-wide preview package versions are real tech debt but are DEP-01/Phase 5, out of scope here. Do **not** bump xUnit in Phase 1.)

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `CombatLogLine`'s `DateTime.Parse` throws `FormatException` on a non-parseable/ambiguous timestamp (vs. silently misparsing) | Pitfall 3 / criterion 2 | If it misparses instead of throwing, the timestamp characterization assertion type changes — write the test to assert the *observed* result when authored; verify by running once. LOW risk: standard `DateTime.Parse` throws on unparseable input. |
| A2 | SWTOR emits time-only stamps (`HH:mm:ss[.fff]`), making the separator culture-robust | Pitfall 3 | If full dates appear, more locale care needed. Existing golden lines (`[18:12:13]`, `[20:33:17.759]`) support time-only. LOW risk. |
| A3 | `[Fact(Skip=...)]` reports skipped (not failed) and `dotnet test` exits 0 | Criterion 4 / Pitfall 4 | Standard xUnit behavior; verifiable by a single run. LOW risk. |

**Note:** A1-A3 are LOW-risk and confirmable by running the authored test once. The eager/lazy throw map itself is `[VERIFIED]` by source read, not assumed.

## Open Questions

1. **Skip vs. Assert.Throws for the Phase-2 contract tests**
   - What we know: Both keep `dotnet test` green; CONTEXT.md permits `Skip`.
   - What's unclear: Whether criterion 4's "NO skips" is literal.
   - Recommendation: Default to `Assert.Throws` characterization (zero skips). The Phase-2 plan inverts each named test. Use `Skip` only if a visible TODO marker is explicitly desired.

2. **Exact `DateTime.Parse` failure mode on an ambiguous date**
   - What we know: It's culture-sensitive (BUG-03) and SWTOR uses time-only stamps.
   - What's unclear: Behavior on a deliberately non-US full-date string (throw vs. misparse).
   - Recommendation: When authoring the locale timestamp test, run it once and assert the observed behavior; annotate as a BUG-03 characterization to invert in Phase 2.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET 8 SDK | build + `dotnet test` | Assumed ✓ (project targets net8.0) | 8.0 | — |
| xUnit + runner + Test SDK | test execution | ✓ (in csproj) | see Standard Stack | — |
| SWTOR CombatLogs dir / settings | **NOT required by Phase 1 tests** | n/a | — | Tests are hermetic; Phase 3 abstracts this |

**Missing dependencies with no fallback:** None — Phase 1 tests are in-memory and CI-safe by design.
**Missing dependencies with fallback:** None.

## Validation Architecture

> `workflow.nyquist_validation: true` → section included. `[VERIFIED: .planning/config.json]`

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.5.0-pre.44 (+ Test SDK 17.7.0-preview, runner.visualstudio 2.5.0-pre.27) |
| Config file | none — config lives in `SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` |
| Quick run command | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj --filter "FullyQualifiedName~<Type>Tests"` |
| Full suite command | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| TEST-03 | Malformed input → graceful (guarded models) | unit | `dotnet test --filter "FullyQualifiedName~ThreatTests"` (+ Value/Action) | ✅ extend existing |
| TEST-03 | Malformed input → current throw characterized (eager: GameObject/CombatLogLine) | unit | `dotnet test --filter "FullyQualifiedName~GameObjectTests"` | ✅ extend existing |
| TEST-03 | Malformed input → lazy throw on property (Actor/Value.Id/Threat.Value/Ability.Id) | unit | `dotnet test --filter "FullyQualifiedName~ActorTests"` | ✅ extend existing |
| TEST-03 | Locale numeric (position invariant) | unit | `dotnet test --filter "FullyQualifiedName~ActorTests"` | ✅ extend existing |
| TEST-03 | Locale timestamp characterized | unit | `dotnet test --filter "FullyQualifiedName~CombatLogLineTests"` | ✅ extend existing |
| TEST-03 | Delimiter-in-name (`[ ] { } @ :`) no IndexOutOfRange escapes | unit (`[Theory]`) | `dotnet test --filter "FullyQualifiedName~ActorTests"` (+ GameObject) | ✅ extend existing |
| TEST-03 | Golden-line regression locks (all 7 models) | unit | `dotnet test` | ✅ extend existing |

### Sampling Rate
- **Per task commit:** `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj --filter "FullyQualifiedName~<TypeUnderEdit>Tests"`
- **Per wave merge:** `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj`
- **Phase gate:** full suite green (zero red; zero skips if Assert.Throws strategy used) before `/gsd-verify-work`.

### Wave 0 Gaps
- None — test infrastructure exists (one `*Tests.cs` per model, `GlobalUsings.cs` with `global using Xunit;`, csproj fully configured). No framework install, no new fixtures, no new files needed. `[Theory]` is available in the referenced xUnit version; first use of it in this phase requires no setup.

*(If a tiny `LogLine` builder helper is added per Pattern 5, it lives in the test project as an `internal static` class — no new project, no new reference.)*

## Security Domain

> `security_enforcement: true`, ASVS level 1. `[VERIFIED: .planning/config.json]`

### Applicable ASVS Categories
| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | No auth in a local log parser test phase |
| V3 Session Management | no | No sessions |
| V4 Access Control | no | No access control surface |
| V5 Input Validation | **yes (subject under test)** | This phase's entire purpose is hardening parser input handling against malformed/adversarial log lines. Tests assert no uncaught exception escapes (graceful where guarded; characterized where not yet). The *fix* for unguarded paths is Phase 2 (`TryParse`); Phase 1 documents the current gap via tests. |
| V6 Cryptography | no | No crypto |

### Known Threat Patterns for span-based text parsing
| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Malformed/truncated line → `IndexOutOfRangeException` / `FormatException` crashes the reader task | Denial of Service | `TryParse` + length/`IndexOf != -1` guards (Phase 2 BUG-05); Phase 1 tests **expose** every such site |
| Locale-dependent parse → wrong numbers/timestamps | Tampering (data integrity) | `CultureInfo.InvariantCulture` everywhere (Phase 2 BUG-03); Phase 1 characterizes current locale behavior |
| Delimiter injection in names (`[ ] { } @ : /`) → wrong slice / crash | Tampering / DoS | Guarded slicing; Phase 1 `[Theory]` matrices prove handling and pin current behavior |

**Phase 1 security posture:** Phase 1 is the *detection* layer for these input-validation threats — it cannot remediate (test-only), but it converts every latent crash site into a named, asserted test so Phase 2's remediation is verifiable. No new attack surface is introduced (no I/O, no network, in-memory literals only).

## Sources

### Primary (HIGH confidence)
- `SwtorLogParser/Model/{CombatLogLine,Actor,GameObject,Value,Threat,Ability,Action}.cs` — read this session; eager/lazy throw map traced directly from source.
- `SwtorLogParser/Monitor/CombatLogs.cs` — cache + constants, confirmed shared static caches.
- `SwtorLogParser.Tests/{Actor,CombatLogLine,Value,GameObject,Threat,Action,Ability}Tests.cs`, `GlobalUsings.cs`, `SwtorLogParser.Tests.csproj` — existing conventions and package versions.
- `.planning/phases/01-parser-safety-net/01-CONTEXT.md`, `.planning/REQUIREMENTS.md`, `.planning/codebase/{TESTING,CONCERNS}.md`, `.planning/STATE.md`, `CLAUDE.md`, `.planning/config.json` — phase constraints and config.

### Secondary (MEDIUM confidence)
- None — all findings grounded in repo source.

### Tertiary (LOW confidence)
- None.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — read from csproj; no new packages.
- Architecture (eager/lazy throw map): HIGH — traced line-by-line from `Model/*.cs` this session.
- Pitfalls: HIGH — derived from actual cache/culture/lazy-parse code paths.
- Locale `DateTime.Parse` exact failure mode: MEDIUM — A1/A2 (confirmable with one test run).

**Research date:** 2026-06-11
**Valid until:** 2026-09-11 (stable — no fast-moving external deps; valid until the parser source changes, i.e. Phase 2).
