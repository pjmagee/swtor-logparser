---
title: Architecture
focus: arch
last_mapped: 2026-06-11
---

# Architecture

**Analysis Date:** 2026-06-11

## Overview

A SWTOR (Star Wars: The Old Republic) combat-log parser. A single AOT-compatible **core library** tails the game's combat-log files, parses each line with zero-allocation span parsing, and exposes a live **reactive stream** of per-player DPS/HPS statistics. Three interchangeable **presentation hosts** (managed CLI, Native AOT CLI, WinForms overlay) consume that stream.

## Pattern

**Producer/consumer over a reactive pipeline**, with a shared core library and pluggable presentation hosts.

- **Core library:** `SwtorLogParser` (`net8.0`, `IsAotCompatible=true`) — referenced by all three hosts plus the test project.
- **Hosts are pure consumers:** each subscribes to `CombatLogsMonitor.Instance.DpsHps` and renders. No host contains parsing logic.

## Layers

1. **Acquisition / Monitoring** — `SwtorLogParser/Monitor/`
   - `CombatLogsMonitor` (process-wide singleton, `Instance`) launches two background tasks on `Start`:
     - `MonitorAsync` — polls `CombatLogs.CombatLogsDirectory` every **5s** for the newest `.txt` log file.
     - `ReadAsync` — tails the active file every **2s**, parsing each new line.
   - `CombatLogs` — resolves Windows special-folder paths, enumerates logs, holds static parse caches and `PlayerNames`.
   - `CombatLog` — represents/reads a single log file.

2. **Parsing / Domain Model** — `SwtorLogParser/Model/`
   - Zero-allocation parsing using `ReadOnlyMemory<char>` / `ReadOnlySpan<char>`.
   - Lazy cached properties (`_field ??= Get...()`) on `CombatLogLine`, `Actor`, `Action`, `Value`, `Ability`, `GameObject`, `Threat`, etc.
   - Each model exposes a `static Parse(ReadOnlyMemory<char>)` factory returning the model or `null` for invalid input.

3. **Reactive Stats Pipeline** — `CombatLogsMonitor`
   - Parsed lines push into a `Subject<CombatLogLine>`.
   - The `DpsHps` pipeline filters the last-10s player damage/heal lines, groups by source name, accumulates a 10s sliding `HashSet` (guarded by a `static object Lock`), and projects `PlayerStats`.
   - Exposed publicly as `IObservable<PlayerStats>`.

4. **Presentation Hosts** (consumers of `DpsHps.Subscribe(...)`):
   - `SwtorLogParser.Cli/Program.cs` — System.CommandLine.Rendering `TableView`.
   - `SwtorLogParser.Native.Cli/Program.cs` — raw console output (Native AOT).
   - `SwtorLogParser.Overlay/ParserForm.cs` — transparent, topmost WinForms `DataGridView`; starts the monitor on form activation.

## Data Flow

```
Combat log file (.txt on disk)
        │  MonitorAsync (5s poll → newest file)
        ▼
CombatLog.GetLogLines()  ──►  Model.Parse(ReadOnlyMemory<char>)  ──►  CombatLogLine
        │  ReadAsync (2s tail)                                            │
        ▼                                                                 ▼
                                   Subject<CombatLogLine>
                                            │  filter last 10s, group by source,
                                            │  sliding HashSet accumulate (static Lock)
                                            ▼
                                   IObservable<PlayerStats>  (DpsHps)
                          ┌──────────────────┼──────────────────┐
                          ▼                  ▼                  ▼
                    Cli (TableView)   Native.Cli (console)  Overlay (DataGridView)
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

> See `CONCERNS.md` for the full list with file:line citations.
