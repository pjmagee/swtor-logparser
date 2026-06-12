# Stack Research

**Domain:** Windows-native desktop overlay + dev/test tooling for an existing .NET 10 app (v1.1 milestone — NEW stack pieces only)
**Researched:** 2026-06-12
**Confidence:** HIGH

> Scope note: This file covers ONLY the four NEW v1.1 stack pieces (WinUI 3 / Windows App SDK overlay, CsWin32 interop, MSTest SDK test project, VSCode debug tooling). The existing validated stack (.NET 10 LTS, `SwtorLogParser` core lib `IsAotCompatible=true`, `System.Reactive` 6.0.2, Spectre.Console, Native AOT CLI, Central Package Management via `Directory.Packages.props`) is treated as fixed and is NOT re-researched.

---

## Recommended Stack

### Core Technologies (NEW for v1.1)

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| `Microsoft.WindowsAppSDK` | **2.2.0** (released 2026-06-09; latest stable) | WinUI 3 UI framework + Windows App SDK runtime for the new transparent overlay host, replacing WinForms | First major release on true SemVer 2.0.0 (2.0 = successor to the 1.8.x date-stamped line). Modern Windows-native XAML UI, full WinRT compositor access (needed for transparency/click-through), still supports **unpackaged self-contained** desktop apps — exactly the developer-tool model this project needs. Aligns with the project's deliberate Windows-only stance. |
| `Microsoft.Windows.CsWin32` | **0.3.275** (latest stable) | Source-generated, type-safe Win32 P/Invoke (user32: `SetWindowPos`, `GetWindowLongPtr`/`SetWindowLongPtr`, `SendMessage`, `ReleaseCapture`, window-style constants) — replaces hand-written `NativeMethods.cs` | Generates only the APIs you name in `NativeMethods.txt`, with correct signatures, marshalling, and architecture-aware constants, **no runtime dependency** (pure compile-time SatG). Eliminates the entire class of hand-marshalling bugs and is the Microsoft-blessed replacement for `[DllImport]` boilerplate. Closes issue #3. |
| `MSTest.Sdk` (project SDK) | **4.2.3** (released 2026-05-14; latest stable) | Single-line test-project SDK (`<Project Sdk="MSTest.Sdk/4.2.3">`) replacing the xUnit + `Microsoft.NET.Test.Sdk` + runner + coverlet package soup | Collapses test-project config to one SDK attribute; defaults to the modern **Microsoft.Testing.Platform (MTP)** runner (faster, self-contained `dotnet run`-able test exe, no VSTest host). Bundles MSTest framework + adapter + coverage. Closes issue #2 (xUnit → MSTest). |
| VSCode **C# Dev Kit** + **C#** extensions | C# Dev Kit (Microsoft) + base C# ext (Roslyn/`coreclr` debugger) — install latest from Marketplace | `launch.json`/`tasks.json` debugging for all hosts (console CLIs, AOT exe, WinUI 3 overlay) | The `coreclr` debug type ships in the base C# extension; C# Dev Kit adds project/solution awareness and the `dotnet` launch type. This is the standard, supported VSCode .NET debug stack. (Extensions, not NuGet packages — no CPM entry.) |

### Supporting Libraries / Sub-packages

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `Microsoft.WindowsAppSDK.*` sub-packages (Foundation, WinUI, …) | transitively via 2.2.0 metapackage | Component implementations | Do NOT reference individually — install the single `Microsoft.WindowsAppSDK` metapackage; it pulls in WinUI + Foundation. |
| `coverlet.collector` | 6.0.4 (already pinned) | Code coverage | Optional with MTP; MSTest.Sdk has its own coverage support. Keep only if you want the existing collector flow. See "What NOT to Use". |

### Development Tools

| Tool | Purpose | Notes |
|------|---------|-------|
| VSCode `coreclr` launcher | Debug console CLIs + WinUI 3 overlay | Standard `"type": "coreclr"` config with `preLaunchTask` → build task. Works on the managed overlay (it is JIT/managed, not AOT). |
| VSCode `dotnet` launch type (C# Dev Kit) | Zero-config debug from project metadata | Preferred when C# Dev Kit is installed; omit `launch.json` or set `"type": "dotnet"`. |
| AOT exe debugging | Debug the published Native AOT CLI | `coreclr` cannot debug a native AOT binary as managed; for AOT, either (a) debug the **non-AOT** build of `Native.Cli` with `coreclr`, or (b) attach a native debugger. Recommend debugging the JIT build for day-to-day work and treating AOT as a publish/CI verification step only. |
| `dotnet test` / `dotnet run` (MTP) | Run MSTest suite | With MSTest.Sdk the test project is directly runnable; CI keeps `dotnet test`. |

## Installation / Project Shapes

**Overlay project (`SwtorLogParser.Overlay.csproj`) — unpackaged, self-contained WinUI 3:**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
    <RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
    <UseWinUI>true</UseWinUI>
    <EnableMsixTooling>true</EnableMsixTooling>
    <WindowsPackageType>None</WindowsPackageType>        <!-- UNPACKAGED: auto-init bootstrapper -->
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained> <!-- no separate runtime install on user machines -->
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>           <!-- required by CsWin32-generated code -->
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" />
    <PackageReference Include="Microsoft.Windows.CsWin32">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <ProjectReference Include="..\SwtorLogParser\SwtorLogParser.csproj" />
  </ItemGroup>
</Project>
```

`NativeMethods.txt` (drives CsWin32; one API/const per line):
```
SetWindowPos
GetWindowLongPtr
SetWindowLongPtr
SendMessage
ReleaseCapture
WS_EX_LAYERED
WS_EX_TRANSPARENT
WS_EX_TOPMOST
WS_EX_TOOLWINDOW
GWL_EXSTYLE
HWND_TOPMOST
SWP_NOMOVE
SWP_NOSIZE
SWP_NOACTIVATE
WM_NCLBUTTONDOWN
HTCAPTION
```

**Test project (`SwtorLogParser.Tests.csproj`) — MSTest SDK:**

```xml
<Project Sdk="MSTest.Sdk/4.2.3">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\SwtorLogParser\SwtorLogParser.csproj" />
  </ItemGroup>
</Project>
```

**Central Package Management additions (`Directory.Packages.props`):**

```xml
<PackageVersion Include="Microsoft.WindowsAppSDK" Version="2.2.0" />
<PackageVersion Include="Microsoft.Windows.CsWin32" Version="0.3.275" />
```

> CPM caveat: an SDK-style project (`Sdk="MSTest.Sdk/4.2.3"`) pins the **SDK** version in the `Sdk` attribute, NOT via `PackageVersion`. The MSTest.Sdk version is therefore declared inline in the test `.csproj`, not in `Directory.Packages.props`. Remove the now-unused `xunit`, `xunit.runner.visualstudio`, and (optionally) `Microsoft.NET.Test.Sdk` / `coverlet.collector` `PackageVersion` entries once migration is complete. Keep `Microsoft.NET.Test.Sdk` only if any project still uses the VSTest host.

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|-------------------------|
| WinUI 3 (Windows App SDK 2.x) | .NET MAUI | If cross-platform were ever desired — but it is explicitly OUT of scope; MAUI's better AOT story is moot since the overlay is not AOT-constrained. |
| WinUI 3 | WPF | WPF is mature and simpler for a transparent overlay, but the milestone is a deliberate move to modern Windows-native UI (issue #4, user directive). WPF would be the fallback if WinUI 3 transparency/click-through proves too fiddly. |
| WinUI 3 | Keep WinForms | Status quo; rejected by the milestone goal. |
| CsWin32 | Hand-written `[DllImport]` (current) | Only if a needed API isn't in the Win32 metadata (rare for user32). CsWin32 is strictly better for the user32 surface here. |
| CsWin32 | `PInvoke.User32` / `Vanara` | Pre-baked wrapper libs add a runtime dependency; CsWin32 generates exactly what's named with zero runtime dep. |
| MSTest.Sdk | Keep xUnit, or TUnit | xUnit works fine; migration is a user directive (issue #2). TUnit is newer/MTP-native but less established — MSTest.Sdk is the Microsoft-supported single-SDK path. |
| Unpackaged self-contained | Packaged (MSIX) | Choose MSIX only if Store distribution or package identity is needed — it is NOT (out of scope). Unpackaged keeps this a simple developer tool: build → run an `.exe`. |

## What NOT to Use / What NOT to Add

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| Adding WinUI 3 / Windows App SDK to the **core library or the Native AOT CLI** | Would couple the AOT-clean core to a heavy Windows-UI dependency and risk AOT-compat (`IsAotCompatible=true` invariant) | Keep WinAppSDK isolated to `SwtorLogParser.Overlay` only; core stays untouched. |
| `WindowsPackageType` left unset / packaged config | Defaults/MSIX path needs identity + deployment ceremony not wanted for a dev tool | `<WindowsPackageType>None</WindowsPackageType>` (unpackaged, auto-init bootstrapper) |
| Manual bootstrapper API calls (`MddBootstrapInitialize`) | Unnecessary complexity; auto-init handles it | Auto-initialization via `WindowsPackageType=None` generates `MddBootstrapAutoInitializer.cs` |
| Individual `Microsoft.WindowsAppSDK.*` sub-packages | Version-skew risk | The single `Microsoft.WindowsAppSDK` metapackage |
| `Microsoft.NET.Test.Sdk` + `xunit*` packages alongside MSTest.Sdk | Redundant / two runners (VSTest + MTP) competing | MSTest.Sdk alone (MTP runner). Drop xUnit packages from CPM. |
| Targeting WinUI 3 overlay for Native AOT | Not needed (overlay is a managed host) and adds risk | Leave overlay as normal managed JIT; AOT stays scoped to `Native.Cli`. |
| Injecting a year into VSCode debug or NuGet searches | n/a | Verify versions on NuGet/Learn directly |

## WinUI 3 / Windows App SDK + Native AOT — explicit statement

- **The overlay is NOT AOT-constrained.** Per PROJECT.md, AOT applies only to `SwtorLogParser.Native.Cli`; the WinUI 3 overlay is a normal managed (JIT) host. So the AOT question does not gate this milestone.
- **For the record:** As of the **May 2026** Windows App SDK update, WinUI 3 has first-class Native AOT support, but it is still maturing (simple/moderate apps compile; complex reflection scenarios need fallbacks). The overlay does **not** need it and should not opt in during v1.1.
- **Core library invariant preserved:** `SwtorLogParser` (`IsAotCompatible=true`) gains NO new dependency from any v1.1 work — WinAppSDK and CsWin32 live only in the overlay project; MSTest.Sdk lives only in the test project. No conflict with the AOT-clean core or the Native AOT CLI.

## Version Compatibility

| Package A | Compatible With | Notes |
|-----------|-----------------|-------|
| `Microsoft.WindowsAppSDK` 2.2.0 | .NET 10 (`net10.0-windows10.0.19041.0`) | WinUI 3 requires a Windows OS-versioned TFM; `10.0.19041.0` (Win10 2004) is the standard target, min OS `10.0.17763.0` (Win10 1809). Self-contained avoids a separate runtime install on user machines. |
| `Microsoft.Windows.CsWin32` 0.3.275 | C# / .NET 10, `AllowUnsafeBlocks=true` | Source generator; emits `unsafe` interop → `AllowUnsafeBlocks` is mandatory. Zero runtime dependency, so no CPM/runtime conflict anywhere. |
| `MSTest.Sdk` 4.2.3 | .NET 8+ (incl. .NET 10), MTP runner | Coexists with `coverlet.collector` but you generally won't need `Microsoft.NET.Test.Sdk` (VSTest) once on MTP. SDK version is set in the `Sdk` attribute, not CPM. |
| VSCode C#/C# Dev Kit | `coreclr` debugger, .NET 10 | Debugs managed hosts incl. the WinUI 3 overlay; AOT-published exe is debugged via its JIT build or a native debugger. |

## Integration Points (for roadmapper)

1. New `Microsoft.WindowsAppSDK` + `Microsoft.Windows.CsWin32` `PackageVersion` entries in `Directory.Packages.props`.
2. Rewrite `SwtorLogParser.Overlay.csproj`: drop `UseWindowsForms`; add WinUI 3 / unpackaged / self-contained knobs + `AllowUnsafeBlocks`; reference both new packages.
3. Add `NativeMethods.txt`; delete hand-written `NativeMethods.cs`; rewrite drag + topmost/click-through logic against CsWin32-generated APIs (carry the BL-01 topmost-over-borderless fix here).
4. Convert `SwtorLogParser.Tests.csproj` to `Sdk="MSTest.Sdk/4.2.3"`; migrate 106 tests xUnit→MSTest attributes (`[Fact]`→`[TestMethod]`, `Assert.*` API differences); remove xUnit CPM entries.
5. Add `.vscode/launch.json` (configs per host) + `tasks.json` (build / `dotnet test` / AOT publish).
6. Update CI: ensure the build agent has the Windows App SDK workload / the self-contained build resolves; `dotnet test` runs under MTP.

## Sources

- [NuGet — Microsoft.WindowsAppSdk](https://www.nuget.org/packages/Microsoft.WindowsAppSdk/) — latest stable 2.2.0 (2026-06-09); 2.1.3, 2.0.1; 1.8.x servicing line. HIGH
- [Microsoft Learn — Windows App SDK 2.0 release notes](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0) — SemVer 2.0.0, successor to 1.8, all four deploy scenarios (SelfContained/FrameworkDependent × Packaged/Unpackaged). HIGH
- [Microsoft Learn — Use the Windows App SDK in an existing project](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/use-windows-app-sdk-in-existing-project) — unpackaged config: `WindowsPackageType=None` auto-init bootstrapper; runtime install/self-contained guidance. HIGH
- [NuGet — Microsoft.Windows.CsWin32](https://www.nuget.org/packages/Microsoft.Windows.CsWin32) — latest stable 0.3.275; `NativeMethods.txt`-driven, zero runtime dep, `PrivateAssets=all`. HIGH
- [NuGet — MSTest.Sdk](https://www.nuget.org/packages/MSTest.Sdk) — latest stable 4.2.3 (2026-05-14); 4.2.x / 4.1.0 history. HIGH
- [Microsoft Learn — MSTest SDK configuration](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-sdk) — `Sdk="MSTest.Sdk/x.y.z"` model; defaults to MTP runner. HIGH
- [Microsoft Learn — Microsoft.Testing.Platform overview](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-intro) — MTP vs VSTest; .NET 8+ support. HIGH
- [WinUI 3 AOT status — Windows Forum / Build 2026 coverage](https://windowsforum.com/threads/build-2026-winui-3-windows-app-sdk-and-ai-agents-push-native-windows-apps.422225/) — May 2026 first-class AOT support, still maturing. MEDIUM
- [VS Code — C# debugger settings](https://code.visualstudio.com/docs/csharp/debugger-settings) — `coreclr` launch config; C# Dev Kit `dotnet` launch type. HIGH

---
*Stack research for: WinUI 3 overlay + dev/test tooling (v1.1 milestone, additive to existing .NET 10 stack)*
*Researched: 2026-06-12*
