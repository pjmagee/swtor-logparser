# Requirements: SWTOR Log Parser — Hardening Milestone

**Defined:** 2026-06-11
**Core Value:** The live DPS/HPS stats pipeline stays correct and reliable while the codebase becomes safe to maintain and extend — no regressions.

Each requirement corresponds to one or more items in `.planning/codebase/CONCERNS.md`. Categories: **BUG** (correctness), **RFCT** (refactor), **PERF** (performance), **DEP** (dependencies), **INFRA** (CI/build), **TEST** (coverage).

## v1 Requirements

### Correctness (BUG)

- [x] **BUG-01**: `CombatLogsMonitor.Stop()` actually cancels the monitor and reader worker tasks (worker tasks observe `_cancellationTokenSource.Token`, not the outer token) — `Monitor/CombatLogsMonitor.cs:107-126`
- [x] **BUG-02**: Calling `Stop()` before `Start()` does not throw (no NRE on unassigned `_cancellationTokenSource`) — `Monitor/CombatLogsMonitor.cs`
- [x] **BUG-03**: All `DateTime` and numeric parsing uses `InvariantCulture` so behavior is locale-independent — `Model/CombatLogLine.cs:9` and numeric parse sites
- [x] **BUG-04**: The `CombatLogs` static constructor tolerates settings filenames without `_` (no startup `TypeInitializationException`) — `Monitor/CombatLogs.cs:23`
- [x] **BUG-05**: Numeric parse paths (`int/long/ulong.Parse`) skip malformed lines instead of throwing — `Threat.cs:14`, `Actor.cs:64,73,93,100,107`, `Value.cs:47`, `GameObject.cs:75,87,95`
- [x] **BUG-06**: The shared parse caches are thread-safe — no `Dictionary.Add` races from the reader task — `Action.cs:53`, `GameObject.cs:108`, `Ability.cs:18`
- [x] **BUG-07**: Combat-log files are opened read-only (`FileAccess.Read`/`FileShare.Read`) — `Monitor/CombatLog.cs:24`

### Refactor (RFCT)

- [x] **RFCT-01**: The duplicated `View/` types (`Entry`, `SlidingExpirationList`) live in one shared location (core library), consumed by all three hosts — `*/View/*.cs`
- [x] **RFCT-02**: `CombatLogsMonitor` is constructible in any build configuration (no `#if RELEASE/#elif DEBUG` gap that leaves `Instance` undefined); construction prefers DI over a hard-coded singleton — `Monitor/CombatLogsMonitor.cs:15-20`
- [x] **RFCT-03**: The static caches use content-based keys (not `ReadOnlyMemory<char>.GetHashCode()`) and have bounded growth — `CombatLogs.cs:8-9`, `Action.cs:47-53`, `GameObject.cs:103-108`, `Ability.cs:15-18`

### Performance (PERF)

- [ ] **PERF-01**: `CombatLog` no longer re-parses the whole file just to count lines, and `GetLogLines()` avoids a `char[]` allocation per line — `Monitor/CombatLog.cs:16,28,33`
- [ ] **PERF-02**: The Native CLI renders incrementally instead of `Console.Clear()` + full redraw per event — `Native.Cli/Program.cs:40-49`
- [ ] **PERF-03**: The stats accumulator avoids re-scanning and re-sorting the entire window on every line — `CombatLogsMonitor.cs:58-100`

### Dependencies (DEP)

- [ ] **DEP-01**: All NuGet packages are on stable GA versions (no preview/alpha/beta) — all `*.csproj`
- [ ] **DEP-02**: Package versions are centrally managed via `Directory.Packages.props`
- [ ] **DEP-03**: The CLI rendering no longer depends on the abandoned `System.CommandLine.Rendering 0.4.0-alpha`; it uses a supported rendering approach — `SwtorLogParser.Cli`

### Infrastructure (INFRA)

- [ ] **INFRA-01**: A CI pipeline (GitHub Actions) builds the solution and runs the test suite on push/PR
- [ ] **INFRA-02**: The misleading `DockerDefaultTargetOS=Linux` is removed from the CLI projects — `*.csproj`

### Test Coverage (TEST)

- [x] **TEST-01**: Automated tests cover `CombatLogsMonitor` lifecycle (start/stop/cancellation) and the Rx pipeline
- [x] **TEST-02**: Automated tests cover the DPS/HPS math — `CombatLogsMonitor.cs:70-100`
- [x] **TEST-03**: Parser edge-case tests exist for malformed lines, locale-formatted numbers/dates, and delimiter characters inside names

## v2 Requirements

(None — deferred items are tracked as Out of Scope below.)

## Out of Scope

| Feature | Reason |
|---------|--------|
| Cross-platform / Linux support | App is intentionally Windows-only (WinForms + `user32.dll` P/Invoke + SWTOR client paths); the Docker target is being removed, not honored |
| New end-user features (new stats, new hosts, packaging) | This milestone is hardening only |
| Rewriting the span-based parser design | It works and is validated; only its safety/perf edges are in scope |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| BUG-01 | Phase 2 | Complete |
| BUG-02 | Phase 2 | Complete |
| BUG-03 | Phase 2 | Complete |
| BUG-04 | Phase 2 | Complete |
| BUG-05 | Phase 2 | Complete |
| BUG-06 | Phase 2 | Complete |
| BUG-07 | Phase 2 | Complete |
| RFCT-01 | Phase 3 | Complete |
| RFCT-02 | Phase 3 | Complete |
| RFCT-03 | Phase 3 | Complete |
| PERF-01 | Phase 4 | Pending |
| PERF-02 | Phase 4 | Pending |
| PERF-03 | Phase 4 | Pending |
| DEP-01 | Phase 5 | Pending |
| DEP-02 | Phase 5 | Pending |
| DEP-03 | Phase 5 | Pending |
| INFRA-01 | Phase 6 | Pending |
| INFRA-02 | Phase 5 | Pending |
| TEST-01 | Phase 3 | Complete |
| TEST-02 | Phase 3 | Complete |
| TEST-03 | Phase 1 | Complete |

**Coverage:**

- v1 requirements: 21 total
- Mapped to phases: 21
- Unmapped: 0 ✓

---
*Requirements defined: 2026-06-11*
*Last updated: 2026-06-11 after roadmap creation*
