---
phase: 6
slug: dotnet-10-upgrade
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-06-12
---

# Phase 6 — Validation Strategy

> Mechanical framework upgrade (net8.0 → net10.0). Behavior preserved; suite green; AOT-clean.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 on the net10.0 test host |
| **Restore** | `dotnet restore SwtorLogParser.slnx` |
| **Build-all** | `dotnet build SwtorLogParser.slnx -c Release` |
| **Test** | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` (106 baseline) |
| **AOT compile** | `dotnet publish SwtorLogParser.Native.Cli -c Release` (code-gen must be IL-warning-free; MSVC link may be unavailable locally → Phase 7 CI) |

---

## Sampling Rate

- **After the TFM + package bump commit:** `dotnet restore` + `dotnet build SwtorLogParser.slnx` succeed on net10.0; `dotnet test` green (106, zero skips).
- **After all changes:** AOT compile is IL2xxx/IL3xxx-clean.
- No package is preview/alpha/beta.

---

## Wave 0 Requirements

- None — the existing 106 tests ARE the regression contract; they must pass unchanged on net10.0 (a passing suite on the new TFM is the proof of a behavior-preserving upgrade). No new tests needed for a pure TFM bump.

---

## Per-Task Verification Map

| Task | Req | Proves | Check | Status |
|------|-----|--------|-------|--------|
| Bump 5 TFMs + Logging.Abstractions 10.0.9 + drop LangVersion=preview | PLAT-01 | targets net10.0; GA packages; builds | `dotnet restore`+`build .slnx`; grep no net8.0 | ⬜ |
| Re-verify tests on net10 | PLAT-01 | behavior preserved | `dotnet test` 106 green | ⬜ |
| Re-verify AOT | PLAT-01 | AOT-clean on net10 | `dotnet publish Native.Cli` IL-warning-free | ⬜ |

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Native AOT full link on net10 | PLAT-01 | MSVC linker may be absent on dev machine | Phase 7 CI (windows-latest) runs `dotnet publish ... PublishAot` end-to-end. |

---

## Validation Sign-Off

- [ ] All 5 csproj target net10.0 / net10.0-windows; no net8.0 remains
- [ ] `Microsoft.Extensions.Logging.Abstractions` 10.0.9; no preview/alpha/beta anywhere
- [ ] `dotnet restore` + `dotnet build SwtorLogParser.slnx` succeed
- [ ] `dotnet test` green (106), zero skips
- [ ] AOT compile IL2xxx/IL3xxx-clean; IsAotCompatible + PublishAot retained
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
