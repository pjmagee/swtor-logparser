# Technology Stack

**Analysis Date:** 2026-06-11

## Languages

**Primary:**
- C# (LangVersion `preview` in `SwtorLogParser.Cli`, default latest elsewhere) - All application and library code across the five projects.

**Secondary:**
- INI / plain-text parsing (no separate language) - SWTOR combat log `.txt` files and `*PlayerGUIState.ini` settings files are parsed manually using `ReadOnlySpan<char>` / `ReadOnlyMemory<char>`.

## Runtime

**Environment:**
- .NET 8.0 (`net8.0`) for core library, CLI, Native CLI, and Tests.
- .NET 8.0 Windows (`net8.0-windows`) for the WinForms overlay (`SwtorLogParser.Overlay`).
- Native AOT compilation enabled for `SwtorLogParser.Native.Cli` (`PublishAot=true`) - compiles to native code with no .NET runtime required. Core library is marked `IsAotCompatible=true` (no reflection).

**Package Manager:**
- NuGet (PackageReference style).
- Lockfile: missing (no `packages.lock.json` present). No `global.json` pinning the SDK version.

## Frameworks

**Core:**
- Microsoft.Extensions.DependencyInjection `8.0.0-preview.5.23280.8` - DI container (referenced in core library `SwtorLogParser`).
- Microsoft.Extensions.Logging.Console / .Debug `8.0.0-preview.5.23280.8` - Logging providers (core library).
- Microsoft.Extensions.Logging.Abstractions `8.0.0-preview.5.23280.8` - Logging abstractions (Native CLI).
- System.Reactive `6.0.1-preview.1` - Rx.NET; powers DPS/HPS/APM streaming calculations via observables (`CombatLogsMonitor.DpsHps`).
- System.CommandLine `2.0.0-beta4.22272.1` - Command-line parsing (Native CLI: `monitor`, `list` commands).
- System.CommandLine.Rendering `0.4.0-alpha.22272.1` - Console table rendering (managed CLI `SwtorLogParser.Cli`).
- Windows Forms (`UseWindowsForms=true`) - Overlay UI (`SwtorLogParser.Overlay`).

**Testing:**
- xUnit `2.5.0-pre.44` - Test framework.
- xunit.runner.visualstudio `2.5.0-pre.27` - Test runner/adapter.
- Microsoft.NET.Test.Sdk `17.7.0-preview.23280.1` - Test SDK.
- coverlet.collector `6.0.0` - Code coverage collection.

**Build/Dev:**
- Microsoft.NET.Sdk - Standard SDK-style project build.
- Native AOT toolchain (for `SwtorLogParser.Native.Cli`).

> Note: Most Microsoft and System packages are pinned to preview/alpha/beta versions. See `INTEGRATIONS.md` and `CONCERNS` for risk implications.

## Key Dependencies

**Critical:**
- System.Reactive `6.0.1-preview.1` - Core to real-time DPS/HPS/APM stats pipeline.
- System.CommandLine (+ Rendering) - CLI command structure and output rendering.

**Infrastructure:**
- Microsoft.Extensions.* (DI + Logging) - Cross-cutting infrastructure in core library.

## Configuration

**Environment:**
- No environment variables or external configuration files are read by the application.
- No `.env`, `appsettings.json`, or secret files present.
- File locations are derived at runtime from OS special folders (see Platform Requirements and `INTEGRATIONS.md`).

**Build:**
- Per-project `.csproj` files; solution `SwtorLogParser.sln`.
- `ImplicitUsings` enabled across all projects; `Nullable` enabled across all projects.
- `DockerDefaultTargetOS=Linux` set on CLI projects (no Dockerfile present in repo).

## Platform Requirements

**Development:**
- .NET 8.0 SDK.
- Windows required to build/run the Overlay (`net8.0-windows` + WinForms) and to resolve the real SWTOR log/settings paths.
- P/Invoke into `user32.dll` for the overlay window dragging (`SwtorLogParser.Overlay/NativeMethods.cs`).

**Production:**
- Windows desktop (SWTOR client machine). The parser reads logs from the local `My Documents\Star Wars - The Old Republic\CombatLogs` folder.
- Native CLI produces a self-contained native executable (no runtime install needed).

---

*Stack analysis: 2026-06-11*
