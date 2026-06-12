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

### Validated (v1.0 Hardening — shipped 2026-06-12)

**Correctness bugs (BUG-01..07)**
- ✓ `Stop()` cancels the monitor/reader worker tasks via linked token — v1.0
- ✓ `Stop()` before `Start()` is a safe no-op — v1.0
- ✓ `InvariantCulture` `TryParseExact` timestamp + `TryParse` numeric guards — v1.0
- ✓ `CombatLogs` static ctor tolerates filenames without `_` — v1.0
- ✓ Malformed numeric lines skip (null) instead of throwing — v1.0
- ✓ Parse caches thread-safe (ConcurrentDictionary, first-writer-wins) — v1.0
- ✓ Combat-log files opened read-only (`FileAccess.Read` + `FileShare.ReadWrite`) — v1.0
- ✓ **(UAT bonus)** Combat logs decoded as Latin-1 — accented player names no longer corrupted — v1.0

**Refactors (RFCT-01..03)**
- ✓ `Entry`/`SlidingExpirationList` deduplicated into core `SwtorLogParser.View` (Overlay composes it) — v1.0
- ✓ `CombatLogsMonitor` constructible in all configs + public DI ctor (no `#if` gap) — v1.0
- ✓ Static caches: per-type, content-keyed (`rom.ToString()`), bounded — v1.0

**Performance (PERF-01..03)**
- ✓ Zero-copy line slicing + parse-free `ToString()` count — v1.0
- ✓ Native CLI in-place render (no `Console.Clear()` flicker); managed CLI flicker-free Spectre.Console `Live` — v1.0
- ✓ Single-pass `CalculateDpsHpsStats` (no per-line re-sort) — v1.0

**Dependencies, platform & infra**
- ✓ All NuGet packages on stable GA via `Directory.Packages.props` — v1.0
- ✓ `System.CommandLine`(+Rendering) removed → hand-rolled dispatch + Spectre.Console — v1.0
- ✓ GitHub Actions CI (build + test + AOT publish), green on `main` — v1.0
- ✓ `DockerDefaultTargetOS=Linux` removed — v1.0
- ✓ Monitor lifecycle, Rx pipeline, and DPS/HPS math tests (106-test suite) — v1.0
- ✓ **All projects on .NET 10 (LTS)**, AOT-clean (issue #1) — v1.0

### Active

(Next milestone not yet scoped — see `BACKLOG.md` and GitHub issues #2/#3/#4. Candidate clusters: Overlay/UI modernization, tooling, docs refresh.)

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

- **Tech stack**: .NET 10 (LTS, upgraded from .NET 8 in v1.0), C#, Rx.NET (`System.Reactive`), WinForms, xUnit, Spectre.Console (managed CLI), central package management.
- **Compatibility**: Windows-only is acceptable and intended; do not add cross-platform burden.
- **AOT**: `SwtorLogParser.Native.Cli` uses Native AOT and the core library is `IsAotCompatible=true` — refactors (esp. DI) must not break AOT compatibility (no reflection-heavy patterns in the core library).
- **No regressions**: the parser model and the live DPS/HPS stream must behave identically after hardening; new tests should lock in current correct behavior before refactors.

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Scope this milestone as "resolve all CONCERNS.md items" | User directive; concerns are concrete and well-cited | ✓ Good — all 22 reqs shipped v1.0 |
| Upgrade preview/alpha deps to GA, including off `System.CommandLine.Rendering` alpha | Fragile restores, missed patches, abandoned alpha API | ✓ Good — GA + CPM; replaced with Spectre.Console |
| Add a GitHub Actions CI pipeline (build + test) | No CI exists; needed to protect against regressions during hardening | ✓ Good — green on main, incl. AOT publish |
| Remove `DockerDefaultTargetOS=Linux` rather than pursue cross-platform | App is Windows-only by design; the Docker target is misleading | ✓ Good |
| Drop System.CommandLine entirely (hand-rolled dispatch + Spectre.Console table) | No GA at decision time; Rendering abandoned; 2-command surface is trivial + AOT-safe | ✓ Good |
| Upgrade to .NET 10 LTS mid-milestone (issue #1) | User directive ("ASAP"); native `.slnx`/single-SDK; LTS; simplified CI | ✓ Good — AOT-clean, 106 tests green |

---

## Current State (v1.0 shipped 2026-06-12)

The SWTOR log parser is hardened and modernized: .NET 10 LTS, 106-test suite, GitHub Actions CI green on `main` (build + test + Native AOT publish), all CONCERNS.md items resolved. Live overlay validated end-to-end against real combat logs. Deferred/next: overlay topmost (BL-01), CsWin32 interop (#3), lightweight UI alternative (#4), xUnit→MSTest (#2), docs refresh.

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
*Last updated: 2026-06-12 after v1.0 Hardening milestone*
