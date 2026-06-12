# SWTOR Log Parser

## What This Is

A .NET 10 / C# parser for *Star Wars: The Old Republic* combat logs. A shared core library (`SwtorLogParser`) tails the game's log files, parses each line with zero-allocation span parsing, and exposes a live reactive stream of per-player DPS/HPS/APM statistics. Three consumer hosts render that stream: a managed CLI, a Native AOT CLI, and a transparent overlay. The v1.0 milestone hardened the codebase (correctness, GA deps, CI, .NET 10); the current milestone (**v1.1**) modernizes the overlay (WinForms → WinUI 3) and the dev/test toolchain.

## Core Value

The live DPS/HPS stats pipeline must stay correct and reliable while the codebase becomes safe to maintain and extend — no regressions to parsing or the reactive stream.

## Current Milestone: v1.1 WinUI 3 Overlay & Dev Tooling

**Goal:** Replace the WinForms overlay with a modern WinUI 3 overlay (CsWin32 interop, fixed topmost-over-borderless), and modernize the toolchain (MSTest SDK, VSCode launch/tasks, refreshed docs) — without touching the core parser or the live DPS/HPS stream.

**Target features:**
- WinUI 3 transparent click-through overlay at parity with WinForms (drag, transparency, live DPS/HPS render), then retire the WinForms host
- Win32 interop via Microsoft.Windows.CsWin32 source generator (replaces hand-written `NativeMethods`)
- Overlay stays on top of borderless/windowed SWTOR (BL-01 fix, carried into WinUI 3)
- Migrate the 106-test suite from xUnit to the new MSTest .NET SDK
- VSCode `launch.json` (debug every host) + `tasks.json` (build / test / AOT-publish)
- Docs refresh (README/docs: WinUI 3 overlay, .NET 10, run/debug story)

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

<!-- v1.1 WinUI 3 Overlay & Dev Tooling — see REQUIREMENTS.md for REQ-IDs. -->

- [ ] WinUI 3 overlay at parity with WinForms (transparent, click-through, drag, live DPS/HPS), then WinForms retired
- [ ] Win32 interop via Microsoft.Windows.CsWin32 source generator (replaces `NativeMethods`) — closes #3
- [ ] Overlay stays on top of borderless/windowed SWTOR (BL-01) — carried into WinUI 3
- [ ] Test suite migrated from xUnit to the MSTest .NET SDK — closes #2
- [ ] VSCode `launch.json` (all hosts) + `tasks.json` (build / test / AOT-publish)
- [ ] Docs refresh (WinUI 3 overlay, .NET 10, run/debug story)

### Out of Scope

- Cross-platform / Linux support — the app is intentionally Windows-only (Win32 P/Invoke + SWTOR client paths); WinUI 3 reinforces the Windows-only stance
- New end-user features (new stats, new metrics, packaging/distribution) — v1.1 is overlay re-implementation + tooling, not new product capability
- Rewriting the parser's span-based design or changing the live DPS/HPS stream — it works and is validated; v1.1 swaps the overlay *host* only, core is untouched
- WinUI 3 overlay on Native AOT — AOT applies only to `Native.Cli`; the overlay stays a normal managed host

## Context

- **Brownfield.** Full codebase map exists at `.planning/codebase/` (STACK, ARCHITECTURE, STRUCTURE, CONVENTIONS, TESTING, INTEGRATIONS, CONCERNS). The concerns document is the authoritative source for this milestone's requirements.
- **Local-only tool.** No network, database, auth, or secrets. Reads combat logs from `My Documents\Star Wars - The Old Republic\CombatLogs` and settings from `%LocalAppData%\SWTOR\...`. The only native dependency is `user32.dll` P/Invoke in the overlay.
- **Risk hotspot.** Several concerns touch the shared `CombatLogsMonitor` singleton and static caches that all three hosts depend on; changes there must preserve the live stream's behavior.
- **Dependency fragility.** Nearly every NuGet package is pinned to a preview/alpha/beta version with no lockfile or `global.json` — GA upgrades are a primary goal but `System.CommandLine` GA reshaped its API and will require CLI rework.

## Constraints

- **Tech stack**: .NET 10 (LTS), C#, Rx.NET (`System.Reactive`), **WinUI 3 / Windows App SDK** (overlay, replacing WinForms in v1.1), **MSTest .NET SDK** (replacing xUnit in v1.1), Spectre.Console (managed CLI), Microsoft.Windows.CsWin32 (interop), central package management.
- **Compatibility**: Windows-only is acceptable and intended; do not add cross-platform burden. WinUI 3 reinforces this.
- **AOT**: `SwtorLogParser.Native.Cli` uses Native AOT and the core library is `IsAotCompatible=true` — refactors must not break AOT compatibility (no reflection-heavy patterns in the core library). The WinUI 3 overlay is a normal managed host and is **not** AOT-constrained.
- **No regressions**: the parser model and the live DPS/HPS stream must behave identically after the overlay/test-framework swap; v1.1 changes hosts and tooling, never the core.

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Scope this milestone as "resolve all CONCERNS.md items" | User directive; concerns are concrete and well-cited | ✓ Good — all 22 reqs shipped v1.0 |
| Upgrade preview/alpha deps to GA, including off `System.CommandLine.Rendering` alpha | Fragile restores, missed patches, abandoned alpha API | ✓ Good — GA + CPM; replaced with Spectre.Console |
| Add a GitHub Actions CI pipeline (build + test) | No CI exists; needed to protect against regressions during hardening | ✓ Good — green on main, incl. AOT publish |
| Remove `DockerDefaultTargetOS=Linux` rather than pursue cross-platform | App is Windows-only by design; the Docker target is misleading | ✓ Good |
| Drop System.CommandLine entirely (hand-rolled dispatch + Spectre.Console table) | No GA at decision time; Rendering abandoned; 2-command surface is trivial + AOT-safe | ✓ Good |
| Upgrade to .NET 10 LTS mid-milestone (issue #1) | User directive ("ASAP"); native `.slnx`/single-SDK; LTS; simplified CI | ✓ Good — AOT-clean, 106 tests green |
| v1.1: replace WinForms overlay with **WinUI 3** (not MAUI) | User directive (issue #4); modern Windows-native UI; MAUI's better AOT story is moot since the overlay isn't AOT-constrained | — Pending |
| v1.1: straight replace — delete WinForms once WinUI 3 reaches parity | One overlay host to maintain; sequence build-before-delete so there's never a window without a working overlay | — Pending |
| v1.1: migrate xUnit → **MSTest .NET SDK** (issue #2) | Modern single-SDK test project; user directive | — Pending |
| v1.1: adopt **Microsoft.Windows.CsWin32** for Win32 interop (issue #3) | Source-generated, type-safe P/Invoke replaces hand-written `NativeMethods`; lands with the overlay rewrite | — Pending |

---

## Current State (v1.0 shipped 2026-06-12; v1.1 in progress)

The SWTOR log parser is hardened and modernized: .NET 10 LTS, 106-test suite, GitHub Actions CI green on `main` (build + test + Native AOT publish), all CONCERNS.md items resolved. **v1.1 in progress:** replacing the WinForms overlay with a WinUI 3 overlay (CsWin32 interop, BL-01 topmost fix), migrating tests xUnit→MSTest, adding VSCode launch/tasks, and refreshing docs. Core parser and live DPS/HPS stream remain frozen.

**Phase 8 complete (2026-06-12):** new `SwtorLogParser.Overlay.WinUi` project scaffolded — unpackaged self-contained WinUI 3, opens an empty window, launches from the published `.exe` with no runtime install (human-verified). `Microsoft.WindowsAppSDK` 2.2.0 + `Microsoft.Windows.CsWin32` 0.3.275 pinned in CPM, isolated to the overlay (core/Native-CLI AOT graph clean). WinForms overlay untouched (parity safety net). OVL-01 validated.

**Phase 9 complete (2026-06-12):** the WinUI overlay now renders the **live DPS/HPS stream** — `MainViewModel` subscribes to `DpsHps`, feeds the reused core `SlidingExpirationList` off-thread, and a 1s `DispatcherQueueTimer` mirrors a DPS-descending snapshot into a bound `ListView` (no cross-thread crash — human-verified launch). WinForms-parity +/- font buttons + corruption-safe settings persistence (window position+size+font → `%LocalAppData%\SwtorLogParser\settings.json`; restore-across-restart human-verified). Code-review blocker CR-01 (unguarded null `Player.Id` could fault the stream) fixed host-side. Core frozen (`View/*` byte-identical), tests 121/121. OVL-02 ✓, OVL-08 ✓, OVL-07 partial (position+size; **opacity persistence → Phase 10**). Live-rows-with-real-combat visual deferred to the Phase 11 parity gate.

**Overlay PIVOT — WinUI 3 → Dear ImGui (2026-06-12):** Phase 10 hit WinUI 3's hard limit — it cannot do **clear per-pixel transparency** (`WS_EX_LAYERED` blanks the compositor → invisible window; only frosted acrylic is see-through). The overlay was pivoted to an immediate-mode **Silk.NET/OpenGL + Dear ImGui** host (`SwtorLogParser.Overlay.ImGui`) with a GLFW `TransparentFramebuffer` — genuine clear see-through, the right tool for a game overlay. **Shipped:** clear transparent borderless always-on-top overlay; live DPS/HPS table; CsWin32 interop (topmost re-assert/BL-01, no-activate/tool-window); **auto-detects + pins over the SWTOR window and follows it** (polls, resilient); manual drag; opacity/font controls + persistence; and a **mini combat-log** widget (human-readable ability feed for streamers, from the core `CombatLogChanged` event). Both predecessor overlays (WinForms **and** the WinUI 3 detour) deleted — ImGui is the sole overlay (OVL-09). VSCode launch/tasks + README refreshed. **Deferred:** click-through (OVL-06 — `WS_EX_TRANSPARENT` insufficient on GLFW; removed per user), and the xUnit→MSTest migration (TEST-01/02 — ~328 assertions, deferred to avoid churn against parallel test work). Core stayed frozen for the overlay (a separately-approved parallel quick task touched core for an absorb-DPS correctness fix). `.slnx` builds Release green; 116/116 tests pass.

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
*Last updated: 2026-06-12 — overlay pivoted WinUI 3 → Dear ImGui (clear transparency); old overlays retired; VSCode+docs done; click-through + MSTest deferred*
