# Roadmap: SWTOR Log Parser

## Milestones

- ✅ **v1.0 Hardening (+ .NET 10)** — Phases 1-7 (shipped 2026-06-12) — see [milestones/v1.0-ROADMAP.md](milestones/v1.0-ROADMAP.md)

## Phases

<details>
<summary>✅ v1.0 Hardening (Phases 1-7) — SHIPPED 2026-06-12</summary>

- [x] Phase 1: Parser Safety Net — characterization/golden tests (TEST-03)
- [x] Phase 2: Correctness Bugs — BUG-01..07 (+ Latin-1 encoding UAT fix)
- [x] Phase 3: Monitor Refactor + Coverage — RFCT-01..03, TEST-01/02
- [x] Phase 4: Performance — PERF-01..03
- [x] Phase 5: Dependency Upgrades — DEP-01..03, INFRA-02
- [x] Phase 6: .NET 10 Upgrade — PLAT-01 (closed issue #1)
- [x] Phase 7: CI Pipeline — INFRA-01 (CI green on main)

Full detail: [milestones/v1.0-ROADMAP.md](milestones/v1.0-ROADMAP.md) · audit: [milestones/v1.0-MILESTONE-AUDIT.md](milestones/v1.0-MILESTONE-AUDIT.md)

</details>

### 📋 Next Milestone (planned — see BACKLOG.md / issues)

Candidate clusters (not yet scoped into a milestone):
- **Overlay / UI** — BL-01 (overlay topmost on windowed/borderless), issue #3 (CsWin32 for Win32 interop), issue #4 (lightweight UI alternative to WinForms: WinUI 3 / MAUI)
- **Tooling** — issue #2 (xUnit → MSTest .NET SDK)
- **Docs** — refresh CLAUDE.md / codebase map to reflect .NET 10 + Spectre.Console (System.CommandLine removed)

Start with `/gsd:new-milestone`.

## Progress

| Phase | Milestone | Status | Completed |
|-------|-----------|--------|-----------|
| 1. Parser Safety Net | v1.0 | Complete | 2026-06-11 |
| 2. Correctness Bugs | v1.0 | Complete | 2026-06-11 |
| 3. Monitor Refactor + Coverage | v1.0 | Complete | 2026-06-11 |
| 4. Performance | v1.0 | Complete | 2026-06-11 |
| 5. Dependency Upgrades | v1.0 | Complete | 2026-06-12 |
| 6. .NET 10 Upgrade | v1.0 | Complete | 2026-06-12 |
| 7. CI Pipeline | v1.0 | Complete | 2026-06-12 |
