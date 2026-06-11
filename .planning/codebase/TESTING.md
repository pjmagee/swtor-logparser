---
title: Testing
focus: quality
last_mapped: 2026-06-11
---

# Testing Patterns

**Analysis Date:** 2026-06-11

## Test Framework

**Runner:** xUnit `2.5.0-pre.44`. Config lives in `SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` (no separate runner config). Test SDK `Microsoft.NET.Test.Sdk` `17.7.0-preview.23280.1`; VS adapter `xunit.runner.visualstudio` `2.5.0-pre.27`.

**Assertions:** xUnit built-in static `Assert` API. No FluentAssertions / Shouldly.

**Coverage:** `coverlet.collector` `6.0.0` referenced; no threshold or settings file.

**Run commands:**
```bash
dotnet test
dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj
dotnet test --collect:"XPlat Code Coverage"
dotnet test --filter "FullyQualifiedName~AbilityTests"
```

## Test Project Setup

Single test project `SwtorLogParser.Tests/`: `net8.0`, `ImplicitUsings` + `Nullable` enabled, `IsTestProject=true`, `IsPackable=false`. References only `SwtorLogParser/SwtorLogParser.csproj` — the CLI, Native CLI, and Overlay projects are untested. `SwtorLogParser.Tests/GlobalUsings.cs` declares `global using Xunit;`.

## Test File Organization

Separate project, flat layout, one file per model type named `{Type}Tests.cs`: `AbilityTests.cs`, `ActionTests.cs`, `ActorTests.cs`, `CombatLogLineTests.cs`, `GameObjectTests.cs`, `ThreatTests.cs`, `ValueTests.cs`. Test classes are `public`, named `{Type}Tests`, namespace `SwtorLogParser.Tests`. Each maps to a type in `SwtorLogParser/Model/`.

## Test Structure

Plain `[Fact]` methods with descriptive `Snake_Case` names (`Zero_Is_Positive`, `Empty_Line_Is_Null`, `Companion_Is_Parsed`). No `[Theory]` / `[InlineData]`. Typical pattern: parse a raw log fragment via `static {Type}.Parse(ReadOnlyMemory<char>)` (input built with `.AsMemory()`), then assert on the parsed model. Null/invalid inputs are expected to return `null` (`Assert.Null`). No `Skip`, traits, categories, or base classes.

Example (`SwtorLogParser.Tests/AbilityTests.cs`):
```csharp
[Fact]
public void Ability_With_Name_And_Id_Parsed()
{
    var ability = Ability.Parse("Overlord's Command Throne {3039943492370432}".AsMemory());
    Assert.NotNull(ability);
    Assert.False(ability.IsNested);
    Assert.Equal("Overlord's Command Throne", ability.Name);
    Assert.Equal(3039943492370432u, ability.Id);
}
```

## Common Assertions

`Assert.NotNull` / `Assert.Null`, `Assert.Equal` (incl. unsigned literals like `3039943492370432u` and tuple positions like `(4641.05f, 4529.71f, 694.02f, -124.45f)`), `Assert.True` / `Assert.False` for flags, `Assert.NotEmpty`, `Assert.Single` (e.g. `HashSet` dedup test in `CombatLogLineTests.cs`).

## Mocking

None. No Moq / NSubstitute / FakeItEasy. Parsers are pure static methods with no injected dependencies, so no mocks/stubs/spies exist.

## Fixtures and Factories

Inline literal strings per `[Fact]`. No `[ClassFixture]` / `[CollectionFixture]`, no `IDisposable` setup/teardown, no committed fixture files.

## External / Environment-Dependent Tests (Important)

- `CombatLogLineTests.All_Logs_Are_Not_Null` (`SwtorLogParser.Tests/CombatLogLineTests.cs:9`) iterates `CombatLogs.EnumerateCombatLogs()`, reading `*.txt` from `%UserProfile%/Documents/Star Wars - The Old Republic/CombatLogs` (`SwtorLogParser/Monitor/CombatLogs.cs:11`). Passes vacuously when no logs exist; non-deterministic across machines.
- `ActorTests.Player_Is_Local_Is_True` (`SwtorLogParser.Tests/ActorTests.cs:28`) iterates `CombatLogs.PlayerNames`, populated in the `CombatLogs` static constructor from `%LocalAppData%/SWTOR/swtor/settings/*PlayerGUIState.ini` (`SwtorLogParser/Monitor/CombatLogs.cs:21-25`). A missing directory makes the static constructor throw `DirectoryNotFoundException`, failing any test touching `CombatLogs`.

Consequence: `CombatLogs`-backed tests are not hermetic and behave differently in CI vs. a developer gaming machine. Filesystem access should be abstracted/injected so it can be tested against committed sample data.

## Test Types

- **Unit:** bulk of the suite — pure parser tests over `Model` types.
- **Integration:** only accidental (the filesystem-backed tests above); no dedicated integration project/category.
- **E2E:** none; CLI / Native CLI / Overlay have no automated tests.

## Coverage

No threshold enforced, no `runsettings`, no CI gate detected. View with `dotnet test --collect:"XPlat Code Coverage"` (Cobertura XML under `SwtorLogParser.Tests/TestResults/<guid>/coverage.cobertura.xml`).

## Gaps Worth Noting

- No `[Theory]` data-driven tests despite many parse-variant cases.
- No tests for `Monitor/CombatLog.cs`, `Monitor/CombatLogsMonitor.cs`, or `Extensions/CombatLogLineExtensions.cs`.
- Filesystem dependencies unabstracted, so file-based parsing can't be tested deterministically.

---

*Testing analysis: 2026-06-11*
