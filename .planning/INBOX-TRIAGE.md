# GSD Inbox Triage — pjmagee/swtor-logparser — 2026-06-12

> Note: this repo has **no `.github/ISSUE_TEMPLATE/`, no PR templates, and no `CONTRIBUTING.md`**, so template-compliance scoring does not apply. This is an adapted triage: classification, summary, relevance to the active **v1.0 Hardening milestone**, and recommended labels.

## Summary

| | Count |
|---|---|
| Open issues | 4 |
| Open PRs | 0 |
| Gate violations | 0 (no PRs) |
| Stale (>30 days) | 0 (all created 2026-06-11) |
| Labelled | 0 of 4 |

All 4 issues are authored by the owner (@pjmagee) and are **forward-looking direction items** — none are bugs, and **none belong to the current v1.0 hardening milestone** (which is intentionally scoped to correctness/perf/deps/CI on the existing .NET 8 + xUnit + WinForms stack). Two of them (#1, #2) are mild *deliberate* tensions with Phase 5 decisions — see notes.

## Issues

### #1 — Upgrade to .NET 10 LTS  · *Chore / platform*
- **Body:** empty.
- **Relevance:** **Out of v1.0 scope by design** — Phase 5 explicitly stays on **.NET 8** (hardening first, modernize later). This is a clean next-milestone candidate.
- **Recommend:** label `enhancement` + `next-milestone`; add a one-line body (target framework bump, AOT re-validation, breaking-change scan).

### #2 — Replace xUnit with the new MSTest .NET SDK  · *Chore / tooling*
- **Body:** links the MSTest SDK docs.
- **Relevance:** **Tension with Phase 5** — Phase 5 *upgrades xUnit to GA*, it doesn't replace the framework. Replacing with MSTest is an alternative tooling direction, best done **after** v1.0 ships (the 106-test suite is the regression contract for the whole milestone; swapping frameworks mid-hardening would risk it).
- **Recommend:** label `enhancement` + `next-milestone`; note the dependency ("do after v1.0 hardening lands").

### #3 — CsWin32 for Win32 APIs (user32, kernel32)  · *Enhancement / code quality*
- **Body:** investigate `Microsoft.Windows.CsWin32` source generator for the native interop.
- **Relevance:** Targets the Overlay's hand-written `user32.dll` P/Invoke (`SwtorLogParser.Overlay/NativeMethods.cs`). **Clusters with [BL-01]** (overlay-topmost, which would *add* `SetWindowPos`/`SetWinEventHook` P/Invoke) and #4. CsWin32 would source-generate those safely.
- **Recommend:** label `enhancement` + `area: overlay`; cross-link to BL-01 — a future "Overlay/UI" effort could do BL-01 + #3 together.

### #4 — Lightweight UI alternative to WinForms (WinUI 3 / MAUI)  · *Feature*
- **Body:** WinUI 3 (Windows App SDK) or MAUI (better Native AOT progress) instead of classic WinForms.
- **Relevance:** A new presentation host — the largest item. Clusters with **[BL-01]** (the overlay is the weak spot) and #3. The Phase 3 view-dedup (`SwtorLogParser.View` core types) already makes adding a new host easier.
- **Recommend:** label `feature` + `next-milestone` + `area: overlay`; flesh out the body (which framework, AOT requirement, parity with the WinForms overlay).

## Recommended labels (report-only — re-run `/gsd:inbox --label` to apply)

| # | Suggested labels |
|---|------------------|
| 1 | `enhancement`, `next-milestone` |
| 2 | `enhancement`, `next-milestone` |
| 3 | `enhancement`, `area: overlay` |
| 4 | `feature`, `next-milestone`, `area: overlay` |

## Observations

- **Theme:** #3 + #4 + the existing backlog item **BL-01** (overlay won't stay on top) form a coherent **"Overlay / UI modernization"** cluster — a strong candidate for the next milestone after v1.0 hardening completes.
- **Theme:** #1 + #2 form a **"platform/tooling modernization"** cluster (.NET 10 + MSTest), also next-milestone.
- **Sequencing is sound:** none of these should interrupt the in-flight v1.0 hardening run (currently mid-Phase-5). They're correctly parked as issues.
- No issue templates / CONTRIBUTING.md exist — if you want `/gsd:inbox` compliance scoring in future, add `.github/ISSUE_TEMPLATE/*.yml`.

---
*Triage generated: 2026-06-12 (report-only; no labels applied, no items closed)*
