<!-- GSD:project-start source:PROJECT.md -->

## Project

**SWTOR Log Parser — Hardening Milestone**

A .NET 8 / C# parser for *Star Wars: The Old Republic* combat logs. A shared core library (`SwtorLogParser`) tails the game's log files, parses each line with zero-allocation span parsing, and exposes a live reactive stream of per-player DPS/HPS/APM statistics. Three consumer hosts render that stream: a managed CLI, a Native AOT CLI, and a transparent WinForms overlay. The current milestone is **hardening** — turning a working multi-prototype experiment into a correct, maintainable, CI-backed codebase by resolving every issue catalogued in `.planning/codebase/CONCERNS.md`.

**Core Value:** The live DPS/HPS stats pipeline must stay correct and reliable while the codebase becomes safe to maintain and extend — no regressions to parsing or the reactive stream.

### Constraints

- **Tech stack**: .NET 8, C#, Rx.NET (`System.Reactive`), WinForms, xUnit — established; stay on .NET 8.
- **Compatibility**: Windows-only is acceptable and intended; do not add cross-platform burden.
- **AOT**: `SwtorLogParser.Native.Cli` uses Native AOT and the core library is `IsAotCompatible=true` — refactors (esp. DI) must not break AOT compatibility (no reflection-heavy patterns in the core library).
- **No regressions**: the parser model and the live DPS/HPS stream must behave identically after hardening; new tests should lock in current correct behavior before refactors.

<!-- GSD:project-end -->

<!-- GSD:stack-start source:codebase/STACK.md -->

## Technology Stack

## Languages

- C# (LangVersion `preview` in `SwtorLogParser.Cli`, default latest elsewhere) - All application and library code across the five projects.
- INI / plain-text parsing (no separate language) - SWTOR combat log `.txt` files and `*PlayerGUIState.ini` settings files are parsed manually using `ReadOnlySpan<char>` / `ReadOnlyMemory<char>`.

## Runtime

- .NET 8.0 (`net8.0`) for core library, CLI, Native CLI, and Tests.
- .NET 8.0 Windows (`net8.0-windows`) for the WinForms overlay (`SwtorLogParser.Overlay`).
- Native AOT compilation enabled for `SwtorLogParser.Native.Cli` (`PublishAot=true`) - compiles to native code with no .NET runtime required. Core library is marked `IsAotCompatible=true` (no reflection).
- NuGet (PackageReference style).
- Lockfile: missing (no `packages.lock.json` present). No `global.json` pinning the SDK version.

## Frameworks

- Microsoft.Extensions.DependencyInjection `8.0.0-preview.5.23280.8` - DI container (referenced in core library `SwtorLogParser`).
- Microsoft.Extensions.Logging.Console / .Debug `8.0.0-preview.5.23280.8` - Logging providers (core library).
- Microsoft.Extensions.Logging.Abstractions `8.0.0-preview.5.23280.8` - Logging abstractions (Native CLI).
- System.Reactive `6.0.1-preview.1` - Rx.NET; powers DPS/HPS/APM streaming calculations via observables (`CombatLogsMonitor.DpsHps`).
- System.CommandLine `2.0.0-beta4.22272.1` - Command-line parsing (Native CLI: `monitor`, `list` commands).
- System.CommandLine.Rendering `0.4.0-alpha.22272.1` - Console table rendering (managed CLI `SwtorLogParser.Cli`).
- Windows Forms (`UseWindowsForms=true`) - Overlay UI (`SwtorLogParser.Overlay`).
- xUnit `2.5.0-pre.44` - Test framework.
- xunit.runner.visualstudio `2.5.0-pre.27` - Test runner/adapter.
- Microsoft.NET.Test.Sdk `17.7.0-preview.23280.1` - Test SDK.
- coverlet.collector `6.0.0` - Code coverage collection.
- Microsoft.NET.Sdk - Standard SDK-style project build.
- Native AOT toolchain (for `SwtorLogParser.Native.Cli`).

## Key Dependencies

- System.Reactive `6.0.1-preview.1` - Core to real-time DPS/HPS/APM stats pipeline.
- System.CommandLine (+ Rendering) - CLI command structure and output rendering.
- Microsoft.Extensions.* (DI + Logging) - Cross-cutting infrastructure in core library.

## Configuration

- No environment variables or external configuration files are read by the application.
- No `.env`, `appsettings.json`, or secret files present.
- File locations are derived at runtime from OS special folders (see Platform Requirements and `INTEGRATIONS.md`).
- Per-project `.csproj` files; solution `SwtorLogParser.sln`.
- `ImplicitUsings` enabled across all projects; `Nullable` enabled across all projects.
- `DockerDefaultTargetOS=Linux` set on CLI projects (no Dockerfile present in repo).

## Platform Requirements

- .NET 8.0 SDK.
- Windows required to build/run the Overlay (`net8.0-windows` + WinForms) and to resolve the real SWTOR log/settings paths.
- P/Invoke into `user32.dll` for the overlay window dragging (`SwtorLogParser.Overlay/NativeMethods.cs`).
- Windows desktop (SWTOR client machine). The parser reads logs from the local `My Documents\Star Wars - The Old Republic\CombatLogs` folder.
- Native CLI produces a self-contained native executable (no runtime install needed).

<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->

## Conventions

## Naming Patterns

- PascalCase matching the primary type name: `CombatLogLine.cs`, `GameObject.cs`, `CombatLogsMonitor.cs`
- One public type per file
- PascalCase for all methods, public and private: `Parse`, `GetSections`, `ExtractPosition`, `GetName`
- Static factory methods named `Parse` returning a nullable type are the standard construction entry point (see `SwtorLogParser/Model/CombatLogLine.cs:38`, `Actor.cs:113`, `Value.cs:58`)
- camelCase for locals and parameters: `rom`, `sections`, `startIndex`, `lastSection`
- Private fields use leading underscore + camelCase: `_id`, `_name`, `_isNested`, `_position` (`SwtorLogParser/Model/GameObject.cs:7`, `Actor.cs:8`)
- `rom` / `roms` is the project-wide convention for `ReadOnlyMemory<char>` and `List<ReadOnlyMemory<char>>`
- PascalCase classes: `Ability`, `Actor`, `Threat`, `Value`
- Domain model types live in namespace `SwtorLogParser.Model`; file watching/IO in `SwtorLogParser.Monitor`
- Note `Action` is a domain type (`SwtorLogParser.Model.Action`) that collides with `System.Action`, requiring aliases (`using Action = SwtorLogParser.Model.Action;` in `Monitor/CombatLogs.cs:2`)
- `const` locals are camelCase: `sectionOpen`, `sectionClose` (`CombatLogLine.cs:58`)

## Code Style

- 4-space indentation, Allman braces (opening brace on its own line)
- Single-line `if` statements without braces are common: `if (rom.IsEmpty) return null;`
- Expression-bodied members used for one-liners: `public override int GetHashCode() => Rom.GetHashCode();` (`CombatLogLine.cs:18`)
- No `.editorconfig` present; style is implicit/IDE-default
- No analyzers or lint config detected (no `.editorconfig`, no `Directory.Build.props`)
- Code style enforced only by convention
- `Nullable` reference types enabled in all projects (`<Nullable>enable</Nullable>`)
- `ImplicitUsings` enabled — no explicit `using System;` etc.
- Target framework: `net8.0`
- Library `SwtorLogParser` is `<IsAotCompatible>true</IsAotCompatible>` — avoid reflection-heavy patterns

## Import Organization

- Always used: `namespace SwtorLogParser.Model;` (not block-scoped)
- Not applicable (C# project references, not module aliases)

## Error Handling

- Parsing favors **null returns over exceptions**: every `Parse` method returns a nullable type and returns `null` on invalid/empty input rather than throwing (`CombatLogLine.cs:40`, `Value.cs:64`, `Actor.cs:115`)
- Defensive guards at the top of methods: `if (rom.IsEmpty) return null;`, section-count checks (`sections.Count != 5 ? null : ...`)
- Try/catch used sparingly and swallows to null: `Actor.GetName()` wraps slicing in `try { ... } catch { return null; }` (`Actor.cs:45-59`)
- `int.Parse` / `ulong.Parse` / `long.Parse` are used directly on spans without TryParse in many getters (`GameObject.cs:75`, `Actor.cs:93`); culture-sensitive float parsing uses `CultureInfo.InvariantCulture` + `float.TryParse` (`Actor.cs:147`)

## Logging

- `Microsoft.Extensions.Logging.Console` / `.Debug` referenced in the core library csproj
- No logging calls observed inside the parsing model classes; parsing is pure/silent
- Parsing layer does not log; failures surface as `null`

## Comments

- Sparse. Comments appear only to explain non-obvious span-scanning logic (`Value.cs:101` "Ignore any leading whitespace", `Actor.cs:130` "Skip the opening parenthesis")
- No XML doc comments (`///`) on public APIs

## Function Design

- Small, single-purpose private helpers (`GetName`, `GetId`, `GetParentId`) backing lazy public properties
- Hot-path methods take `ReadOnlyMemory<char>` / operate on `.Span` to avoid string allocations
- Nullable returns are the norm for parse/extract helpers
- Lazy-initialized properties via null-coalescing assignment: `Name => _name ??= GetName();` (`GameObject.cs:27`, `Actor.cs:28`)

## Module Design

- Private constructors + public static `Parse` factory (controls validation and caching)
- Object caching via static dictionaries keyed by hash code: `CombatLogs.GameObjectCache`, `CombatLogs.ActionCache` (`Monitor/CombatLogs.cs:8-9`); `Parse` checks cache before allocating (`Ability.cs:15`, `GameObject.cs:103`)
- Value types implement `IEquatable<T>` based on the underlying `Rom.GetHashCode()` (`CombatLogLine.cs:3`, `GameObject.cs:5`)
- Dedicated comparer class `CombatLogLineComparer` for `HashSet` usage
- Shared lookup tables and caches are `internal static` on `CombatLogs`; public surface is the model types and `Parse`/enumerate methods

<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->

## Architecture

## Overview

## Pattern

- **Core library:** `SwtorLogParser` (`net8.0`, `IsAotCompatible=true`) — referenced by all three hosts plus the test project.
- **Hosts are pure consumers:** each subscribes to `CombatLogsMonitor.Instance.DpsHps` and renders. No host contains parsing logic.

## Layers

## Data Flow

```

```

## Key Abstractions

- **`CombatLogsMonitor.Instance`** — the single producer; owns file polling, parsing dispatch, and the Rx pipeline.
- **`Model.Parse(...)` factories** — uniform static-parse contract across all domain types; null = unparseable.
- **`IObservable<PlayerStats>` (`DpsHps`)** — the seam between core and every host.
- **Span-based lazy models** — parsing cost is deferred to first property access and cached.

## Entry Points

- `SwtorLogParser.Cli/Program.cs` — managed CLI host (System.CommandLine).
- `SwtorLogParser.Native.Cli/Program.cs` — Native AOT CLI host.
- `SwtorLogParser.Overlay/ParserForm.cs` — WinForms overlay; monitor starts on form activation.

## Architectural Concerns (surfaced for the map)

- Singleton producer exposing a **public mutable `IObservable`** — process-wide shared state, hard to test/inject.
- **Duplicated `View/` code** (`Entry`, `SlidingExpirationList`) and host wiring across all three hosts.
- **Non-thread-safe, unbounded static caches** (`CombatLogs.ActionCache` / `GameObjectCache`) written from the background reader task.
- **Hardcoded Windows special-folder paths** in `CombatLogs.cs` — Windows-only assumption.

<!-- GSD:architecture-end -->

<!-- GSD:skills-start source:skills/ -->

## Project Skills

No project skills found. Add skills to any of: `.claude/skills/`, `.agents/skills/`, `.cursor/skills/`, `.github/skills/`, or `.codex/skills/` with a `SKILL.md` index file.
<!-- GSD:skills-end -->

<!-- GSD:workflow-start source:GSD defaults -->

## GSD Workflow Enforcement

Before using Edit, Write, or other file-changing tools, start work through a GSD command so planning artifacts and execution context stay in sync.

Use these entry points:

- `/gsd-quick` for small fixes, doc updates, and ad-hoc tasks
- `/gsd-debug` for investigation and bug fixing
- `/gsd-execute-phase` for planned phase work

Do not make direct repo edits outside a GSD workflow unless the user explicitly asks to bypass it.
<!-- GSD:workflow-end -->

<!-- GSD:profile-start -->

## Developer Profile

> Profile not yet configured. Run `/gsd-profile-user` to generate your developer profile.
> This section is managed by `generate-claude-profile` -- do not edit manually.
<!-- GSD:profile-end -->
