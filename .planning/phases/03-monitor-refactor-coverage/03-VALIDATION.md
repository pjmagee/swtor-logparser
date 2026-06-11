---
phase: 3
slug: monitor-refactor-coverage
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-06-11
---

# Phase 3 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.5.0-pre.44 (.NET 8) |
| **Config file** | none — existing `SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` |
| **Quick run command** | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` |
| **Full suite command** | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` |
| **Build-all (AOT/host check)** | `dotnet build SwtorLogParser.slnx -c Debug` |
| **Estimated runtime** | ~10-20 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test`. Refactor commits must each leave the suite green (77 baseline, growing with TEST-01/02).
- **After RFCT-01 (view dedup) and RFCT-02:** also `dotnet build SwtorLogParser.slnx` to confirm all 3 hosts still compile and the core lib stays AOT-clean (no WinForms leak).
- **Before phase verification:** full suite green, zero skips; solution builds.
- **Max feedback latency:** ~20 seconds (tests), ~a few seconds (build).

---

## Per-Task Verification Map

| Task | Requirement | What it proves | Test/Build | Status |
|------|-------------|----------------|-----------|--------|
| Instance in all configs + public ctor | RFCT-02 | `Instance` defined; DI ctor usable from tests | `dotnet build` + new ctor test | ⬜ |
| Per-type bounded content-keyed caches | RFCT-03 | no Ability/GameObject cast collision; bounded; thread-safe | unit + concurrency test | ⬜ |
| Move Entry/SlidingExpirationList to lib | RFCT-01 | one shared copy; hosts reference it; core lib has no WinForms | `dotnet build` all hosts | ⬜ |
| Monitor lifecycle / Rx tests | TEST-01 | Start delivers, Stop halts (cancellation) | `dotnet test` | ⬜ |
| DPS/HPS math + window expiry | TEST-02 | accumulator/CalculateDpsHpsStats correct | `dotnet test` | ⬜ |
| Abstract CombatLogs filesystem | TEST-01/02 | `All_Logs_Are_Not_Null`/`Player_Is_Local_Is_True` hermetic | `dotnet test` (CI-safe) | ⬜ |

---

## Wave 0 Requirements

- Existing infrastructure (xUnit project + `InternalsVisibleTo(SwtorLogParser.Tests)` from Phase 2) covers the test seam. No new framework/package needed (bounded cache implemented in-repo; BitFaster deferred to Phase 5).

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| All 3 hosts still run after view dedup | RFCT-01 | Full host launch needs the SWTOR client for live data | Build + smoke-run CLI `list`; optionally launch Overlay to confirm the shared list still renders. |

---

## Validation Sign-Off

- [ ] Every refactor commit leaves `dotnet test` green, zero skips
- [ ] `dotnet build SwtorLogParser.slnx` succeeds (all 3 hosts) after RFCT-01/02
- [ ] Core library contains NO WinForms types (AOT preserved)
- [ ] TEST-01/02 deterministic (no DateTime.Now flakiness; accumulator tested directly)
- [ ] Deferred filesystem tests now hermetic (CI-safe for Phase 6)
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
