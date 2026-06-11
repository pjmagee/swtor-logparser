---
title: Structure
focus: arch
last_mapped: 2026-06-11
---

# Structure

**Analysis Date:** 2026-06-11

## Solution Layout

A single .NET solution (`SwtorLogParser.sln`) with five projects: one core library, three presentation hosts, and one test project.

```
SwtorLogParser.sln
├── SwtorLogParser/                 # Core library (net8.0, AOT-compatible)
│   ├── Monitor/                    # File acquisition + reactive stats pipeline
│   │   ├── CombatLogsMonitor.cs    # Singleton producer; Start/Stop; DpsHps observable
│   │   ├── CombatLogs.cs           # Path resolution, log enumeration, static caches, PlayerNames
│   │   └── CombatLog.cs            # Single log file read/representation
│   ├── Model/                      # Zero-allocation span-parsed domain types
│   │   ├── CombatLogLine.cs
│   │   ├── Actor.cs
│   │   ├── Action.cs
│   │   ├── Ability.cs
│   │   ├── GameObject.cs
│   │   ├── Value.cs
│   │   ├── Threat.cs
│   │   └── ...                      # one public type per file, *.Parse(ReadOnlyMemory<char>)
│   └── Extensions/
│       └── CombatLogLineExtensions.cs
│
├── SwtorLogParser.Cli/             # Managed CLI host (System.CommandLine.Rendering)
│   ├── Program.cs
│   └── View/                       # Entry.cs, SlidingExpirationList.cs (duplicated)
│
├── SwtorLogParser.Native.Cli/      # Native AOT CLI host (raw console)
│   ├── Program.cs
│   └── View/                       # Entry.cs, SlidingExpirationList.cs (duplicated)
│
├── SwtorLogParser.Overlay/         # WinForms transparent overlay host
│   ├── ParserForm.cs               # Topmost DataGridView; starts monitor on activation
│   └── View/                       # Entry.cs, SlidingExpirationList.cs (duplicated)
│
└── SwtorLogParser.Tests/           # xUnit test project (net8.0)
    ├── GlobalUsings.cs             # global using Xunit;
    ├── AbilityTests.cs
    ├── ActionTests.cs
    ├── ActorTests.cs
    ├── CombatLogLineTests.cs
    ├── GameObjectTests.cs
    ├── ThreatTests.cs
    └── ValueTests.cs
```

## Key Locations

| What | Where |
|------|-------|
| Core domain models | `SwtorLogParser/Model/*.cs` |
| File monitoring + Rx pipeline | `SwtorLogParser/Monitor/CombatLogsMonitor.cs` |
| Path/log resolution + caches | `SwtorLogParser/Monitor/CombatLogs.cs` |
| Managed CLI entry | `SwtorLogParser.Cli/Program.cs` |
| Native AOT CLI entry | `SwtorLogParser.Native.Cli/Program.cs` |
| Overlay entry | `SwtorLogParser.Overlay/ParserForm.cs` |
| Tests | `SwtorLogParser.Tests/*Tests.cs` |

## Naming Conventions

- **Files:** PascalCase, named after the single public type they contain (one class per file).
- **Projects:** `SwtorLogParser[.Host]` with a matching `*.csproj` per project.
- **Test files:** `<Type>Tests.cs`, one per model type, mirroring `SwtorLogParser/Model/`.
- **View folders:** each host has its own `View/` folder containing `Entry.cs` and `SlidingExpirationList.cs` (copy-pasted across hosts — see `CONCERNS.md`).

## Runtime Data Locations (not in repo)

- **Combat logs:** `%UserProfile%/Documents/Star Wars - The Old Republic/CombatLogs/*.txt`
- **Player settings:** `%LocalAppData%/SWTOR/swtor/settings/*PlayerGUIState.ini`

These external filesystem dependencies are referenced from `SwtorLogParser/Monitor/CombatLogs.cs` and make several tests environment-dependent (see `TESTING.md`).
