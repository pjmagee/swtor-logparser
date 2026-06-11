---
phase: 5
slug: dependency-upgrades
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-06-12
---

# Phase 5 — Validation Strategy

> Dependency upgrades + CLI framework removal. Behavior preserved; suite stays green; AOT preserved.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit (upgrading to GA 2.9.3) |
| **Restore** | `dotnet restore SwtorLogParser.slnx` (must succeed with GA feeds only) |
| **Build-all** | `dotnet build SwtorLogParser.slnx -c Debug` |
| **Test** | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` (106 baseline) |
| **AOT publish** | `dotnet publish SwtorLogParser.Native.Cli -c Release` (PublishAot must still work) |
| **Estimated runtime** | restore+build ~30s, test ~15s, AOT publish ~1-3min |

---

## Sampling Rate

- **After the version/CPM migration commit:** `dotnet restore` + `dotnet build SwtorLogParser.slnx` must succeed; `dotnet test` green.
- **After the System.CommandLine removal + Spectre.Console commit:** build + test green; manually smoke `list` and `monitor` (Ctrl+C) on BOTH hosts.
- **After all changes:** `dotnet publish SwtorLogParser.Native.Cli -c Release` (AOT) must succeed.
- No package version is preview/alpha/beta; `Directory.Packages.props` is the single source of versions.

---

## Wave 0 Requirements

- Existing test infrastructure carries over (xUnit, just GA-bumped). No new test framework. The GA bump itself is validated by the existing 106 tests passing post-upgrade.

---

## Per-Task Verification Map

| Task | Req | Proves | Check | Status |
|------|-----|--------|-------|--------|
| Directory.Packages.props + GA versions | DEP-01, DEP-02 | central mgmt; no beta/alpha/preview; restore GA-only | `dotnet restore` + grep csproj for Version=/preview | ⬜ |
| Remove dead core-lib refs + add explicit Logging.Abstractions | DEP-01 | core lib builds with only needed refs | `dotnet build SwtorLogParser` | ⬜ |
| Drop System.CommandLine + hand-rolled dispatch + Ctrl+C bridge | DEP-03 | both hosts parse list/monitor; Ctrl+C → Stop() | build + manual smoke | ⬜ |
| Spectre.Console table in managed CLI | DEP-03 | managed CLI renders 5 columns without S.CL.Rendering | build + manual `monitor` | ⬜ |
| Remove DockerDefaultTargetOS=Linux | INFRA-02 | absent from all csproj | grep | ⬜ |

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| `monitor` Ctrl+C stops cleanly (both hosts) | DEP-03 | Requires interactive console + signal | Run `dotnet run --project SwtorLogParser.Cli -- monitor` and `...Native.Cli -- monitor`; press Ctrl+C; confirm clean exit (Stop() ran, no hang). |
| Managed CLI table renders 5 columns | DEP-03 | Console rendering not unit-testable | Run managed CLI `monitor`; confirm Player/DPS/crit%/HPS/crit% columns render via Spectre.Console. |
| Native AOT publish works | DEP-01/03 | AOT compile needs MSVC toolchain | `dotnet publish SwtorLogParser.Native.Cli -c Release`; confirm native exe produced, no AOT warnings about removed deps. |

---

## Validation Sign-Off

- [ ] `dotnet restore` succeeds; no preview/alpha/beta version in any csproj or Directory.Packages.props
- [ ] `Directory.Packages.props` is the single version source (no NU1008)
- [ ] `dotnet test` green (106), zero skips
- [ ] No System.CommandLine / System.CommandLine.Rendering reference anywhere
- [ ] No DockerDefaultTargetOS in any csproj
- [ ] Native AOT publish succeeds; core lib still IsAotCompatible
- [ ] Ctrl+C → Stop() verified on both hosts (manual)
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
