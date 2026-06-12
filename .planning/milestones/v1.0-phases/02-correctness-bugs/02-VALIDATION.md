---
phase: 2
slug: correctness-bugs
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-06-11
---

# Phase 2 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.5.0-pre.44 (.NET 8) |
| **Config file** | none — existing `SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` |
| **Quick run command** | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` |
| **Full suite command** | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` |
| **Estimated runtime** | ~10-20 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test`. Each bug fix lands WITH its flipped/added test in the same commit — the suite MUST be green after every commit.
- **Before phase verification:** Full suite green with zero skips.
- **Max feedback latency:** ~20 seconds.

---

## Per-Task Verification Map

| Task | Bug | Test that flips/adds | Test Type | Command | Status |
|------|-----|----------------------|-----------|---------|--------|
| guard numeric parse | BUG-05 | 6 numeric characterization tests flip Throws→Null | unit | `dotnet test` | ⬜ |
| InvariantCulture date | BUG-03 | timestamp characterization flips Throws→Null | unit | `dotnet test` | ⬜ |
| ConcurrentDictionary caches | BUG-06 | golden parses stay green; add concurrency sanity | unit | `dotnet test` | ⬜ |
| static-ctor split guard | BUG-04 | add: filename without `_` does not throw | unit | `dotnet test` | ⬜ |
| read-only file open | BUG-07 | covered by build + existing behavior | unit/manual | `dotnet test` | ⬜ |
| cancellation wiring + Stop guard | BUG-01/02 | add where testable (DEBUG Instance) else defer Phase 3 | unit | `dotnet test` | ⬜ |

---

## Wave 0 Requirements

- Existing infrastructure covers all phase requirements (xUnit project + Phase 1 tests already present). No framework install needed.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Live tailing not blocked after read-only file open | BUG-07 | Requires the running SWTOR client writing the log concurrently | With game running and writing combat log, start the monitor; confirm new lines still appear and the game is not blocked (validates `FileShare.ReadWrite` kept). |
| Stop() actually halts worker tasks | BUG-01 | Full lifecycle test needs the Phase 3 DI refactor for clean injection | Start monitor, call Stop(), confirm CPU/file activity ceases (or covered by a Phase 3 lifecycle test). |

---

## Validation Sign-Off

- [ ] Every bug fix commit leaves `dotnet test` green, zero skips
- [ ] Flipped characterization tests assert null/skip (not throw)
- [ ] static-ctor guard test added
- [ ] Manual checks recorded for BUG-07 live-tailing and BUG-01 Stop()
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
