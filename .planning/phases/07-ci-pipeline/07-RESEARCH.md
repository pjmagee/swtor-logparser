# Phase 6: CI Pipeline - Research

**Researched:** 2026-06-12
**Domain:** GitHub Actions CI for a multi-target .NET 8 solution (.slnx) with WinForms + Native AOT
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- **Runner:** `windows-latest`. The Overlay targets `net8.0-windows` + WinForms (`UseWindowsForms`) and won't build on Linux; the full `.slnx` solution must build. Windows runners also have the MSVC toolchain needed for Native AOT publish.
- **SDK:** use `actions/setup-dotnet@v4` to install **.NET 8.0.x** (targeting packs / runtime for `net8.0` + `net8.0-windows`) AND a **.slnx-capable SDK (10.0.x)** — the solution was migrated to `.slnx`, which the .NET 8 SDK cannot parse (needs SDK 9+/10). Install both (multi-line `dotnet-version`).
- **Steps:** `dotnet restore SwtorLogParser.slnx` → `dotnet build SwtorLogParser.slnx -c Release --no-restore` → `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj -c Release --no-build` (a test failure → non-zero exit → red run = the regression gate).
- **Native AOT publish job** (windows-latest, MSVC present): `dotnet publish SwtorLogParser.Native.Cli -c Release` — validates the AOT link step that could not be linked on the dev machine (closes the Phase 5 human-verify item). May be a separate job in the same workflow (parallel or dependent).
- **Triggers:** `push` and `pull_request` targeting the default branch (`main`).
- **Coverage:** collect via the existing `coverlet.collector` (`--collect:"XPlat Code Coverage"`) but do NOT gate or upload to an external service — keep simple (optionally upload as a build artifact).

### Claude's Discretion
- Workflow file name (`ci.yml` / `build.yml` / `dotnet.yml`), exact job layout (one job vs build+test and AOT split), whether AOT is a separate job or appended, NuGet caching (`actions/setup-dotnet` cache or `actions/cache`), and concurrency/cancel-in-progress settings — guided by a green run on current `main` and the 3 success criteria.

### Deferred Ideas (OUT OF SCOPE)
- Release/packaging workflow, NuGet publish, badges, coverage upload (Codecov) — future, not INFRA-01.
- Updating CLAUDE.md/STACK.md to drop the now-removed System.CommandLine — handle at milestone docs-update, not here.
- Backlog BL-01 (overlay topmost), issues #1-4 (next-milestone).
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| INFRA-01 | A CI pipeline (GitHub Actions) builds the solution and runs the test suite on push/PR | Confirmed `.slnx` builds via SDK 9.0.200+/10 CLI; `setup-dotnet@v4` multiline installs both SDK 8 + 10; windows-latest has WindowsDesktop pack (WinForms) and MSVC (AOT); tests confirmed hermetic (no real SWTOR folder). Complete workflow YAML provided below. |
</phase_requirements>

## Summary

This is a greenfield CI phase: no `.github/workflows/` exists yet. The goal is a single GitHub Actions workflow that restores → builds (`-c Release`) the full `SwtorLogParser.slnx` and runs the 106-test xUnit suite on every push and pull request to `main`, plus a Native AOT publish job to validate the link step. All four locked technical assumptions in CONTEXT.md were verified against official sources and the codebase:

1. **`.slnx` requires SDK 9.0.200+ to build via the CLI** `[VERIFIED]`. The .NET 8 SDK cannot parse `.slnx`. .NET 10 makes `.slnx` the default `dotnet new sln` format, so SDK 10.0.x parses and builds it reliably. The repo has **only** a `.slnx` (no sibling `.sln`), so there is no ambiguous-solution error.
2. **`actions/setup-dotnet@v4` installs multiple SDKs in one step via a multiline `dotnet-version`** `[VERIFIED]`. Installing `8.0.x` (targeting packs/runtime for `net8.0` + `net8.0-windows`) and `10.0.x` (the `.slnx` parser + build driver) is correct.
3. **`windows-latest` (Windows Server 2022, VS 2022 Enterprise) ships the Native Desktop C++ workload (MSVC `link.exe`) and the Windows Desktop targeting pack** `[VERIFIED]`. WinForms (`net8.0-windows`) builds without extra steps; Native AOT publish should link successfully on the runner.
4. **The test suite is hermetic — no test requires a real SWTOR log directory** `[VERIFIED: codebase]`. All filesystem access goes through `Path.GetTempPath()` per-test temp dirs or the injectable `ICombatLogSource` seam (guarded by `Directory.Exists`). The one test touching the real path (`Default_Path_Has_Expected_Suffix`) only asserts the path *string* suffix, never that the directory exists.

**Primary recommendation:** Single `ci.yml` with two jobs — `build-test` (required gate: restore/build/test/coverage-artifact) and `aot-publish` (`continue-on-error: true` safety net). Use `setup-dotnet@v4` with `cache: true` for NuGet. Add `concurrency` with `cancel-in-progress` to save runner minutes. A complete copy-pasteable workflow is in **Code Examples**.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Trigger on push/PR | CI / GitHub Actions | — | Workflow `on:` block owns event filtering |
| SDK provisioning (8 + 10) | CI runner setup | — | `actions/setup-dotnet@v4` installs toolchains |
| Restore / build `.slnx` | CI / dotnet SDK 10 | — | SDK 10 is the only installed SDK that parses `.slnx` |
| Run test suite (regression gate) | CI / dotnet test | Test project | `dotnet test` non-zero exit = red run |
| AOT link validation | CI runner (MSVC) | dotnet publish | Validates Phase 5 human-verify item; MSVC on runner |
| Coverage collection | coverlet.collector | CI artifact | Collect-only, no gate/upload per locked decision |

## Standard Stack

### Core
| Library / Action | Version | Purpose | Why Standard |
|------------------|---------|---------|--------------|
| `actions/checkout` | `v4` | Clone repo into runner | Canonical first step; `v4` is current GA `[VERIFIED]` |
| `actions/setup-dotnet` | `v4` | Install .NET SDK(s) + NuGet cache | Official .NET setup action; `v4` supports multiline versions + `cache` `[VERIFIED]` |
| `actions/upload-artifact` | `v4` | Persist coverage `.cobertura.xml` | Standard artifact persistence (optional per decision) |

> Note: `actions/setup-dotnet@v5` and `actions/checkout@v6` exist as of mid-2026, but CONTEXT.md **locks `@v4`** for setup-dotnet. Honor the lock. `checkout@v4` is the safe, widely-used pairing; do not bump without a decision change.

### Supporting (already in the repo — no install needed)
| Component | Version | Purpose |
|-----------|---------|---------|
| `coverlet.collector` | `6.0.4` (Directory.Packages.props) | `--collect:"XPlat Code Coverage"` produces Cobertura XML |
| `xunit` / `xunit.runner.visualstudio` | `2.9.3` / `3.1.5` | Test framework + VSTest adapter |
| `Microsoft.NET.Test.Sdk` | `18.6.0` | Enables `dotnet test` |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Build full `.slnx` | `dotnet test` on the test project only (pulls core lib transitively) | Faster, but skips compiling Overlay/CLIs — loses the "whole solution compiles" guarantee. CONTEXT.md prefers full `.slnx`; use the project-only path only as a documented fallback if `.slnx` proves troublesome. |
| `setup-dotnet` `cache: true` | `actions/cache` keyed on `**/*.csproj` + `Directory.Packages.props` | `actions/cache` is more flexible but more boilerplate; `cache: true` is simpler and sufficient here. Note: `cache: true` requires a lockfile OR it hashes `**/packages.lock.json` — repo has **none**, see Pitfall 4. |
| Two jobs (build-test + aot) | One job, AOT appended as a step | Splitting lets AOT run in parallel and `continue-on-error` without affecting the gate's status cleanly. Recommended. |

**Installation:** No `npm`/`pip` install. The "install" is the workflow file at `.github/workflows/ci.yml`.

**Version verification (actions are GitHub-hosted, not a package registry):**
- `actions/checkout@v4`, `actions/setup-dotnet@v4`, `actions/upload-artifact@v4` are all current major tags maintained by GitHub `[VERIFIED: github.com/actions]`.
- SDK channels: `8.0.x` and `10.0.x` are valid `setup-dotnet` version specifiers resolving to the latest patch in each channel.

## Package Legitimacy Audit

> This phase installs **no NuGet/npm/pip packages**. It adds GitHub Actions (first-party, published by the `actions` org on GitHub Marketplace) and consumes already-pinned NuGet packages from `Directory.Packages.props` (verified in Phase 5, DEP-01/DEP-02).

| Action | Source | Publisher | Verdict | Disposition |
|--------|--------|-----------|---------|-------------|
| `actions/checkout@v4` | github.com/actions/checkout | GitHub (first-party) | OK | Approved |
| `actions/setup-dotnet@v4` | github.com/actions/setup-dotnet | GitHub (first-party) | OK | Approved |
| `actions/upload-artifact@v4` | github.com/actions/upload-artifact | GitHub (first-party) | OK | Approved |

**Packages removed due to [SLOP] verdict:** none
**Packages flagged as suspicious [SUS]:** none

> Supply-chain note: pinning actions to a major tag (`@v4`) is the project-locked convention here. Pinning to a full commit SHA is the hardened alternative but is out of scope for INFRA-01 and not requested.

## Architecture Patterns

### System Architecture Diagram

```
  push to main ─┐
                ├─► GitHub Actions trigger (on: push / pull_request, branches: [main])
  PR to main ──┘
                         │
                         ▼
          ┌──────────────────────────────────────┐
          │ concurrency gate (cancel-in-progress) │
          └──────────────────────────────────────┘
                         │
        ┌────────────────┴─────────────────────────┐
        ▼                                           ▼
 ┌──────────────────────────┐         ┌──────────────────────────────┐
 │ job: build-test          │         │ job: aot-publish             │
 │ (REQUIRED gate)          │         │ (continue-on-error: true)    │
 │ runs-on: windows-latest  │         │ runs-on: windows-latest      │
 │                          │         │                              │
 │ checkout@v4              │         │ checkout@v4                  │
 │ setup-dotnet@v4 (8 + 10) │         │ setup-dotnet@v4 (8 + 10)     │
 │   cache NuGet            │         │ restore Native.Cli           │
 │ restore SwtorLogParser   │         │ publish Native.Cli -c Release│
 │   .slnx  (SDK 10 parses) │         │   └─ MSVC link.exe (runner)  │
 │ build .slnx -c Release   │         │ upload native exe (optional) │
 │ test Tests.csproj        │         └──────────────────────────────┘
 │   --collect coverage     │
 │   (non-zero = RED)       │
 │ upload coverage artifact │
 └──────────────────────────┘
        │
        ▼
  green = regression gate passed   red = build/test failure (the gate fires)
```

The two jobs are independent and run in parallel (no `needs`). `build-test` is the required status check; `aot-publish` is informational (its failure is swallowed by `continue-on-error` so a runner-side AOT flake cannot block a PR).

### Recommended Project Structure
```
.github/
└── workflows/
    └── ci.yml        # single workflow, two jobs (build-test + aot-publish)
```

### Pattern 1: Install two SDK channels in one setup step
**What:** A multiline `dotnet-version` installs both runtimes/SDKs side by side.
**When to use:** When the project targets `net8.0` (needs SDK 8 targeting packs/runtime) but the solution format (`.slnx`) requires a newer SDK to drive the build.
**Example:** see Code Examples below.
**Source:** `[CITED: github.com/actions/setup-dotnet]` — multiline `dotnet-version` with `|`.

### Pattern 2: `.slnx` as the build entry point
**What:** Pass `SwtorLogParser.slnx` to `dotnet restore`/`build`. With SDK 10 installed, the CLI resolves and builds all 5 projects.
**When to use:** Always here — the repo has exactly one `.slnx` and no `.sln`, so the "both present is an error" caveat does not apply.
**Source:** `[CITED: devblogs.microsoft.com/dotnet/introducing-slnx-support-dotnet-cli]`.

### Pattern 3: Coverage as collect-only artifact
**What:** `dotnet test --collect:"XPlat Code Coverage"` writes `TestResults/**/coverage.cobertura.xml`; upload it with `upload-artifact@v4`. No threshold, no Codecov.
**When to use:** When coverage visibility is wanted without a quality gate (the locked decision).

### Anti-Patterns to Avoid
- **Running on `ubuntu-latest`:** the Overlay (`net8.0-windows` + WinForms) and Native AOT (MSVC linker) cannot build on Linux. Locked to `windows-latest`.
- **Installing only SDK 8:** SDK 8 cannot parse `.slnx` → restore/build fails immediately. Must install 10.0.x.
- **Installing only SDK 10:** builds `.slnx`, but if the `net8.0` targeting pack is missing the `net8.0`/`net8.0-windows` builds error with "SDK does not support targeting". Installing the `8.0.x` channel provides the packs. (See Pitfall 5 for the nuance.)
- **Gating/uploading coverage to Codecov:** out of scope (deferred).
- **`dotnet build --no-restore` without a prior restore step:** ordering matters — restore first, then `build --no-restore`, then `test --no-build`.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Installing .NET SDKs on the runner | Custom `winget`/`dotnet-install.ps1` script | `actions/setup-dotnet@v4` | Handles channel resolution, PATH, caching, multi-version |
| NuGet restore caching | Hand-rolled `actions/cache` key | `setup-dotnet` `cache: true` (with caveat) or a single `actions/cache` keyed on packages | Less error-prone; see Pitfall 4 |
| Test result → red build | Parsing test output | `dotnet test` exit code | Non-zero exit already fails the step/job |
| Coverage format | Custom instrumentation | `coverlet.collector` (already referenced) | Already in the test project; just pass `--collect` |
| Locating MSVC `link.exe` for AOT | `ilammy/msvc-dev-cmd` / manual vcvars | nothing — runner has it | windows-latest ships the Native Desktop C++ workload; the SDK locates VS via vswhere |

**Key insight:** Everything this phase needs is provided by first-party GitHub Actions and the already-configured SDK/NuGet tooling. The only "code" is YAML.

## Runtime State Inventory

> N/A — this phase is purely additive (a new workflow file). It is not a rename/refactor/migration. No stored data, live service config, OS-registered state, secrets, or build artifacts are changed.
>
> **Nothing found in any category — verified:** the phase adds `.github/workflows/ci.yml` and touches no source, config, or runtime state. The workflow consumes existing repo state (`.slnx`, `Directory.Packages.props`, test project) read-only on the runner.

## Common Pitfalls

### Pitfall 1: `.slnx` fails to build because only SDK 8 is installed
**What goes wrong:** `dotnet restore SwtorLogParser.slnx` errors with an unrecognized/unsupported solution format.
**Why it happens:** `.slnx` CLI support starts at SDK **9.0.200**; the .NET 8 SDK predates it.
**How to avoid:** Install `10.0.x` via `setup-dotnet` (multiline). Because both SDKs are on PATH, the CLI uses the newest (10) to drive the build, which parses `.slnx`.
**Warning signs:** "Could not load the solution" / unknown file extension on the restore step.
`[VERIFIED: devblogs.microsoft.com/dotnet/introducing-slnx-support-dotnet-cli]`

### Pitfall 2: Native AOT `link.exe not found`
**What goes wrong:** `dotnet publish ... PublishAot=true` fails at the link stage.
**Why it happens:** AOT needs the MSVC C/C++ linker. It fails when the build is not run from an environment where the SDK can locate the VS C++ toolchain.
**How to avoid:** On `windows-latest` the VS 2022 Native Desktop workload (incl. `link.exe`) is pre-installed, and the SDK finds it via vswhere — normally no extra step needed. Keep the AOT job `continue-on-error: true` so any residual flake does not block the required gate (locked decision). If it ever fails deterministically, add `ilammy/msvc-dev-cmd@v1` before the publish step (not expected to be necessary).
**Warning signs:** `'link.exe' is not recognized` or `Platform linker not found`.
`[VERIFIED: actions/runner-images Windows2022 README + dotnet/runtime#121275]`

### Pitfall 3: WinForms `net8.0-windows` build error on the runner
**What goes wrong:** "The current .NET SDK does not support targeting net8.0-windows" or missing WindowsDesktop pack.
**Why it happens:** Missing the Windows Desktop targeting/reference pack.
**How to avoid:** On `windows-latest` the Windows Desktop pack ships with the installed Windows .NET SDKs; installing the `8.0.x` channel provides the `net8.0` + `net8.0-windows` packs. No extra step required.
**Warning signs:** SDK-does-not-support-targeting error on the Overlay project during `build .slnx`.
`[VERIFIED: actions/setup-dotnet docs + runner image]`

### Pitfall 4: `setup-dotnet` `cache: true` warns/fails without a lockfile
**What goes wrong:** With `cache: true`, the action expects `packages.lock.json` files to hash; the repo has **none** (STACK.md: "Lockfile: missing"). Behavior can be a warning or a poor cache key.
**Why it happens:** NuGet cache key derivation relies on lockfiles by default.
**How to avoid (pick one):**
- (a) Simplest/safest: **omit caching** for the first green run, add it later. Restore of ~7 small packages is fast.
- (b) Use `actions/cache` keyed on `Directory.Packages.props` + `**/*.csproj` hash, caching `~/.nuget/packages`.
- (c) Set `cache: true` **and** `cache-dependency-path: '**/*.csproj'` (or add a generated lockfile). Avoid relying on a non-existent `packages.lock.json`.
**Recommendation:** Start with (a) for a guaranteed-green first run; caching is a perf nicety, not a requirement. The provided workflow uses (b) to give caching without a lockfile.
**Warning signs:** "No file matched to [**/packages.lock.json]" warning; cache never hits.
`[VERIFIED: github.com/actions/setup-dotnet]`

### Pitfall 5: SDK 10 vs `net8.0` rollforward
**What goes wrong:** Building `net8.0` with only SDK 10 present can work (rollforward) but may pull in a different language/analyzer behavior; missing the `net8.0` runtime/packs can fail tests at runtime.
**Why it happens:** SDK 10 builds `net8.0` via targeting packs; the `net8.0` *runtime* is needed to *run* the tests.
**How to avoid:** Installing the `8.0.x` channel installs the .NET 8 runtime + packs, so `dotnet test` runs on the `net8.0` runtime. Keep both channels. (The dev machine already proves SDK 10 + .NET 8 packs builds `net8.0`.)
**Warning signs:** Tests fail to start with "framework net8.0 not found"; or analyzer/lang differences.
`[CITED: learn.microsoft.com — SDK targeting/rollforward]`

### Pitfall 6: Suite accidentally depends on a real SWTOR folder
**What goes wrong:** A test reads the live `My Documents\Star Wars - The Old Republic\CombatLogs` path and flakes/throws on a clean runner.
**Why it happens (historically):** Pre-Phase-3 tests iterated the static `CombatLogs` source which read the real folder.
**Status: RESOLVED — verified this session.** `[VERIFIED: codebase grep]`
- All file I/O in tests targets `Path.GetTempPath()` per-test temp dirs (`InMemoryCombatLogSource`, `CombatLogReadTests`, `CombatLogsMonitorTests`).
- The `ICombatLogSource` seam is injected via `CombatLogs.SetSource(fixture)` / `ResetSource()`, serialized by `[Collection(CombatLogsSourceCollection.Name)]`.
- The only test referencing the real path, `CombatLogSourceTests.Default_Path_Has_Expected_Suffix`, asserts the path **string** ends with `Star Wars - The Old Republic\CombatLogs` — it does **not** require the directory to exist.
- `Static_Ctor_Does_Not_Throw_When_Settings_Absent` explicitly verifies the static ctor tolerates a missing folder (`Directory.Exists` guard).
**How to keep it green:** No action needed; do not introduce tests that read the real folder.

## Code Examples

### Complete recommended `.github/workflows/ci.yml`

```yaml
# Source: composed from actions/setup-dotnet, actions/checkout official docs
#         + Microsoft .slnx CLI guidance. Verified 2026-06-12.
name: CI

on:
  push:
    branches: [ main ]
  pull_request:
    branches: [ main ]

# Cancel superseded runs on the same ref to save runner minutes.
concurrency:
  group: ci-${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true

jobs:
  build-test:
    name: Build & Test (regression gate)
    runs-on: windows-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET (8 for net8.0 packs/runtime, 10 to parse .slnx)
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: |
            8.0.x
            10.0.x

      # Lockfile-free NuGet cache (repo has no packages.lock.json).
      - name: Cache NuGet packages
        uses: actions/cache@v4
        with:
          path: ~/.nuget/packages
          key: nuget-${{ runner.os }}-${{ hashFiles('**/*.csproj', 'Directory.Packages.props') }}
          restore-keys: |
            nuget-${{ runner.os }}-

      - name: Restore
        run: dotnet restore SwtorLogParser.slnx

      - name: Build
        run: dotnet build SwtorLogParser.slnx -c Release --no-restore

      - name: Test (failure => red run = the gate)
        run: >
          dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj
          -c Release --no-build
          --collect:"XPlat Code Coverage"
          --results-directory ./TestResults

      - name: Upload coverage artifact
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: coverage
          path: TestResults/**/coverage.cobertura.xml
          if-no-files-found: ignore

  aot-publish:
    name: Native AOT publish (link-step validation)
    runs-on: windows-latest
    continue-on-error: true   # informational; must not block the gate
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: |
            8.0.x
            10.0.x

      - name: Publish Native AOT CLI
        run: dotnet publish SwtorLogParser.Native.Cli -c Release

      - name: Upload native executable
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: native-cli
          path: SwtorLogParser.Native.Cli/bin/Release/**/publish/*.exe
          if-no-files-found: ignore
```

### Multiline SDK install (the locked Pattern 1)
```yaml
# Source: github.com/actions/setup-dotnet (README)
- uses: actions/setup-dotnet@v4
  with:
    dotnet-version: |
      8.0.x
      10.0.x
```

### Fallback if `.slnx` ever misbehaves in CI (documented, not preferred)
```yaml
# Builds the test project + its transitive core-lib reference only.
# Loses the "Overlay/CLIs compile" guarantee — use only as a last resort.
- run: dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj -c Release
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `.sln` text format | `.slnx` XML format, default for `dotnet new sln` | .NET 10 (CLI support from SDK 9.0.200) | Must use SDK 9.0.200+ in CI; SDK 8 cannot build it |
| Single `dotnet-version` | Multiline `dotnet-version` installs N SDKs | setup-dotnet v3+/v4 | One step installs both 8.0.x + 10.0.x |
| Codecov/coverage gates by default | Collect-only artifact for small projects | — (project decision) | Keeps CI simple; no external dependency |

**Deprecated/outdated:**
- `actions/upload-artifact@v3` — deprecated; use `@v4`.
- Building Native AOT on Linux runners for a Windows-target app — not applicable; this app is Windows-only.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | Native AOT publish links cleanly on `windows-latest` with no extra MSVC setup step | Pitfall 2 | LOW — job is `continue-on-error`, so a failure does not block the gate; closing the Phase 5 verify item would just need `ilammy/msvc-dev-cmd`. Verified MSVC is present on the image; the only residual risk is SDK/vswhere discovery. |
| A2 | `10.0.x` is a valid, available `setup-dotnet` channel on the runner at run time | Standard Stack | LOW — .NET 10 is GA as of 2026; channel resolves to latest 10 patch. If a future runner lacks it, pin a concrete `10.0.x` patch. |

**Note:** All other claims are `[VERIFIED]` (codebase grep or official docs) — only A1/A2 carry residual runtime risk, both mitigated.

## Open Questions

1. **Should `aot-publish` close the Phase-5 human-verify item automatically?**
   - What we know: the job validates the link step; `continue-on-error` means its status is informational.
   - What's unclear: whether a green `aot-publish` is sufficient evidence to flip the Phase-5 item, or whether a human must inspect the artifact.
   - Recommendation: treat a green `aot-publish` run as closing the item; the planner can add a note. No blocker.

2. **NuGet caching strategy (discretion area).**
   - What we know: repo has no lockfile, so `setup-dotnet cache: true` is awkward.
   - Recommendation: use the `actions/cache@v4` block shown (keyed on csproj + Directory.Packages.props), or drop caching entirely for the first run. Either yields green.

## Environment Availability

| Dependency | Required By | Available on windows-latest | Version | Fallback |
|------------|------------|-----------------------------|---------|----------|
| .NET SDK 8.0.x | `net8.0`/`net8.0-windows` packs + runtime | Installed via `setup-dotnet@v4` | 8.0.x channel | — |
| .NET SDK 10.0.x | Parse/build `.slnx` | Installed via `setup-dotnet@v4` | 10.0.x channel | — |
| Windows Desktop targeting pack | WinForms Overlay (`net8.0-windows`) | Yes (ships with SDK / VS on runner) | — | — |
| MSVC `link.exe` (Native Desktop C++ workload) | Native AOT publish | Yes (VS 2022 Enterprise on runner) | VS 2022 17.x | `ilammy/msvc-dev-cmd@v1` (not expected) |
| Real SWTOR `CombatLogs` folder | NOTHING (tests are hermetic) | N/A | — | N/A — not needed |

**Missing dependencies with no fallback:** none.
**Missing dependencies with fallback:** MSVC discovery for AOT — fallback `ilammy/msvc-dev-cmd`, mitigated by `continue-on-error`.

## Validation Architecture

> The CI run **is** the validation for this phase. Success = green run on `main`; red = build error or test failure.

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 + xunit.runner.visualstudio 3.1.5 + Microsoft.NET.Test.Sdk 18.6.0 |
| Config file | none (convention-based; `IsTestProject=true`) |
| Quick run command | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` |
| Full suite command | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj -c Release --no-build` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|--------------|
| INFRA-01 | `.slnx` restores + builds in Release on windows-latest | build (smoke) | `dotnet build SwtorLogParser.slnx -c Release` | ✅ (the CI job) |
| INFRA-01 | 106-test suite runs and a failure reds the run | regression gate | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj -c Release --no-build` | ✅ existing suite |
| INFRA-01 (extra) | Native AOT link step succeeds | publish (informational) | `dotnet publish SwtorLogParser.Native.Cli -c Release` | ✅ (continue-on-error job) |

### Sampling Rate
- **Per task commit:** local `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` (106 pass).
- **Per push/PR:** the `build-test` job (this is the gate going forward).
- **Phase gate:** a green `build-test` run on `main` after merge = INFRA-01 satisfied.

### Wave 0 Gaps
- None — the test infrastructure already exists (Phases 1-5; 106 passing, hermetic). This phase adds *only* the workflow file; no new test files or fixtures are required.
- One verification action for the plan: after merging, confirm the first `main` run is green and the `aot-publish` job is green (or note it as `continue-on-error` informational).

## Security Domain

> `security_enforcement` default (enabled). This phase adds no application attack surface; the relevant surface is CI/supply-chain.

### Applicable ASVS Categories
| ASVS Category | Applies | Standard Control |
|---------------|---------|------------------|
| V1 Architecture / CI | yes | Pin actions to maintained major tags (`@v4`); first-party `actions/*` only |
| V5 Input Validation | no | No runtime input handled by the workflow |
| V6 Cryptography | no | No secrets/crypto in this workflow |
| V14 Configuration | yes | Least-privilege; default `GITHUB_TOKEN` is read-only for this workflow (no write needed) |

### Known Threat Patterns for GitHub Actions CI
| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Malicious/compromised third-party action | Tampering / EoP | Use only first-party `actions/*`; pin to `@v4` (SHA-pin is the hardened option, out of scope) |
| Workflow with excessive `GITHUB_TOKEN` perms | EoP | No `permissions:` write needed; default read is sufficient. Optionally add `permissions: { contents: read }` |
| Untrusted PR code running with secrets | Info disclosure | This workflow uses no secrets; `pull_request` (not `pull_request_target`) runs in the fork context safely |
| Cache poisoning | Tampering | NuGet cache keyed on file hashes; restore is deterministic from GA packages |

**Recommended hardening (optional, low effort):** add a top-level `permissions: { contents: read }` block to make least-privilege explicit. Not required for INFRA-01.

## Sources

### Primary (HIGH confidence)
- `github.com/actions/setup-dotnet` (README) — multiline `dotnet-version`, `cache` input, lockfile expectation.
- `github.com/actions/runner-images` Windows2022 README + GitHub changelog — VS 2022 Enterprise + Native Desktop C++ workload (MSVC) on `windows-latest`.
- `devblogs.microsoft.com/dotnet/introducing-slnx-support-dotnet-cli` — `.slnx` CLI support from SDK 9.0.200; `dotnet build .slnx`; both-present-is-error caveat.
- `learn.microsoft.com/.../dotnet-new-sln-slnx-default` — .NET 10 makes `.slnx` the default solution format.
- Codebase: `SwtorLogParser.slnx`, all `*.csproj`, `Directory.Packages.props`, `SwtorLogParser.Tests/**` (grep) — hermetic-test verification, target frameworks, package versions.

### Secondary (MEDIUM confidence)
- `dotnet/runtime#121275` — Native AOT `link.exe not found` symptom + that it's about MSVC linker discovery.

### Tertiary (LOW confidence)
- General GitHub Actions .NET tutorials (codejack, dotnetcurry) — corroborating workflow shape only.

## Metadata

**Confidence breakdown:**
- Standard stack (actions + SDK channels): HIGH — official docs + runner image confirmed.
- `.slnx` build requirement: HIGH — Microsoft devblog + Learn breaking-change doc.
- WinForms / WindowsDesktop pack: HIGH — runner image + setup-dotnet behavior.
- Native AOT link on runner: MEDIUM-HIGH — MSVC confirmed present; residual vswhere-discovery risk mitigated by `continue-on-error`.
- Hermetic tests: HIGH — direct codebase verification.

**Research date:** 2026-06-12
**Valid until:** ~2026-07-12 (stable domain; re-check if `setup-dotnet` majors bump or runner image drops the C++ workload).
