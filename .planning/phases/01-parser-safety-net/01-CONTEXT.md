# Phase 1: Parser Safety Net - Context

**Gathered:** 2026-06-11
**Status:** Ready for planning

<domain>
## Phase Boundary

This phase locks in the *current correct* parse behavior of the SWTOR combat-log domain model with automated tests, so that every subsequent change (Phases 2-6) can be verified to produce no regressions. It delivers TEST-03: edge-case tests for malformed lines, locale-formatted numbers/dates, and delimiter characters inside names — plus golden-line regression tests for known-good input.

**In scope:** New/extended xUnit tests over the `SwtorLogParser/Model/*.cs` `Parse` factories. Pure, hermetic, in-memory tests only.

**Out of scope:** Changing parser production code (that's Phase 2 — Correctness Bugs), refactoring the non-hermetic `CombatLogs` filesystem tests (deferred to Phase 3), monitor/Rx/DPS-HPS tests (Phase 3).

</domain>

<decisions>
## Implementation Decisions

### Test Scope & Edge Cases
- Edge-case tests cover ALL `Parse`-factory models: `Ability`, `Action`, `Actor`, `Value`, `Threat`, `GameObject`, `CombatLogLine`.
- Malformed input asserts `null` / no-throw — this codifies the *intended* contract. Where current production code actually throws (the unguarded `int/long/ulong.Parse` sites), the test expresses the desired post-Phase-2 behavior; if a test cannot pass against current code without a production change, mark it clearly (e.g. `[Fact(Skip="locks Phase 2 behavior — BUG-05")]` or an `xfail`-style note) so Phase 2 flips it green rather than Phase 1 shipping red.
- Locale coverage: comma-decimal inputs (e.g. `4641,05`) and non-US date strings assert invariant parsing / clean rejection.
- Delimiter-in-name coverage: names containing `{ } [ ] @ :` must not break index-based span parsing.

### Test Data & Style
- Test data source: inline literal log fragments built with `.AsMemory()` (matches existing convention in `SwtorLogParser.Tests`).
- Use `[Theory]` / `[InlineData]` for the new edge-case input matrices to reduce duplication; keep `[Fact]` for single golden-line cases.
- File organization: extend the existing per-model `*Tests.cs` files rather than adding parallel `*EdgeCaseTests.cs`.
- Naming: keep descriptive `Snake_Case` test method names (e.g. `Comma_Decimal_Value_Parses_Invariant`).

### Behavior-Lock & Environment Tests
- Add "golden line" regression tests per type: a known-good full combat-log line (or model fragment) → expected parsed model values. These are the strongest regression locks before the Phase 2-4 changes.
- Defer the non-hermetic `CombatLogs` filesystem tests (`All_Logs_Are_Not_Null`, `Player_Is_Local_Is_True`) to Phase 3, where the DI refactor abstracts the filesystem. Leave them untouched in Phase 1.
- New Phase 1 tests must NOT depend on `CombatLogs` or the filesystem — in-memory strings only, fully hermetic, CI-safe.

### Claude's Discretion
- Exact set of `[InlineData]` rows per matrix, helper builders for log-line strings, and precise golden-line samples are at Claude's discretion, guided by `.planning/codebase/TESTING.md` conventions.

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets
- Existing per-model test files: `SwtorLogParser.Tests/{Ability,Action,Actor,CombatLogLine,GameObject,Threat,Value}Tests.cs`.
- `SwtorLogParser.Tests/GlobalUsings.cs` already has `global using Xunit;`.
- Each model exposes `static {Type}.Parse(ReadOnlyMemory<char>)` returning the model or `null`.

### Established Patterns
- xUnit `[Fact]` with `Snake_Case` names; assertions via static `Assert` (`Assert.Null`, `Assert.Equal`, `Assert.NotNull`, etc.); inputs built with `"...".AsMemory()`.
- No mocks/fixtures; inline literals per test. No `[Theory]` currently used (Phase 1 introduces it for edge matrices).

### Integration Points
- Tests reference only `SwtorLogParser/SwtorLogParser.csproj` (already the sole project reference of the test project). No new project references needed.
- Run via `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj`.

</code_context>

<specifics>
## Specific Ideas

- Known unguarded numeric parse sites to target with malformed-input tests: `Threat.cs:14`, `Actor.cs:64,73,93,100,107`, `Value.cs:47`, `GameObject.cs:75,87,95`.
- Locale risk site: `CombatLogLine.cs:9` (`DateTime.Parse`).
- Tests that assert intended-but-not-yet-true behavior (depends on Phase 2 fixes) should be explicitly marked so Phase 1's `dotnet test` is green, and Phase 2 removes the marker when the production fix lands.

</specifics>

<deferred>
## Deferred Ideas

- Abstracting `CombatLogs` filesystem access for hermetic testing → Phase 3 (Monitor Refactor + Coverage).
- Monitor lifecycle / Rx pipeline / DPS-HPS math tests → Phase 3 (TEST-01, TEST-02).

</deferred>
