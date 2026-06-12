# Phase 6: .NET 10 Upgrade - Research

**Researched:** 2026-06-12
**Domain:** .NET runtime/TFM upgrade (net8.0 → net10.0 LTS), Central Package Management, Native AOT, WinForms, xUnit
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
**Target frameworks**
- `SwtorLogParser` (core), `SwtorLogParser.Cli`, `SwtorLogParser.Native.Cli`, `SwtorLogParser.Tests`: `net8.0` → `net10.0`.
- `SwtorLogParser.Overlay`: `net8.0-windows` → `net10.0-windows` (WinForms `UseWindowsForms` retained).
- Keep `IsAotCompatible=true` (core) and `PublishAot=true` (Native CLI) — re-verify they still hold on net10.0.

**Packages (Directory.Packages.props)**
- Bump framework-tied packages to their **.NET 10 GA** versions — primarily `Microsoft.Extensions.Logging.Abstractions` 8.0.3 → 10.0.x (GA).
- Framework-agnostic GA packages stay unless a newer GA is warranted: `System.Reactive` 6.0.2, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.5, `Microsoft.NET.Test.Sdk` 18.6.0, `coverlet.collector` 6.0.4, `Spectre.Console` 0.57.0. Check each for a net10-recommended bump; **no preview/alpha/beta**.
- No NU1605 downgrades; restore must resolve cleanly under central package management.

**Verification (the gates)**
- `dotnet restore SwtorLogParser.slnx` → `dotnet build SwtorLogParser.slnx -c Release` → `dotnet test SwtorLogParser.Tests/...` (106 green, zero skips) — all on net10.0.
- `dotnet publish SwtorLogParser.Native.Cli -c Release` (PublishAot) — managed/ILCompiler code-gen must be AOT-clean (zero IL2xxx/IL3xxx); the MSVC link step may be unavailable locally (Phase 7 CI exercises the full link).

### Claude's Discretion
- Whether to bump test tooling to a newer net10-targeting GA, the exact `Microsoft.Extensions.Logging.Abstractions` 10.0.x patch, and whether to drop `LangVersion=preview` from the managed CLI — guided by green tests, AOT-clean compile, no preview/alpha/beta packages.

### Deferred Ideas (OUT OF SCOPE)
- CI pipeline targeting .NET 10 → Phase 7.
- Next-milestone issues #2 (MSTest), #3 (CsWin32), #4 (new UI), BL-01 (overlay topmost).
- Adopting new .NET 10 language/runtime APIs in code — not part of a pure TFM upgrade.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| PLAT-01 | Every project targets .NET 10 (LTS) — `net10.0` / `net10.0-windows`; framework packages on .NET 10 GA; solution builds, all tests pass, Native AOT compiles AOT-clean | This research provides the exact per-csproj TFM change list, the verified `Directory.Packages.props` GA version table, the net8→net10 breaking-change landmines (none block this codebase), and the four-gate Validation Architecture (restore/build/test/AOT-publish). |
</phase_requirements>

## Summary

This is a mechanical, well-bounded LTS upgrade. .NET 10 is GA (released Nov 2025); the dev machine already has SDK 10.0.301 and `Microsoft.WindowsDesktop.App` 10.0.9 installed (no SDK 8 present — net8 builds run on the in-box 8.0.28 runtime), so `net10.0` and `net10.0-windows` build natively today `[VERIFIED: dotnet --list-sdks/--list-runtimes]`. Five `.csproj` files need TFM string changes; one package (`Microsoft.Extensions.Logging.Abstractions`) needs a framework-aligned GA bump from 8.0.3 to **10.0.9** `[VERIFIED: NuGet flat-container API]`.

The only package the AOT path actually depends on is `Microsoft.Extensions.Logging.Abstractions`. Its `ILogger<T>`/`NullLogger<T>` usage in the core library is fully AOT/trim-safe, and the 10.0.9 GA carries `IsTrimmable`/AOT-compatibility metadata. No package in the stack lacks a net10-compatible asset: the Microsoft.Extensions.* family ships a true `net10.0` TFM; `System.Reactive` 6.0.2 (and the newer 6.1.0 GA) target `net6.0`/`netstandard2.0`, which forward-resolve cleanly to net10.0; the test stack (`Microsoft.NET.Test.Sdk` 18.6.0, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.5) and `Spectre.Console` 0.57.0 are framework-agnostic and run on a net10.0 test host `[VERIFIED: NuGet flat-container API]`.

Breaking-change review against the official .NET 10 list found **zero changes that affect this codebase**: the WinForms breaking changes touch WPF-disambiguation, `HtmlElement`, `TreeView`, `StatusStrip`, and `System.Drawing` `OutOfMemoryException` — none of which this Overlay uses (it uses `DataGridView`, `BindingList<T>`, and two `user32.dll` P/Invokes). The C# default rises to C# 14 on net10, which is a superset of preview-12 features, so the managed CLI's `LangVersion=preview` can be dropped safely.

**Primary recommendation:** Change five TFM strings, bump `Microsoft.Extensions.Logging.Abstractions` to `10.0.9`, drop `LangVersion=preview` from `SwtorLogParser.Cli` (Claude's discretion — clean C# 14 default), leave every other package as-is, then run the four gates (restore → build Release → test 106-green → AOT-publish).

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| TFM targeting (net10.0) | Build/SDK (MSBuild) | — | `<TargetFramework>` is an MSBuild property resolved by the installed SDK 10.x |
| Central package versions | Build/SDK (`Directory.Packages.props`) | — | CPM resolves versions centrally; csproj only lists `<PackageReference Include>` |
| Logging abstractions (`ILogger<T>`) | Core library | Native CLI / hosts | `Microsoft.Extensions.Logging.Abstractions` consumed in core + Native CLI |
| Reactive stream (DPS/HPS) | Core library | — | `System.Reactive` powers `CombatLogsMonitor.DpsHps`; hosts only subscribe |
| Native AOT compile | Native CLI host | Core library (`IsAotCompatible`) | ILCompiler runs at `dotnet publish` on the Native CLI; core lib must stay reflection-free |
| WinForms UI | Overlay host | Core library (View types) | `net10.0-windows` + `UseWindowsForms`; P/Invoke + DataGridView live only here |
| Test execution | Test host (VSTest/Test.Sdk) | Core library (SUT) | Test SDK runs the net10.0 test host; xunit is the framework |

## Standard Stack

### Core
| Library | Version (net10 GA) | Purpose | Why Standard |
|---------|--------------------|---------|--------------|
| Microsoft.Extensions.Logging.Abstractions | **10.0.9** | `ILogger<T>` / `NullLogger<T>` in core + Native CLI | Framework-aligned logging abstraction; ships true `net10.0` TFM with trim/AOT metadata `[VERIFIED: NuGet flat-container API]` |
| System.Reactive | **6.0.2** (keep) | Rx.NET — DPS/HPS/APM observable pipeline | Stable GA; `net6.0` baseline forward-resolves to net10.0 `[VERIFIED: nuspec dependency groups]` |

### Supporting
| Library | Version (keep) | Purpose | When to Use |
|---------|----------------|---------|-------------|
| Spectre.Console | 0.57.0 | Managed CLI table rendering | Latest stable; framework-agnostic `[VERIFIED: NuGet flat-container API]` |
| Microsoft.NET.Test.Sdk | 18.6.0 | VSTest host / `dotnet test` entry | Latest stable 18.x; runs net10.0 test host `[VERIFIED: NuGet flat-container API]` |
| xunit | 2.9.3 | Test framework (stay on 2.x) | Latest stable 2.x line; **do not migrate to xunit v3** (out of scope) `[VERIFIED: NuGet flat-container API]` |
| xunit.runner.visualstudio | 3.1.5 | VSTest adapter for xunit 2.x | Latest stable; 4.0.0 only in `-pre` `[VERIFIED: NuGet flat-container API]` |
| coverlet.collector | 6.0.4 (keep) | Coverage data collector | Works on net10.0; see Alternatives for the optional 10.0.1 bump `[VERIFIED: NuGet flat-container API]` |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Logging.Abstractions 10.0.9 | 10.0.0 … 10.0.8 | All GA; 10.0.9 is the current latest patch and matches the installed runtime (10.0.9) — pick the newest GA patch |
| System.Reactive 6.0.2 | System.Reactive 6.1.0 (GA) | 6.1.0 is a newer stable, but **6.0.2 is fine** — same `net6.0`/`netstandard2.0` baseline, no net10 benefit. CONTEXT says keep unless a newer GA is *warranted*; it is not. Keep 6.0.2 to minimize churn. |
| coverlet.collector 6.0.4 | coverlet.collector 10.0.1 (GA) | 10.0.1 exists as stable, but 6.0.4 already works on net10.0 and is in scope as "keep". Optional bump only if a coverage issue appears; not required. |
| xunit 2.9.3 | xunit v3 (`xunit.v3`) | **Out of scope** — v3 is a framework change (different packages/runner model). Stay on 2.x per CONTEXT. |
| `LangVersion=preview` (CLI) | drop the property (C# 14 default) | On net10 the default is C# 14 (superset of preview-12). Dropping `preview` removes a moving target; Claude's discretion, recommended. |

**Installation:** No new packages added. Edit `Directory.Packages.props` (one version change) + five `.csproj` TFM strings. CPM means versions live only in `Directory.Packages.props`.

**Version verification:** All versions confirmed against the authoritative NuGet flat-container registry (`https://api.nuget.org/v3-flatcontainer/<id>/index.json`) on 2026-06-12.

## Package Legitimacy Audit

> The `gsd-tools query package-legitimacy check` seam supports npm/PyPI/crates only — it does **not** cover NuGet. Every package below was verified directly against the authoritative NuGet flat-container API (the registry's own version index + nuspec dependency groups), which is the .NET equivalent authoritative source. No package was discovered via WebSearch or training data alone; all are pre-existing dependencies already in `Directory.Packages.props`.

| Package | Registry | Latest GA seen | net10 asset | Verdict | Disposition |
|---------|----------|----------------|-------------|---------|-------------|
| Microsoft.Extensions.Logging.Abstractions | NuGet | 10.0.9 (10.0.0–10.0.9 all GA) | true `net10.0` TFM | OK | Bump 8.0.3 → 10.0.9 |
| System.Reactive | NuGet | 6.1.0 (6.0.2 in use) | `net6.0`/`netstandard2.0` (forward-resolves) | OK | Keep 6.0.2 |
| Spectre.Console | NuGet | 0.57.0 (in use) | framework-agnostic | OK | Keep 0.57.0 |
| Microsoft.NET.Test.Sdk | NuGet | 18.6.0 (in use) | framework-agnostic | OK | Keep 18.6.0 |
| xunit | NuGet | 2.9.3 (latest 2.x, in use) | framework-agnostic | OK | Keep 2.9.3 |
| xunit.runner.visualstudio | NuGet | 3.1.5 (in use; 4.0 only `-pre`) | framework-agnostic | OK | Keep 3.1.5 |
| coverlet.collector | NuGet | 10.0.1 (6.0.4 in use) | framework-agnostic | OK | Keep 6.0.4 |

**Packages removed due to [SLOP] verdict:** none.
**Packages flagged as suspicious [SUS]:** none.
**Assumed (unverified) packages:** none — every package is an existing dependency verified on NuGet.

## Architecture Patterns

### System Architecture Diagram

```
                    Directory.Packages.props (CPM: ONE version per package id)
                                  │  resolves versions for
                                  ▼
  ┌──────────────────────────────────────────────────────────────────────┐
  │  SwtorLogParser  (net8.0 → net10.0, IsAotCompatible=true)             │
  │   • ILogger<T>/NullLogger<T>  ← M.E.Logging.Abstractions 10.0.9       │
  │   • System.Reactive (DpsHps IObservable<PlayerStats>)  6.0.2          │
  └──────────────────────────────────────────────────────────────────────┘
        ▲ ProjectReference          ▲ ProjectReference          ▲ ProjectReference          ▲ ProjectReference
        │                           │                           │                           │
 ┌──────┴───────┐         ┌─────────┴─────────┐       ┌─────────┴──────────┐      ┌──────────┴──────────┐
 │ .Cli         │         │ .Native.Cli       │       │ .Overlay           │      │ .Tests              │
 │ net8→net10   │         │ net8→net10        │       │ net8-win→net10-win │      │ net8→net10          │
 │ Spectre 0.57 │         │ PublishAot=true   │       │ UseWindowsForms    │      │ Test.Sdk/xunit      │
 │ LangVer drop │         │ ILogger<T>        │       │ DataGridView+P/Inv │      │ 106 tests           │
 └──────────────┘         └─────────┬─────────┘       └────────────────────┘      └─────────────────────┘
                                    │ dotnet publish
                                    ▼
                          ILCompiler (Native AOT)
                          GATE: zero IL2xxx/IL3xxx warnings
```

The four verification gates trace left-to-right through the build: `restore` (CPM resolves) → `build -c Release` (all 5 projects compile on net10) → `test` (106 green on net10 test host) → `publish` Native.Cli (AOT-clean code-gen).

### Recommended Project Structure
No structural change. Only these files are edited:
```
Directory.Packages.props                          # 1 version bump (Logging.Abstractions)
SwtorLogParser/SwtorLogParser.csproj              # net8.0 → net10.0
SwtorLogParser.Cli/SwtorLogParser.Cli.csproj      # net8.0 → net10.0 (+ optional drop LangVersion)
SwtorLogParser.Native.Cli/SwtorLogParser.Native.Cli.csproj  # net8.0 → net10.0
SwtorLogParser.Overlay/SwtorLogParser.Overlay.csproj        # net8.0-windows → net10.0-windows
SwtorLogParser.Tests/SwtorLogParser.Tests.csproj  # net8.0 → net10.0
```

### Pattern 1: Exact per-csproj TFM change list
**What:** A single MSBuild property edit per project; no SDK attribute or item changes.
**When to use:** This is the entire mechanical change.

| Project | Current line | New line | Other edits |
|---------|--------------|----------|-------------|
| `SwtorLogParser` | `<TargetFramework>net8.0</TargetFramework>` | `<TargetFramework>net10.0</TargetFramework>` | none — keep `IsAotCompatible=true` |
| `SwtorLogParser.Cli` | `<TargetFramework>net8.0</TargetFramework>` | `<TargetFramework>net10.0</TargetFramework>` | optional: remove `<LangVersion>preview</LangVersion>` (C# 14 default) |
| `SwtorLogParser.Native.Cli` | `<TargetFramework>net8.0</TargetFramework>` | `<TargetFramework>net10.0</TargetFramework>` | none — keep `PublishAot=true` |
| `SwtorLogParser.Overlay` | `<TargetFramework>net8.0-windows</TargetFramework>` | `<TargetFramework>net10.0-windows</TargetFramework>` | none — keep `UseWindowsForms=true` |
| `SwtorLogParser.Tests` | `<TargetFramework>net8.0</TargetFramework>` | `<TargetFramework>net10.0</TargetFramework>` | none |

`[VERIFIED: read of all 5 csproj files]` `[CITED: learn.microsoft.com/dotnet/core/compatibility/10 — "change net8.0 to net10.0"]`

### Pattern 2: One-line CPM version bump
**What:** Update only the framework-tied package in `Directory.Packages.props`.
**Example:**
```xml
<!-- Source: Directory.Packages.props (current) -->
<PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.3" />
<!-- After -->
<PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.9" />
```
All other `<PackageVersion>` lines stay unchanged.

### Anti-Patterns to Avoid
- **Bumping packages that don't need it.** Do not bump System.Reactive to 6.1.0 or coverlet to 10.0.1 "because newer exists" — CONTEXT says keep framework-agnostic GA packages unless a bump is warranted. Churn invites NU1605/regression risk for zero benefit.
- **Migrating xunit 2.x → v3.** Explicitly out of scope; v3 is a framework change.
- **Pinning per-project versions in csproj.** CPM is active (`ManagePackageVersionsCentrally=true`); versions belong only in `Directory.Packages.props`. Adding a `Version=` to a `<PackageReference>` triggers NU1008.
- **Adding a `global.json`.** No global.json exists; the latest installed SDK (10.0.301) rolls forward fine. Phase 7 (CI) owns SDK pinning, not this phase.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Detecting net10-incompatible packages | Manual asset spelunking | `dotnet restore` (NU1605/NU1701/NU1202 surface it) | The restore gate is the authoritative compatibility check |
| Catching AOT regressions | Eyeballing reflection code | `dotnet publish -c Release` AOT warnings (IL2xxx/IL3xxx) | ILCompiler analyzer is the source of truth for AOT-cleanliness |
| Verifying C# 14 default | Reading blog posts | Omit `LangVersion`; let the SDK pick the TFM default | net10 SDK auto-selects C# 14; explicit `preview` is a moving target |

**Key insight:** This phase's "correctness" is entirely tool-verified. Don't reason about compatibility — let `restore`, `build`, `test`, and `publish` assert it.

## Runtime State Inventory

> This is a TFM/refactor-adjacent phase (changes build identity, not stored data). Each category checked explicitly.

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | None — the app reads SWTOR `.txt`/`.ini` logs at runtime; no datastore keys/collections embed a TFM. | none |
| Live service config | None — no external service (no DB, no n8n, no cloud). `user32.dll` is a system DLL resolved by the OS, unaffected by TFM. | none |
| OS-registered state | None — no Task Scheduler entries, no services, no pm2; the app is launched directly. | none |
| Secrets/env vars | None — STACK.md confirms "No environment variables or external configuration files are read." | none |
| Build artifacts | `bin/`/`obj/` carry `net8.0`/`net8.0-windows` paths and will be stale after the TFM change. | `dotnet clean` (or delete `bin`/`obj`) before/after the bump so the AOT publish doesn't link stale net8 IL. No committed artifacts in repo. |

**The canonical question — "after every file is updated, what runtime systems still have net8.0 cached?":** only on-disk `bin`/`obj` build output. Clean them; nothing else persists a TFM.

## Common Pitfalls

### Pitfall 1: Stale net8 build output linked into the AOT publish
**What goes wrong:** `obj/` retains `net8.0` intermediate state; an incremental `publish` mixes net8 IL into the net10 native image or restore appears to "succeed" against cached assets.
**Why it happens:** MSBuild incremental build keys off timestamps, not TFM identity, across a TFM change.
**How to avoid:** `dotnet clean SwtorLogParser.slnx` (or delete `bin`/`obj`) immediately after editing TFMs, before the build/test/publish gates.
**Warning signs:** restore/build referencing `net8.0` paths in output; AOT warnings pointing at assemblies you didn't expect.

### Pitfall 2: NU1008 from leaving/adding a Version on a PackageReference
**What goes wrong:** Build fails with NU1008 ("Projects that use central package version management should not define the version on the PackageReference").
**Why it happens:** CPM is enabled; a stray `Version=` in a csproj conflicts.
**How to avoid:** Keep all versions in `Directory.Packages.props` only. The current csproj files already do this correctly — don't introduce a version attribute.
**Warning signs:** NU1008 at restore.

### Pitfall 3: NU1605 downgrade if a transitive pulls a newer M.E.* than pinned
**What goes wrong:** A package transitively requires `Microsoft.Extensions.*` ≥ 10.0.x while the direct pin is lower → downgrade error.
**Why it happens:** Bumping the direct Logging.Abstractions pin to 10.0.9 (matching the runtime) prevents this; leaving it at 8.0.3 on a net10 target is the real risk.
**How to avoid:** This is exactly why the locked decision bumps Logging.Abstractions to 10.0.x. Restore is the gate.
**Warning signs:** NU1605 at restore naming a Microsoft.Extensions.* package.

### Pitfall 4 (non-blocking): MSVC linker unavailable locally for AOT
**What goes wrong:** `dotnet publish` Native.Cli reaches the native link step and fails because the Visual C++ build tools / `link.exe` aren't installed locally.
**Why it happens:** Native AOT's final link needs the MSVC toolchain; the managed→IL→codegen stage (where AOT *warnings* appear) runs first and is what matters here.
**How to avoid:** Treat the gate as "zero IL2xxx/IL3xxx code-gen warnings." A link-step failure due to missing MSVC is a known local env gap (CONTEXT) — Phase 7 CI on `windows-latest` exercises the full link. Distinguish "AOT analyzer warning" (must be zero) from "linker not found" (env, deferred).
**Warning signs:** publish output shows clean ILCompiler analysis then errors at `link.exe`/`lld`.

## Code Examples

### Native library search behavior on AOT/single-file (net10 interop change — does NOT affect this app)
```text
// Source: learn.microsoft.com/dotnet/core/compatibility/interop/10.0/native-library-search
// .NET 10: "Single-file apps no longer look for native libraries in the executable directory."
// This codebase P/Invokes ONLY into user32.dll (a Windows SYSTEM library resolved via the OS
// search path), so this change has no effect. NativeMethods.cs:13,16 — [DllImport("user32.dll")].
```

### WinForms surface used by the Overlay (none of it is touched by net10 breaking changes)
```csharp
// Source: SwtorLogParser.Overlay/ParserForm.cs + SlidingExpirationList.cs + NativeMethods.cs
public class SlidingExpirationList : BindingList<Entry> { /* binds to DataGridView */ }
private DataGridView dataGridView;                    // DataGridViewTextBoxColumn, AutoSize* modes
[DllImport("user32.dll")] public static extern int SendMessage(IntPtr, int, int, int);
[DllImport("user32.dll")] public static extern bool ReleaseCapture();
// net10 WinForms breaking changes affect: WPF/WinForms MenuItem+ContextMenu disambiguation,
// HtmlElement.InsertAdjacentElement param rename, TreeView checkbox image, StatusStrip RenderMode,
// System.Drawing OutOfMemoryException→ExternalException. NONE of these APIs appear here.
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| net8.0 (default C# 12) | net10.0 (default **C# 14**) | .NET 10 GA, Nov 2025 | `LangVersion=preview` in the CLI can be dropped; C# 14 is the implicit default `[CITED: learn.microsoft.com/dotnet/csharp/whats-new/csharp-14]` |
| `Microsoft.Extensions.Logging.Abstractions` 8.0.3 | 10.0.9 GA | .NET 10 GA | True `net10.0` TFM with trim/AOT metadata; `ProviderAliasAttribute` now lives here (type-forwarded — no source change for `ILogger<T>` users) `[CITED: extensions/10.0/provideraliasattribute-moved-assembly]` |
| `IsAotCompatible` warns via existing IL30xx | net10 AOT tooling "more approachable," better warnings/coverage | .NET 10 | More thorough analysis may surface latent warnings, but this core lib is reflection-free; expect zero `[CITED: learn.microsoft.com native-aot]` |
| `dotnet restore` audits direct packages | net10 audits **transitive** packages too (NuGetAudit) | .NET 10 SDK | Restore may print NU1901-1904 advisories for transitive CVEs; informational, not a hard fail unless configured `[CITED: sdk/10.0/nugetaudit-transitive-packages]` |

**Deprecated/outdated:**
- Targeting net8.0 with an 8.0.x `Microsoft.Extensions.*` pin: superseded by net10.0 + 10.0.x.
- `LangVersion=preview`: unnecessary on net10 (C# 14 is the default).

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| — | (none) | — | All claims are tool-verified against NuGet/`dotnet` or cited from learn.microsoft.com. |

**This table is intentionally empty:** every factual claim was verified against the authoritative NuGet registry, the local `dotnet` SDK, the actual csproj/source files, or cited from official Microsoft documentation. No `[ASSUMED]` claims remain — no user confirmation required.

## Open Questions

1. **Should `LangVersion=preview` be dropped or kept on the managed CLI?**
   - What we know: net10 defaults to C# 14, a superset of preview-as-of-net8. Dropping it is safe and removes a moving target.
   - What's unclear: nothing functional — purely a style/discretion call (CONTEXT marks it Claude's discretion).
   - Recommendation: **Drop it.** If a build error somehow appears, re-add `<LangVersion>latest</LangVersion>` (not `preview`).

2. **Is the local MSVC linker present for the AOT publish gate?**
   - What we know: ILCompiler code-gen warnings are the in-scope signal; the native link needs MSVC tools that may be absent locally (CONTEXT flags this).
   - What's unclear: whether `link.exe` is installed on this dev box (not probed — out of this phase's necessity).
   - Recommendation: Define the AOT gate as "zero IL2xxx/IL3xxx warnings from code-gen." A missing-linker failure is acceptable locally and is fully exercised by Phase 7 CI on `windows-latest`.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK 10.x | All build/test/publish | ✓ | 10.0.108 / 10.0.204 / 10.0.300 / **10.0.301** | — |
| Microsoft.NETCore.App 10 runtime | net10.0 run/test | ✓ | 10.0.8, 10.0.9 | — |
| Microsoft.WindowsDesktop.App 10 | net10.0-windows Overlay | ✓ | 10.0.8, 10.0.9 | — |
| ILCompiler / Native AOT toolchain | Native.Cli AOT code-gen | ✓ (in SDK 10) | bundled | — |
| MSVC / `link.exe` (native link) | Native.Cli final AOT link | ✗ (assumed; not probed) | — | Phase 7 CI `windows-latest` runs full link; local gate = code-gen warnings only |

`[VERIFIED: dotnet --list-sdks / dotnet --list-runtimes on 2026-06-12]`

**Missing dependencies with no fallback:** none.
**Missing dependencies with fallback:** MSVC native linker (fallback: CI does the full link; local gate verifies AOT-cleanliness via warnings).

## Validation Architecture

> `workflow.nyquist_validation` is `true` in `.planning/config.json` — section included.

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 + Microsoft.NET.Test.Sdk 18.6.0 (VSTest) |
| Config file | none — versions in `Directory.Packages.props`; runner via `xunit.runner.visualstudio` 3.1.5 |
| Quick run command | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj -c Release` |
| Full suite command | `dotnet test SwtorLogParser.slnx -c Release` (or the Tests csproj — only project with tests) |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| PLAT-01 | Restore resolves cleanly on net10 (no NU1605/NU1008) | gate | `dotnet restore SwtorLogParser.slnx` | ✅ (slnx) |
| PLAT-01 | All 5 projects build Release on net10 | gate | `dotnet build SwtorLogParser.slnx -c Release` | ✅ |
| PLAT-01 | All 106 existing tests pass on net10 host, zero skips | regression | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj -c Release` | ✅ (suite exists, Phase 1–4) |
| PLAT-01 | Native CLI AOT code-gen is clean (zero IL2xxx/IL3xxx) | gate | `dotnet publish SwtorLogParser.Native.Cli -c Release` | ✅ (PublishAot=true) |

### Sampling Rate
- **Per task commit:** `dotnet build SwtorLogParser.slnx -c Release` (fast compile check after each TFM edit).
- **Per wave merge:** `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj -c Release` (106 green).
- **Phase gate:** all four gates green — restore, build Release, test (106/0 skips), AOT publish (zero IL warnings) — before `/gsd-verify-work`.

### Wave 0 Gaps
- None — existing test infrastructure (106 tests, hermetic per Phase 3 `ICombatLogSource` seam, deterministic `[Collection]`) covers all phase requirements. No new tests are needed; this phase re-runs the existing suite on net10. Framework already installed (Test.Sdk 18.6.0).

## Security Domain

> `security_enforcement: true`, `security_asvs_level: 1` in config — section included.

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | No auth surface — local desktop log reader |
| V3 Session Management | no | No sessions |
| V4 Access Control | no | No multi-user/authorization |
| V5 Input Validation | partial (existing) | Parser already returns null on malformed input (BUG-05, TEST-03); this phase changes no parsing code |
| V6 Cryptography | no | No crypto used |
| V14 Configuration / Dependencies | **yes** | All packages on stable GA (DEP-01); net10 restore adds **transitive** NuGetAudit (NU1901-1904) — review any advisory the upgrade surfaces |

### Known Threat Patterns for this stack

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Vulnerable transitive dependency pulled by net10 resolution | Tampering / EoP | net10 `dotnet restore` transitive audit surfaces NU1901-1904; review advisories at the restore gate |
| Untrusted log-file content (path/format) | Tampering | Files opened read-only (BUG-07); parser is null-tolerant (BUG-05) — unchanged by this phase |
| Native P/Invoke (`user32.dll`) misuse | EoP | Two fixed system-DLL imports, no dynamic library loading; net10 single-file native-search change does not affect system DLLs |

**Phase-specific security note:** This is a runtime/TFM upgrade with no code-behavior change, so it introduces no new attack surface. The one net-new security-relevant signal is the net10 transitive NuGetAudit at restore — treat any NU190x advisory it prints as an item to review (informational unless configured to fail).

## Sources

### Primary (HIGH confidence)
- NuGet flat-container API (`api.nuget.org/v3-flatcontainer/<id>/index.json` + nuspec) — version indices & dependency TFMs for: Microsoft.Extensions.Logging.Abstractions (10.0.9 GA), System.Reactive (6.0.2/6.1.0, net6.0 baseline), Spectre.Console (0.57.0), Microsoft.NET.Test.Sdk (18.6.0), xunit (2.9.3), xunit.runner.visualstudio (3.1.5), coverlet.collector (6.0.4/10.0.1).
- `dotnet --list-sdks` / `dotnet --list-runtimes` (local, 2026-06-12) — SDK 10.0.301, NETCore.App 10.0.9, WindowsDesktop.App 10.0.9 present; no SDK 8.
- Read of all 5 `.csproj`, `Directory.Packages.props`, `SwtorLogParser.slnx`, `NativeMethods.cs`, `ParserForm.cs`, `SlidingExpirationList.cs`.
- learn.microsoft.com — Breaking changes in .NET 10 (full table: Core libraries, WinForms, Interop, SDK, Extensions).
- learn.microsoft.com — ProviderAliasAttribute moved to Logging.Abstractions (type-forwarded, .NET 10).

### Secondary (MEDIUM confidence)
- learn.microsoft.com — What's new in C# 14 / language versioning (net10 default = C# 14).
- learn.microsoft.com — Native AOT deployment overview & "Introduction to AOT warnings" (IL2xxx/IL3xxx categories).

### Tertiary (LOW confidence)
- General .NET 8→10 migration commentary (Visual Studio Magazine, blogs) — used only to corroborate "few breaking changes"; not relied on for any specific claim.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — every version verified against the authoritative NuGet registry; TFMs read from actual csproj.
- Architecture: HIGH — mechanical TFM change; tier map derived from read source + ProjectReference graph.
- Pitfalls: HIGH — derived from the official .NET 10 breaking-change list cross-referenced against actual code usage (zero applicable WinForms/AOT breaking changes).

**Research date:** 2026-06-12
**Valid until:** 2026-07-12 (stable LTS; revisit only if a newer 10.0.x patch is desired or the test stack ships a net10-targeting GA bump).
