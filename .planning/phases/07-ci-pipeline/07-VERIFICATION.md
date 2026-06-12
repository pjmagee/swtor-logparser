---
phase: 7
slug: ci-pipeline
status: human_needed
verified: 2026-06-12
---

# Phase 7 — CI Pipeline — Verification

**Requirement:** INFRA-01
**Status:** human_needed (the "green on main" run requires a push to GitHub)

## Success Criteria

1. **A `.github/workflows/` YAML exists that triggers on push + pull_request to main, restores, builds the full solution, and runs `dotnet test`** — ✅ VERIFIED.
   `.github/workflows/ci.yml` exists; `on: push/pull_request` → `branches: [main]`; `build-test` job runs `dotnet restore SwtorLogParser.slnx` → `dotnet build SwtorLogParser.slnx -c Release` → `dotnet test SwtorLogParser.Tests/...`. YAML parsed clean.

2. **The workflow completes successfully (green) on the current main branch state** — ⏳ HUMAN-NEEDED.
   The 64 milestone commits are local; the workflow has not run on GitHub yet. The exact CI build-test commands were SIMULATED locally and pass:
   - `dotnet restore SwtorLogParser.slnx` → OK
   - `dotnet build SwtorLogParser.slnx -c Release` → Build succeeded, 0 errors
   - `dotnet test SwtorLogParser.Tests/... -c Release --no-build` → **106 passed / 0 failed / 0 skipped (net10.0)**
   So the run is expected green once pushed. Confirming "green on main" requires pushing to `origin/main` (or a PR branch) so Actions executes.

3. **A test failure in any project causes the CI run to fail (non-zero exit)** — ✅ VERIFIED (by design).
   `dotnet test` exits non-zero on any failed test → the `build-test` job fails → the run is red. The job is the required gate (the non-blocking `aot-publish` job is `continue-on-error`).

## Bonus — closes a Phase 6 item

The `aot-publish` job (`windows-latest`, MSVC present) runs `dotnet publish SwtorLogParser.Native.Cli -c Release`, exercising the full Native AOT **link** step that could not run on the dev machine — once CI runs, this confirms the Phase 6 AOT human-verify item end-to-end.

## Outstanding (human)

- **Push** the milestone to GitHub (direct to `main`, or a PR branch) so the workflow runs and we observe green. This is the only item between here and INFRA-01 fully satisfied.

---
*Verified locally 2026-06-12; GitHub run pending push.*
