# Coding Conventions

**Analysis Date:** 2026-06-11

## Naming Patterns

**Files:**
- PascalCase matching the primary type name: `CombatLogLine.cs`, `GameObject.cs`, `CombatLogsMonitor.cs`
- One public type per file

**Functions / Methods:**
- PascalCase for all methods, public and private: `Parse`, `GetSections`, `ExtractPosition`, `GetName`
- Static factory methods named `Parse` returning a nullable type are the standard construction entry point (see `SwtorLogParser/Model/CombatLogLine.cs:38`, `Actor.cs:113`, `Value.cs:58`)

**Variables:**
- camelCase for locals and parameters: `rom`, `sections`, `startIndex`, `lastSection`
- Private fields use leading underscore + camelCase: `_id`, `_name`, `_isNested`, `_position` (`SwtorLogParser/Model/GameObject.cs:7`, `Actor.cs:8`)
- `rom` / `roms` is the project-wide convention for `ReadOnlyMemory<char>` and `List<ReadOnlyMemory<char>>`

**Types:**
- PascalCase classes: `Ability`, `Actor`, `Threat`, `Value`
- Domain model types live in namespace `SwtorLogParser.Model`; file watching/IO in `SwtorLogParser.Monitor`
- Note `Action` is a domain type (`SwtorLogParser.Model.Action`) that collides with `System.Action`, requiring aliases (`using Action = SwtorLogParser.Model.Action;` in `Monitor/CombatLogs.cs:2`)

**Constants:**
- `const` locals are camelCase: `sectionOpen`, `sectionClose` (`CombatLogLine.cs:58`)

## Code Style

**Formatting:**
- 4-space indentation, Allman braces (opening brace on its own line)
- Single-line `if` statements without braces are common: `if (rom.IsEmpty) return null;`
- Expression-bodied members used for one-liners: `public override int GetHashCode() => Rom.GetHashCode();` (`CombatLogLine.cs:18`)
- No `.editorconfig` present; style is implicit/IDE-default

**Linting:**
- No analyzers or lint config detected (no `.editorconfig`, no `Directory.Build.props`)
- Code style enforced only by convention

**Language Features:**
- `Nullable` reference types enabled in all projects (`<Nullable>enable</Nullable>`)
- `ImplicitUsings` enabled — no explicit `using System;` etc.
- Target framework: `net8.0`
- Library `SwtorLogParser` is `<IsAotCompatible>true</IsAotCompatible>` — avoid reflection-heavy patterns

## Import Organization

**Order (observed):**
1. `System.*` explicit usings (only when not covered by implicit usings, e.g. `using System.Globalization;` in `Actor.cs:1`)
2. Project usings (`using SwtorLogParser.Model;`, `using SwtorLogParser.Monitor;`)
3. Type aliases last (`using Action = SwtorLogParser.Model.Action;`)

**File-scoped namespaces:**
- Always used: `namespace SwtorLogParser.Model;` (not block-scoped)

**Path Aliases:**
- Not applicable (C# project references, not module aliases)

## Error Handling

**Patterns:**
- Parsing favors **null returns over exceptions**: every `Parse` method returns a nullable type and returns `null` on invalid/empty input rather than throwing (`CombatLogLine.cs:40`, `Value.cs:64`, `Actor.cs:115`)
- Defensive guards at the top of methods: `if (rom.IsEmpty) return null;`, section-count checks (`sections.Count != 5 ? null : ...`)
- Try/catch used sparingly and swallows to null: `Actor.GetName()` wraps slicing in `try { ... } catch { return null; }` (`Actor.cs:45-59`)
- `int.Parse` / `ulong.Parse` / `long.Parse` are used directly on spans without TryParse in many getters (`GameObject.cs:75`, `Actor.cs:93`); culture-sensitive float parsing uses `CultureInfo.InvariantCulture` + `float.TryParse` (`Actor.cs:147`)

## Logging

**Framework:**
- `Microsoft.Extensions.Logging.Console` / `.Debug` referenced in the core library csproj
- No logging calls observed inside the parsing model classes; parsing is pure/silent

**Patterns:**
- Parsing layer does not log; failures surface as `null`

## Comments

**When to Comment:**
- Sparse. Comments appear only to explain non-obvious span-scanning logic (`Value.cs:101` "Ignore any leading whitespace", `Actor.cs:130` "Skip the opening parenthesis")
- No XML doc comments (`///`) on public APIs

## Function Design

**Size:**
- Small, single-purpose private helpers (`GetName`, `GetId`, `GetParentId`) backing lazy public properties

**Parameters:**
- Hot-path methods take `ReadOnlyMemory<char>` / operate on `.Span` to avoid string allocations

**Return Values:**
- Nullable returns are the norm for parse/extract helpers
- Lazy-initialized properties via null-coalescing assignment: `Name => _name ??= GetName();` (`GameObject.cs:27`, `Actor.cs:28`)

## Module Design

**Construction:**
- Private constructors + public static `Parse` factory (controls validation and caching)
- Object caching via static dictionaries keyed by hash code: `CombatLogs.GameObjectCache`, `CombatLogs.ActionCache` (`Monitor/CombatLogs.cs:8-9`); `Parse` checks cache before allocating (`Ability.cs:15`, `GameObject.cs:103`)

**Equality:**
- Value types implement `IEquatable<T>` based on the underlying `Rom.GetHashCode()` (`CombatLogLine.cs:3`, `GameObject.cs:5`)
- Dedicated comparer class `CombatLogLineComparer` for `HashSet` usage

**Exports / Visibility:**
- Shared lookup tables and caches are `internal static` on `CombatLogs`; public surface is the model types and `Parse`/enumerate methods

---

*Convention analysis: 2026-06-11*
