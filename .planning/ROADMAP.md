# Roadmap: SWTOR Log Parser — Hardening Milestone

## Overview

This milestone turns a working multi-prototype experiment into a correct, maintainable, CI-backed codebase by resolving every concern catalogued in CONCERNS.md. The sequence is safety-net first (parser behavior locks), then correctness fixes, then refactors protected by that safety net, then performance tuning, then dependency upgrades (including the CommandLine rendering rework), and finally a CI pipeline that guards against regressions going forward.

## Phases

**Phase Numbering:**

- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [ ] **Phase 1: Parser Safety Net** - Lock in correct parse behavior with edge-case tests before anything else changes
- [ ] **Phase 2: Correctness Bugs** - Fix all seven bug-class defects (cancellation, NRE, locale, static-ctor crash, malformed-line throws, cache races, wrong file-access mode)
- [ ] **Phase 3: Monitor Refactor + Coverage** - De-duplicate view types, replace singleton construction, redesign static caches, and add monitor/Rx/math tests
- [ ] **Phase 4: Performance** - Eliminate the re-parse in ToString, per-line char[] alloc, Console.Clear flicker, and full-window re-sort per line
- [ ] **Phase 5: Dependency Upgrades** - Move all packages to stable GA, add central package management, migrate off the abandoned CommandLine.Rendering alpha
- [ ] **Phase 6: CI Pipeline** - Add GitHub Actions build + test pipeline and remove the misleading DockerDefaultTargetOS=Linux

## Phase Details

### Phase 1: Parser Safety Net

**Goal**: Existing parse behavior is locked in by automated tests for edge cases, so every subsequent change can be verified to produce no regressions
**Depends on**: Nothing (first phase)
**Requirements**: TEST-03
**Success Criteria** (what must be TRUE):

  1. The test suite contains xUnit tests that feed malformed lines (missing fields, truncated tokens) to every model Parse factory and assert null or graceful skip — no unhandled exception escapes
  2. Tests exist for locale-sensitive inputs: timestamps and numeric fields formatted with a non-invariant culture are either parsed correctly via InvariantCulture or rejected cleanly
  3. Tests exist for delimiter edge cases: actor/ability/game-object names containing `[`, `]`, `{`, `}`, `@`, or `:` are handled without index-out-of-range errors
  4. All new tests pass on a clean `dotnet test` run with no skips

**Plans**: 3 plans (all Wave 1 — parallel; no file overlap)

- [x] 01-01-PLAN.md — EAGER models: GameObject + CombatLogLine (Assert.Throws characterization, brace/delimiter [Theory], timestamp locale, golden lines)
- [ ] 01-02-PLAN.md — LAZY models: Actor + Threat + Value (property-throw characterization, delimiter [Theory], position locale, guard-null [Theory], golden lines)
- [ ] 01-03-PLAN.md — Inheriting/guarded models: Ability (LAZY .Id throw) + Action (graceful-null [Theory], golden lines)

**Cross-cutting constraints:**

- dotnet test runs fully GREEN with zero skips

### Phase 2: Correctness Bugs

**Goal**: The monitor starts, runs, and stops correctly in all conditions; no parse path throws on malformed or locale-variant input; shared caches cannot be corrupted; log files are opened safely
**Depends on**: Phase 1
**Requirements**: BUG-01, BUG-02, BUG-03, BUG-04, BUG-05, BUG-06, BUG-07
**Success Criteria** (what must be TRUE):

  1. Calling `Stop()` after `Start()` causes the monitor and reader worker tasks to observe cancellation and exit; the process does not hang
  2. Calling `Stop()` before `Start()` returns without throwing a NullReferenceException
  3. The application starts without a TypeInitializationException even when a PlayerGUIState settings filename contains no underscore
  4. Feeding a malformed line (missing numeric token, truncated record) to any model Parse factory results in a null return or skipped line, never an unhandled exception during normal log tailing
  5. Log files are opened with FileAccess.Read so the game client is never blocked from writing

**Plans**: TBD

### Phase 3: Monitor Refactor + Coverage

**Goal**: The shared CombatLogsMonitor is constructible in all build configurations and testable via DI; view-layer types are deduplicated into the core library; static caches are content-keyed, bounded, and thread-safe; monitor lifecycle, Rx pipeline, and DPS/HPS math have automated test coverage
**Depends on**: Phase 2
**Requirements**: RFCT-01, RFCT-02, RFCT-03, TEST-01, TEST-02
**Success Criteria** (what must be TRUE):

  1. `Entry` and `SlidingExpirationList` exist in exactly one location (the core library); all three host projects reference that single copy and the duplicated per-host View/ files are removed
  2. `CombatLogsMonitor.Instance` is defined (not undefined) regardless of build configuration; an alternative DI-friendly construction path exists that does not rely on the preprocessor singleton
  3. The static parse caches use string or span-to-string content keys (not ReadOnlyMemory GetHashCode), are bounded so they cannot grow without limit, and are written under a lock or via a concurrent collection safe for concurrent reader-task access
  4. Automated tests cover monitor Start/Stop lifecycle transitions (including the cancellation wiring fixed in Phase 2) and assert that the Rx Subject receives lines after Start and stops receiving them after Stop
  5. Automated tests verify DPS/HPS arithmetic against known inputs and sliding-window expiry behavior

**Plans**: TBD

### Phase 4: Performance

**Goal**: The live stats pipeline avoids wasteful full-file re-parses, per-line heap allocations, and full-window re-sorts; the Native CLI renders without full-screen flicker
**Depends on**: Phase 3
**Requirements**: PERF-01, PERF-02, PERF-03
**Success Criteria** (what must be TRUE):

  1. `CombatLog.ToString()` reports line count without re-parsing the entire file; `GetLogLines()` does not allocate a char[] per line (ReadOnlyMemory<char> zero-copy intent is preserved end-to-end)
  2. The Native CLI host renders updates without calling `Console.Clear()` per event — existing displayed rows are updated or overwritten in-place, eliminating the full-screen repaint flicker
  3. The stats accumulator's sliding-window update does not re-sort or re-scan the entire window collection on every incoming line; incremental maintenance replaces the full-scan loop

**Plans**: TBD

### Phase 5: Dependency Upgrades

**Goal**: Every NuGet package is on a stable GA release managed centrally; the CLI host no longer depends on the abandoned System.CommandLine.Rendering 0.4.0-alpha
**Depends on**: Phase 1
**Requirements**: DEP-01, DEP-02, DEP-03, INFRA-02
**Success Criteria** (what must be TRUE):

  1. `Directory.Packages.props` exists at solution root and all project files reference it for package versions — no per-csproj version attributes remain for centrally managed packages
  2. No package in any csproj is pinned to a preview, alpha, or beta version; `dotnet restore` succeeds with only GA feeds
  3. `SwtorLogParser.Cli` renders its live table without referencing `System.CommandLine.Rendering`; the rendering approach compiles and produces visible output (e.g. Spectre.Console or equivalent)
  4. `DockerDefaultTargetOS=Linux` is absent from all project files; no misleading cross-platform properties remain

**Plans**: TBD

### Phase 6: CI Pipeline

**Goal**: Every push and pull request is automatically built and tested by a GitHub Actions workflow; the build is green on the main branch
**Depends on**: Phase 5
**Requirements**: INFRA-01
**Success Criteria** (what must be TRUE):

  1. A `.github/workflows/` YAML file exists that triggers on push and pull_request to main, restores packages, builds the full solution, and runs `dotnet test`
  2. The workflow completes successfully (green) on the current main branch state — no red CI on delivery
  3. A test failure in any project causes the CI run to fail (non-zero exit), providing a regression gate for all future changes

**Plans**: TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 1 → 2 → 3 → 4 → 5 → 6
Note: Phase 5 depends on Phase 1 (not Phase 4) so it can run in parallel with Phases 2-4 if desired; Phase 6 depends on Phase 5 being green.

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Parser Safety Net | 1/3 | In Progress|  |
| 2. Correctness Bugs | 0/? | Not started | - |
| 3. Monitor Refactor + Coverage | 0/? | Not started | - |
| 4. Performance | 0/? | Not started | - |
| 5. Dependency Upgrades | 0/? | Not started | - |
| 6. CI Pipeline | 0/? | Not started | - |
