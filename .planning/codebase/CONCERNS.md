---
title: Concerns
focus: concerns
last_mapped: 2026-06-11
---

# Concerns — Technical Debt, Bugs, Risks

Areas of concern found while mapping the SWTOR combat-log parser. Each item cites the file/line so it can be acted on directly. Severity is the mapper's judgment, not a guarantee.

## Tech Debt

- **All NuGet packages are preview/alpha, not stable.** `SwtorLogParser.csproj` (`Microsoft.Extensions.* 8.0.0-preview.5`, `System.Reactive 6.0.1-preview.1`), `SwtorLogParser.Tests.csproj` (`xunit 2.5.0-pre.44`, `Microsoft.NET.Test.Sdk 17.7.0-preview`), `SwtorLogParser.Cli.csproj` (`System.CommandLine.Rendering 0.4.0-alpha`). Fragile restores, missed security patches. **Fix:** move to GA versions; add `Directory.Packages.props` for central package management.
- **Triplicated view code.** `SlidingExpirationList.cs` and `Entry.cs` are copy-pasted across `SwtorLogParser.Cli/View/`, `SwtorLogParser.Native.Cli/View/`, and `SwtorLogParser.Overlay/View/` and have already diverged. **Fix:** extract to the core library.
- **`#if RELEASE / #elif DEBUG` singleton with no `#else`** at `SwtorLogParser/Monitor/CombatLogsMonitor.cs:15-20` — `Instance` is undefined in any other build config. **Fix:** add a default branch; prefer dependency injection over a static singleton.
- **Static mutable caches keyed on `ReadOnlyMemory<char>.GetHashCode()`** (`CombatLogs.cs:8-9`, `Action.cs:47-53`, `GameObject.cs:103-108`, `Ability.cs:15-18`) — non-content hash, not thread-safe, unbounded growth.

## Known Bugs / Correctness

- **Non-thread-safe `Dictionary.Add`** to shared static caches from the reader task while the overlay/CLI may also parse (`Action.cs:53`, `GameObject.cs:108`, `Ability.cs:18`). Race → corruption or `InvalidOperationException`.
- **Cancellation token mis-wiring.** `CombatLogsMonitor.Start` passes the outer `cancellationToken` to the worker tasks instead of `_cancellationTokenSource.Token`, so `Stop()`'s cancel never reaches them (`CombatLogsMonitor.cs:107-126`). Also, `Stop()` before `Start()` throws NRE (`_cancellationTokenSource` is only assigned in `Start`).
- **Culture-sensitive `DateTime.Parse`** at `CombatLogLine.cs:9` — should use `CultureInfo.InvariantCulture`.
- **Window filter compares log timestamps to `DateTime.Now`** at `CombatLogsMonitor.cs:48` — clock skew or log replay drops or keeps everything.
- **Unguarded `int/long/ulong.Parse`** in parse paths: `Threat.cs:14`, `Actor.cs:64,73,93,100,107`, `Value.cs:47`, `GameObject.cs:75,87,95`. Malformed lines throw instead of being skipped.
- **Static-constructor crash risk.** `CombatLogs.cs:23` does `Name.Split('_')[1]` over `*PlayerGUIState.ini` files; a filename without `_` throws `TypeInitializationException` at app startup.

## Security

- `CombatLog.GetLogLines()` opens files `FileAccess.ReadWrite` / `FileShare.ReadWrite` though it only reads (`CombatLog.cs:24`). **Fix:** open read-only. Otherwise this is a local-only tool with no network calls or secrets.

## Performance

- `CombatLog.ToString()` parses the entire file just to count lines; `GetLogLines()` allocates a `char[]` per line via `line.ToArray()` (`CombatLog.cs:16,28,33`), defeating the zero-copy `ReadOnlyMemory<char>` intent.
- Native CLI does `Console.Clear()` + full redraw per event (`Native.Cli/Program.cs:40-49`) — flicker and wasted work.
- `Accumulator` / `CalculateDpsHpsStats` re-scan and re-sort the whole window per line under a coarse static lock (`CombatLogsMonitor.cs:58-100`).

## Fragile Areas

- Index-arithmetic span parsing with hard-coded delimiter offsets throughout `Actor.cs`, `GameObject.cs`, `Value.cs`, `Threat.cs`. `Actor.GetName()` swallows all exceptions, masking format drift if SWTOR changes its log format.
- `ParserForm` (`Overlay/ParserForm.cs:24,135-138`) never disposes `_hpsDpsSubscription`; Rx `OnNext` mutates the bound `BindingList` on a background thread without `Control.Invoke` (only `Redraw` marshals to the UI thread).

## Dependencies at Risk

- `System.CommandLine.Rendering 0.4.0-alpha` is effectively abandoned; GA `System.CommandLine` reshaped the API. CLI rendering will need rework (e.g. migrate to Spectre.Console).
- Pre-release xUnit / Test SDK versions may become unrestorable as feeds are pruned.

## Missing / Coverage Gaps

- **No CI pipeline detected.** No GitHub Actions / Azure Pipelines config.
- **Windows-only** (WinForms + `user32.dll` P/Invoke) despite `DockerDefaultTargetOS=Linux` in `Cli.csproj` — the Docker target is misleading.
- **Test gaps (High):** `CombatLogsMonitor` lifecycle / Rx pipeline; DPS/HPS math (`CombatLogsMonitor.cs:70-100`).
- **Test gaps (Medium):** view-layer threading; parser edge cases (malformed lines, locale-formatted numbers/dates, delimiter characters inside names).
