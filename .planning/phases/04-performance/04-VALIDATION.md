---
phase: 4
slug: performance
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-06-12
---

# Phase 4 — Validation Strategy

> Per-phase validation contract. These are PURE perf refactors — output must be identical.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.5.0-pre.44 (.NET 8) |
| **Quick/Full run** | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` |
| **Build-all** | `dotnet build SwtorLogParser.slnx -c Debug` |
| **Estimated runtime** | ~10-20 seconds |

---

## Sampling Rate

- **After every task commit:** `dotnet test` green, zero skips (102 baseline + new Wave-0 tests). Output identity is the contract.
- **PERF-03:** the existing `DpsHpsMathTests` MUST still pass unchanged (DPS/HPS/crit%/window numbers identical).
- **PERF-02:** code-review + manual flicker check only (no automated console test — called out so the verifier does not expect one).

---

## Wave 0 Requirements (test gaps to add BEFORE optimizing)

- [ ] PERF-01 read/slice test: `GetLogLines()` over a known multi-line string yields the same `CombatLogLine`s as before (and no `char[]`-per-line).
- [ ] `EnumerateLines` parity test: the new offset-tracking splitter produces the SAME lines as `MemoryExtensions.EnumerateLines` over a mixed-terminator (LF/CRLF) fixture.
- [ ] Explicit `count <= 1 ⇒ timeSpan == 1s` test for `CalculateDpsHpsStats` (locks the edge the single-pass rewrite must preserve).

---

## Per-Task Verification Map

| Task | Req | Proves | Test/Check | Status |
|------|-----|--------|-----------|--------|
| Offset-splitter + zero-copy slices | PERF-01 | no per-line char[]; same lines; ToString count without parse | parity + read/slice tests | ⬜ |
| Native CLI cursor render | PERF-02 | no Console.Clear; rows overwritten in place | code review + manual flicker | ⬜ |
| Single-pass CalculateDpsHpsStats | PERF-03 | identical DPS/HPS/crit%/window | DpsHpsMathTests unchanged | ⬜ |

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Native CLI renders without flicker | PERF-02 | Console rendering not unit-testable | Run `dotnet run --project SwtorLogParser.Native.Cli -- monitor`; confirm rows update in place with no full-screen blink. |

---

## Validation Sign-Off

- [ ] Wave-0 parity/edge tests added and green before each optimization
- [ ] `DpsHpsMathTests` pass UNCHANGED (PERF-03 output identical)
- [ ] No per-line `char[]` in `GetLogLines`; `ReadOnlyMemory<char>` zero-copy preserved
- [ ] No `Console.Clear()` in the Native CLI `monitor` render path
- [ ] `dotnet test` green (zero skips), `dotnet build SwtorLogParser.slnx` succeeds
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
