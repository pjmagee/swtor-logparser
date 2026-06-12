---
phase: 08-winui-3-scaffold-dependencies-guardrails
reviewed: 2026-06-12T00:00:00Z
depth: standard
files_reviewed: 10
files_reviewed_list:
  - .github/workflows/ci.yml
  - Directory.Packages.props
  - SwtorLogParser.Overlay.WinUi/App.xaml
  - SwtorLogParser.Overlay.WinUi/App.xaml.cs
  - SwtorLogParser.Overlay.WinUi/MainWindow.xaml
  - SwtorLogParser.Overlay.WinUi/MainWindow.xaml.cs
  - SwtorLogParser.Overlay.WinUi/NativeMethods.txt
  - SwtorLogParser.Overlay.WinUi/SwtorLogParser.Overlay.WinUi.csproj
  - SwtorLogParser.Overlay.WinUi/app.manifest
  - SwtorLogParser.slnx
findings:
  critical: 0
  warning: 3
  info: 3
  total: 6
status: issues_found
---

# Phase 8: Code Review Report

**Reviewed:** 2026-06-12
**Depth:** standard
**Files Reviewed:** 10
**Status:** issues_found

## Summary

Phase 8 is a scaffold-only deliverable: a new unpackaged self-contained WinUI 3
overlay project that opens an empty window, two CPM dependency pins
(Microsoft.WindowsAppSDK 2.2.0, Microsoft.Windows.CsWin32 0.3.275), and a CI
comment update. The intentional emptiness (empty `MainWindow`, default
`App.OnLaunched`, no stream/interop) is in scope and not flagged.

Reviewed adversarially, the code-behind is clean and the CPM pins are wired
correctly (CsWin32 has the required `PrivateAssets=all` + asset isolation; both
pins live in `Directory.Packages.props`, not in the Native AOT graph). The real
defects are in the **build-matrix configuration**: the project's declared
`Platforms` / `RuntimeIdentifiers` do not cover every solution platform the
`.slnx` advertises, the RID-selection conditions silently produce an
arch-mismatched build for the `x86` platform, and a CsWin32 P/Invoke is being
generated for a method nothing consumes. None are crashes or security issues, so
no Critical findings — but the platform-matrix gaps are latent build breaks the
moment someone selects a non-default solution platform, which the Phase 8
verification (a default `Any CPU` build) would not have exercised.

I could not empirically restore `Microsoft.WindowsAppSDK 2.2.0` in this
environment to confirm the version resolves; the Plan 02 SUMMARY asserts a green
`-c Release` build, so version existence is taken as verified and not flagged.

## Warnings

### WR-01: WinUI project omits `x86` (and `Any CPU`) platforms the `.slnx` declares

**File:** `SwtorLogParser.Overlay.WinUi/SwtorLogParser.Overlay.WinUi.csproj:8` and `SwtorLogParser.slnx:2-7`
**Issue:** The solution (`.slnx`) advertises four platforms: `Any CPU`, `arm64`,
`x64`, `x86`. The WinUI project declares only `<Platforms>x64;arm64</Platforms>`.
There is no project mapping for `x86`, and `Any CPU` is also absent. A
`dotnet build SwtorLogParser.slnx -c Release /p:Platform=x86` (or building the
`x86` solution configuration from an IDE) has no valid project platform to map to
for the WinUI project. Because `WindowsAppSDKSelfContained=true` hard-requires a
single resolved architecture RID, this is not a benign no-op: the build either
fails to map or falls through to the RID conditions in WR-02. The Phase 8
verification only exercised the default (`Any CPU`) path, so this gap is unproven
either way and ships latent.
**Fix:** Either restrict the solution to the architectures the WinUI project
actually supports, or declare every solution platform on the project and map the
unsupported ones explicitly. Minimal option — drop `x86` from the WinUI mapping
intentionally and document it, or add an `x86` guard that fails loudly:
```xml
<!-- WinUI 3 self-contained does not support x86; fail fast instead of mis-RID. -->
<Target Name="BlockX86" BeforeTargets="Build"
        Condition="'$(Platform)' == 'x86'">
  <Error Text="SwtorLogParser.Overlay.WinUi does not support the x86 platform." />
</Target>
```

### WR-02: RID-selection conditions produce an arch-mismatched build for non-arm64 platforms

**File:** `SwtorLogParser.Overlay.WinUi/SwtorLogParser.Overlay.WinUi.csproj:14-15`
**Issue:** The default RID logic is:
```xml
<RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == '' and '$(Platform)' != 'arm64'">win-x64</RuntimeIdentifier>
<RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == '' and '$(Platform)' == 'arm64'">win-arm64</RuntimeIdentifier>
```
The first condition matches **any** non-arm64 platform when no RID is supplied —
including `x86`. So building the `x86` solution platform yields
`Platform=x86` + `RuntimeIdentifier=win-x64`, an architecture mismatch for a
self-contained app (the manifest/platform says x86 while the runtime payload is
x64). The `!=` test treats "x86", "AnyCPU", and "x64" identically, which is only
correct for the latter two. This is the concrete failure mode behind WR-01: the
condition does not fail, it silently picks the wrong arch.
**Fix:** Make the default explicit per supported platform and let unsupported
platforms fall through with no RID (so the build errors clearly rather than
mis-building):
```xml
<RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == '' and ('$(Platform)' == 'x64' or '$(Platform)' == 'Any CPU' or '$(Platform)' == 'AnyCPU')">win-x64</RuntimeIdentifier>
<RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == '' and '$(Platform)' == 'arm64'">win-arm64</RuntimeIdentifier>
```

### WR-03: CsWin32 generates an unused `GetForegroundWindow` P/Invoke in a no-interop scaffold

**File:** `SwtorLogParser.Overlay.WinUi/NativeMethods.txt:1`
**Issue:** `NativeMethods.txt` lists `GetForegroundWindow`, which makes the
CsWin32 source generator emit a P/Invoke wrapper. Per the phase scope there is
intentionally no interop yet (interop lands in Phase 10), and nothing in
`App.xaml.cs` / `MainWindow.xaml.cs` calls it. This is generated dead code: it
either produces an unused-member warning surface or, more importantly,
contradicts the stated "no interop in Phase 8" boundary the AOT-contamination
gate is meant to protect. It also pre-commits to a specific API before Phase 10
has designed the interop surface.
**Fix:** Remove the entry until Phase 10 introduces the first real consumer, so
the scaffold generates nothing:
```
# NativeMethods.txt — empty until Phase 10 wires the first P/Invoke consumer.
```
If CsWin32 is pinned now purely to validate the package resolves (the stated
guardrail), keep the `PackageReference` but leave `NativeMethods.txt` empty.

## Info

### IN-01: NuGet cache key cannot invalidate on a transitive/SDK version change

**File:** `.github/workflows/ci.yml:32`
**Issue:** The cache key is
`hashFiles('**/*.csproj', 'Directory.Packages.props')`. With no
`packages.lock.json` in the repo, the restored transitive graph is not part of
the key, so a floating dependency (e.g. anything resolved by `10.0.x` SDK
servicing) can serve a stale cache. The inline comment acknowledges the missing
lockfile but the consequence (non-deterministic restore that the cache can mask)
is worth recording. Low impact for a pinned-CPM repo, but it is a correctness
caveat rather than a style nit.
**Fix:** Generate and commit `packages.lock.json`
(`<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>`) and add it to
the cache `hashFiles` set, or accept the non-determinism explicitly.

### IN-02: `Microsoft.WindowsAppSDK` pin lacks asset/transitive isolation metadata

**File:** `SwtorLogParser.Overlay.WinUi/SwtorLogParser.Overlay.WinUi.csproj:26`
**Issue:** The `Microsoft.WindowsAppSDK` reference has no `PrivateAssets` /
`IncludeAssets` control, unlike the CsWin32 reference next to it. For a leaf
`WinExe` this is functionally fine (the SDK is a genuine runtime dependency and
the project is referenced by no other project). It is noted only because the
phase's central guardrail is "WinAppSDK must not leak into the Native AOT graph";
relying on the absence of a ProjectReference rather than explicit metadata makes
that boundary implicit. The Plan 02 SUMMARY confirms no leak today, so this is
informational.
**Fix:** No action required while nothing references this project. If a future
phase adds a `ProjectReference` to it, add `PrivateAssets` to keep the SDK from
flowing transitively.

### IN-03: `App._window` field never disposed/closed; no shutdown wiring

**File:** `SwtorLogParser.Overlay.WinUi/App.xaml.cs:11,20`
**Issue:** `_window` is assigned in `OnLaunched` and never closed; there is no
`Window.Closed` handler to drive `Application.Current.Exit()` or to release the
reference. For a single-window scaffold this is the standard WinUI template shape
and harmless now, but once Phase 9 attaches an Rx subscription to this window,
the missing close/teardown hook becomes the natural place a subscription leaks.
Flagging now so the teardown seam is added with the first subscription rather
than retrofitted.
**Fix:** No change required for Phase 8. In Phase 9, wire
`_window.Closed += (_, _) => { /* dispose subscriptions */ };` when the stream is
attached.

---

_Reviewed: 2026-06-12_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
