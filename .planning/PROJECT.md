# SWTOR Log Parser — Hardening Milestone

## What This Is

A .NET 8 / C# parser for *Star Wars: The Old Republic* combat logs. A shared core library (`SwtorLogParser`) tails the game's log files, parses each line with zero-allocation span parsing, and exposes a live reactive stream of per-player DPS/HPS/APM statistics. Three consumer hosts render that stream: a managed CLI, a Native AOT CLI, and a transparent WinForms overlay. The current milestone is **hardening** — turning a working multi-prototype experiment into a correct, maintainable, CI-backed codebase by resolving every issue catalogued in `.planning/codebase/CONCERNS.md`.

## Core Value

The live DPS/HPS stats pipeline must stay correct and reliable while the codebase becomes safe to maintain and extend — no regressions to parsing or the reactive stream.

## Requirements

### Validated

<!-- Inferred from existing code (brownfield) — shipped and relied upon. -->

- ✓ Zero-allocation span parsing of SWTOR combat-log lines into a typed domain model (`SwtorLogParser/Model/*.cs`) — existing
- ✓ Live file monitoring that tails the newest combat log and parses new lines (`Monitor/CombatLogsMonitor.cs`, `Monitor/CombatLog.cs`) — existing
- ✓ Reactive DPS/HPS/APM stats pipeline exposed as `IObservable<PlayerStats>` (`CombatLogsMonitor.DpsHps`) — existing
- ✓ Three presentation hosts consuming the stream: managed CLI, Native AOT CLI, WinForms overlay — existing
- ✓ xUnit test suite covering the parser model types — existing

### Active

<!-- This milestone: resolve everything in CONCERNS.md. -->

**Correctness bugs**
- [ ] Fix cancellation-token mis-wiring so `Stop()` actually cancels the monitor worker tasks
- [ ] Make `Stop()` safe to call before `Start()` (no NRE on unassigned `_cancellationTokenSource`)
- [ ] Use `InvariantCulture` for all `DateTime`/number parsing (locale-independent)
- [ ] Harden the `CombatLogs` static constructor against settings filenames without `_` (no startup `TypeInitializationException`)
- [ ] Guard `int/long/ulong.Parse` paths against malformed lines (skip instead of throw)
- [ ] Make the shared parse caches thread-safe (no `Dictionary.Add` races from the reader task)
- [ ] Open combat-log files read-only instead of `ReadWrite`

**Refactors**
- [ ] De-duplicate the triplicated `View/` code (`Entry`, `SlidingExpirationList`) into the core library
- [ ] Replace the `#if RELEASE/#elif DEBUG` singleton with a safe construction path (prefer DI)
- [ ] Redesign the static caches: content-based keys, bounded growth, thread-safe

**Performance**
- [ ] Stop full-file re-parsing in `CombatLog.ToString()` and per-line `char[]` allocation in `GetLogLines()`
- [ ] Replace `Console.Clear()` full-redraw-per-event in the Native CLI with incremental rendering
- [ ] Avoid re-scanning/re-sorting the whole window per line in the stats accumulator

**Dependencies & infra**
- [ ] Move all preview/alpha/beta NuGet packages to stable GA versions; add central package management (`Directory.Packages.props`)
- [ ] Migrate off the abandoned `System.CommandLine.Rendering 0.4.0-alpha` to a supported rendering approach
- [ ] Add a CI pipeline (GitHub Actions) that builds the solution and runs tests
- [ ] Remove the misleading `DockerDefaultTargetOS=Linux` (this is a Windows-only app)
- [ ] Add tests for the currently-untested monitor lifecycle, Rx pipeline, and DPS/HPS math

### Out of Scope

- Cross-platform / Linux support — the app is intentionally Windows-only (WinForms + `user32.dll` P/Invoke + SWTOR client paths); the Docker target is being *removed*, not honored
- New end-user features (new stats, new hosts, packaging/distribution) — this milestone is hardening only
- Rewriting the parser's span-based design — it works and is validated; only its safety/perf edges are in scope

## Context

- **Brownfield.** Full codebase map exists at `.planning/codebase/` (STACK, ARCHITECTURE, STRUCTURE, CONVENTIONS, TESTING, INTEGRATIONS, CONCERNS). The concerns document is the authoritative source for this milestone's requirements.
- **Local-only tool.** No network, database, auth, or secrets. Reads combat logs from `My Documents\Star Wars - The Old Republic\CombatLogs` and settings from `%LocalAppData%\SWTOR\...`. The only native dependency is `user32.dll` P/Invoke in the overlay.
- **Risk hotspot.** Several concerns touch the shared `CombatLogsMonitor` singleton and static caches that all three hosts depend on; changes there must preserve the live stream's behavior.
- **Dependency fragility.** Nearly every NuGet package is pinned to a preview/alpha/beta version with no lockfile or `global.json` — GA upgrades are a primary goal but `System.CommandLine` GA reshaped its API and will require CLI rework.

## Constraints

- **Tech stack**: .NET 8, C#, Rx.NET (`System.Reactive`), WinForms, xUnit — established; stay on .NET 8.
- **Compatibility**: Windows-only is acceptable and intended; do not add cross-platform burden.
- **AOT**: `SwtorLogParser.Native.Cli` uses Native AOT and the core library is `IsAotCompatible=true` — refactors (esp. DI) must not break AOT compatibility (no reflection-heavy patterns in the core library).
- **No regressions**: the parser model and the live DPS/HPS stream must behave identically after hardening; new tests should lock in current correct behavior before refactors.

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Scope this milestone as "resolve all CONCERNS.md items" | User directive; concerns are concrete and well-cited | — Pending |
| Upgrade preview/alpha deps to GA, including off `System.CommandLine.Rendering` alpha | Fragile restores, missed patches, abandoned alpha API | — Pending |
| Add a GitHub Actions CI pipeline (build + test) | No CI exists; needed to protect against regressions during hardening | — Pending |
| Remove `DockerDefaultTargetOS=Linux` rather than pursue cross-platform | App is Windows-only by design; the Docker target is misleading | — Pending |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd-transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd-complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-06-11 after initialization*
