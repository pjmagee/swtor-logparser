---
phase: 1
slug: parser-safety-net
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-06-11
---

# Phase 1 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.5.0-pre.44 (.NET 8) |
| **Config file** | none — existing `SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` |
| **Quick run command** | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj --filter "FullyQualifiedName~Tests"` |
| **Full suite command** | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` |
| **Estimated runtime** | ~10-20 seconds |

---

## Sampling Rate

- **After every task commit:** Run the full suite (`dotnet test`) — the suite is small and fast; no need for a narrower quick run.
- **After every plan wave:** Run `dotnet test`.
- **Before `/gsd-verify-work`:** Full suite must be green with NO skips.
- **Max feedback latency:** ~20 seconds.

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 1-01-xx | 01 | 1 | TEST-03 | — / — | N/A (pure parser tests, no attack surface) | unit | `dotnet test` | ✅ existing project | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- Existing infrastructure covers all phase requirements. The `SwtorLogParser.Tests` project, xUnit, and `GlobalUsings.cs` already exist — no framework install or new test project needed.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| — | — | — | — |

*All phase behaviors have automated verification (`dotnet test`).*

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify (`dotnet test`)
- [ ] Sampling continuity: every task is verifiable by the full suite
- [ ] Wave 0 covers all MISSING references (none — existing infra)
- [ ] No watch-mode flags
- [ ] Feedback latency < 20s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
