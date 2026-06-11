# Phase 5: Dependency Upgrades - Pattern Map

**Mapped:** 2026-06-12
**Files analyzed:** 7 (5 csproj + 2 Program.cs) + 1 new (Directory.Packages.props)
**Analogs found:** 0 in-repo for Directory.Packages.props / Spectre.Console (canonical examples supplied); in-repo analog for hand-rolled dispatch is the two existing Program.cs files

## File Classification

| Modified/New File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `Directory.Packages.props` (NEW, repo root) | config | transform | none in repo | canonical example |
| `SwtorLogParser/SwtorLogParser.csproj` | config | transform | self | exact (edit in place) |
| `SwtorLogParser.Cli/SwtorLogParser.Cli.csproj` | config | transform | self | exact |
| `SwtorLogParser.Native.Cli/SwtorLogParser.Native.Cli.csproj` | config | transform | self | exact |
| `SwtorLogParser.Overlay/SwtorLogParser.Overlay.csproj` | config | transform | self | exact |
| `SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` | config | transform | self | exact |
| `SwtorLogParser.Cli/Program.cs` | host/entrypoint | event-driven (Rx) | `Native.Cli/Program.cs` | role+flow match |
| `SwtorLogParser.Native.Cli/Program.cs` | host/entrypoint | event-driven (Rx) | `Cli/Program.cs` | role+flow match |

---

## Migration Source: EVERY current `<PackageReference>` (this is the input for Directory.Packages.props)

### `SwtorLogParser/SwtorLogParser.csproj` (lines 14-19)
```xml
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0-preview.5.23280.8" />   <!-- REMOVE (unused, Phase 3 WR-04) -->
<PackageReference Include="Microsoft.Extensions.Logging.Console" Version="8.0.0-preview.5.23280.8" />        <!-- REMOVE (unused) -->
<PackageReference Include="Microsoft.Extensions.Logging.Debug" Version="8.0.0-preview.5.23280.8" />          <!-- REMOVE (unused) -->
<PackageReference Include="System.Reactive" Version="6.0.1-preview.1" />                                     <!-- KEEP, bump to 6.0.x GA -->
```
NOTE: this csproj has NO `Microsoft.Extensions.Logging.Abstractions` ref today, yet CONTEXT says core lib "only needs Logging.Abstractions". Abstractions currently arrives transitively via the three packages being removed. **Do-not-break:** if any core-lib code uses `Microsoft.Extensions.Logging.Abstractions` types directly, an explicit `<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />` must be ADDED here when the three providers are removed, or the build breaks. Verify with a build after removal.

### `SwtorLogParser.Cli/SwtorLogParser.Cli.csproj` (line 22)
```xml
<PackageReference Include="System.CommandLine.Rendering" Version="0.4.0-alpha.22272.1"/>   <!-- REMOVE entirely; replace with Spectre.Console -->
```
Add (managed CLI only): `<PackageReference Include="Spectre.Console" />` (version pinned centrally; research-confirmed GA + AOT note — base package only, NOT Spectre.Console.Cli).

### `SwtorLogParser.Native.Cli/SwtorLogParser.Native.Cli.csproj` (lines 18-19)
```xml
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.0-preview.5.23280.8"/>   <!-- KEEP, bump to 8.0.x GA -->
<PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1"/>                               <!-- REMOVE entirely (no GA); hand-rolled dispatch -->
```

### `SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` (lines 13-22)
```xml
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.7.0-preview.23280.1"/>   <!-- bump to GA, drop -preview -->
<PackageReference Include="xunit" Version="2.5.0-pre.44"/>                              <!-- bump to GA 2.x -->
<PackageReference Include="xunit.runner.visualstudio" Version="2.5.0-pre.27">           <!-- bump to GA; KEEP IncludeAssets/PrivateAssets child -->
<PackageReference Include="coverlet.collector" Version="6.0.0">                         <!-- already GA; KEEP child metadata -->
```
**Do-not-break:** `xunit.runner.visualstudio` and `coverlet.collector` have `<IncludeAssets>`/`<PrivateAssets>` child elements. Under central management, ONLY the `Version=` attribute moves out; the `<IncludeAssets>`/`<PrivateAssets>` children STAY on the `<PackageReference>` in the csproj.

### `SwtorLogParser.Overlay/SwtorLogParser.Overlay.csproj`
No active `<PackageReference>` (only a commented-out block, lines 16-18). No change needed beyond confirming no Docker/cross-platform props (none present).

---

## DockerDefaultTargetOS / cross-platform properties to remove (INFRA-02)

### `SwtorLogParser.Cli/SwtorLogParser.Cli.csproj` line 9
```xml
<DockerDefaultTargetOS>Linux</DockerDefaultTargetOS>   <!-- REMOVE -->
```
Also present (review, likely keep — not Docker): line 8 `<LangVersion>preview</LangVersion>`, line 14 `<PublishAot>false</PublishAot>`.

### `SwtorLogParser.Native.Cli/SwtorLogParser.Native.Cli.csproj` line 8
```xml
<DockerDefaultTargetOS>Linux</DockerDefaultTargetOS>   <!-- REMOVE -->
```
Keep line 13 `<PublishAot>true</PublishAot>` (load-bearing AOT invariant).

No `DockerDefaultTargetOS` in core lib, Overlay, or Tests csproj.

---

## Pattern Assignments

### `Directory.Packages.props` (NEW — repo root, sibling of `SwtorLogParser.slnx`)

No in-repo analog. Canonical minimal example (fill versions from RESEARCH.md GA confirmations):
```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="System.Reactive" Version="6.0.x" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.x" />
    <PackageVersion Include="Spectre.Console" Version="0.x (GA)" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.x (GA)" />
    <PackageVersion Include="xunit" Version="2.x (GA)" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.x (GA)" />
    <PackageVersion Include="coverlet.collector" Version="6.0.0" />
  </ItemGroup>
</Project>
```
Per-csproj refs become bare: `<PackageReference Include="System.Reactive" />` (no `Version=`).
**Pitfall (from CONTEXT specifics):** a `Version=` attribute left on any `<PackageReference>` while central management is on causes **NU1008**. Strip every `Version=`.

---

### `SwtorLogParser.Native.Cli/Program.cs` — hand-rolled dispatch (controller/entrypoint, event-driven)

**Analog for the new dispatch shape:** the existing `Main` in BOTH files. Current System.CommandLine setup to REPLACE (lines 10-20):
```csharp
public static async Task<int> Main(string[] args)
{
    var rootCommand = new RootCommand("SWTOR Log Parser");
    var listCommand = new Command("list", "list all swtor logs");
    var monitorCommand = new Command("monitor", "monitor log file changes");
    listCommand.SetHandler(ListCombatLogs);
    monitorCommand.SetHandler(MonitorCombatLogs);
    rootCommand.Add(listCommand);
    rootCommand.Add(monitorCommand);
    return await rootCommand.InvokeAsync(args);
}
```

**Cancellation wiring to PRESERVE** — Native currently gets the token from `InvocationContext` (lines 22-34):
```csharp
private static void MonitorCombatLogs(InvocationContext context)
{
    using var manualResetEvent = new ManualResetEvent(false);
    var token = context.GetCancellationToken();                          // <- source of token
    var list = new SlidingExpirationList(TimeSpan.FromSeconds(30));
    manualResetEvent.SetSafeWaitHandle(token.WaitHandle.SafeWaitHandle);
    CombatLogsMonitor.Instance.CombatLogAdded += OnCombatLogAdded;
    CombatLogsMonitor.Instance.DpsHps.Subscribe(playerStats => Update(list, playerStats));
    CombatLogsMonitor.Instance.Start(token);
    manualResetEvent.WaitOne();
}
```
`context.GetCancellationToken()` (cancelled by System.CommandLine on Ctrl+C) **goes away** — replace with an explicit `Console.CancelKeyPress` → `CancellationTokenSource` so the existing `ManualResetEvent` + `SetSafeWaitHandle(token.WaitHandle.SafeWaitHandle)` + `WaitOne()` blocking pattern still works unchanged. Canonical replacement skeleton:
```csharp
public static int Main(string[] args)
{
    return (args.Length > 0 ? args[0] : null) switch
    {
        "list"    => ListCombatLogs(),       // adapt return
        "monitor" => MonitorCombatLogs(),
        _         => PrintUsage(),
    };
}

private static int MonitorCombatLogs()
{
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };  // Ctrl+C -> token
    var token = cts.Token;
    using var manualResetEvent = new ManualResetEvent(false);
    manualResetEvent.SetSafeWaitHandle(token.WaitHandle.SafeWaitHandle);
    // ... unchanged: subscribe DpsHps, Start(token), WaitOne()
}
```
Everything below line 36 in Native/Program.cs (`Update`, `FormatRow`, `OnCombatLogAdded`, redirected-output guards, the SetCursorPosition in-place renderer) is **UNCHANGED** — it has no System.CommandLine dependency.

---

### `SwtorLogParser.Cli/Program.cs` — hand-rolled dispatch + Spectre.Console table (entrypoint, event-driven)

Same `Main`/dispatch replacement as Native (lines 29-43, identical RootCommand/Command/SetHandler/InvokeAsync shape).

**Cancellation wiring to PRESERVE** — managed CLI currently (lines 45-61):
```csharp
private static void MonitorCombatLogs(InvocationContext context)
{
    var token = context.GetCancellationToken();
    var terminal = context.Console.GetTerminal();
    terminal.HideCursor();
    using var manualResetEvent = new ManualResetEvent(false);
    manualResetEvent.SetSafeWaitHandle(token.WaitHandle.SafeWaitHandle);
    var consoleRenderer = new ConsoleRenderer(context.Console, OutputMode.Ansi);
    CombatLogsMonitor.Instance.CombatLogAdded += OnCombatLogAdded;
    CombatLogsMonitor.Instance.DpsHps.Subscribe(playerStats => Update(consoleRenderer, playerStats));
    CombatLogsMonitor.Instance.Start(token);
    manualResetEvent.WaitOne();
}
```
`context.GetCancellationToken()`, `context.Console.GetTerminal()`, `terminal.HideCursor()`, and `ConsoleRenderer`/`OutputMode.Ansi` all belong to System.CommandLine(.Rendering) and **must go**. Replace token with the same `Console.CancelKeyPress` + `CancellationTokenSource` pattern as Native; replace cursor-hide with `Console.CursorVisible = false`.

**THE 5-COLUMN TABLE to port to Spectre.Console** — current `System.CommandLine.Rendering.Views.TableView` block (lines 15-27):
```csharp
private static void Update(ConsoleRenderer renderer, CombatLogsMonitor.PlayerStats playerStats)
{
    List.AddOrUpdate(playerStats);
    var tableView = new TableView<CombatLogsMonitor.PlayerStats> { Items = List.Items };
    tableView.AddColumn(x => x.Player.Name, "Player", ColumnDefinition.Star(0.2));
    tableView.AddColumn(x => x.DPS.HasValue ? x.DPS.Value.ToString("N") : "-", "dps", ColumnDefinition.Star(0.2));
    tableView.AddColumn(x => x.DPSCritP.HasValue ? x.DPSCritP.Value.ToString("N") : "-", "(crit %)", ColumnDefinition.Star(0.2));
    tableView.AddColumn(x => x.HPS.HasValue ? x.HPS.Value.ToString("N") : "-", "hps", ColumnDefinition.Star(0.2));
    tableView.AddColumn(x => x.HPSCritP.HasValue ? x.HPSCritP.Value.ToString("N") : "-", "(crit %)", ColumnDefinition.Star(0.2));
    tableView.Render(renderer, Region);
}
```
The **5 columns (exact headers + cell expressions to reproduce in Spectre.Console)** — DO NOT change order, headers, or the `"N"` / `"-"` formatting:
1. header `"Player"`   → `x.Player.Name`
2. header `"dps"`      → `x.DPS.HasValue ? x.DPS.Value.ToString("N") : "-"`
3. header `"(crit %)"` → `x.DPSCritP.HasValue ? x.DPSCritP.Value.ToString("N") : "-"`
4. header `"hps"`      → `x.HPS.HasValue ? x.HPS.Value.ToString("N") : "-"`
5. header `"(crit %)"` → `x.HPSCritP.HasValue ? x.HPSCritP.Value.ToString("N") : "-"`

Canonical Spectre.Console port (no in-repo analog) — rebuild a `Table` each tick and clear+rewrite (matches current re-render-at-Region(0,0) behavior):
```csharp
private static readonly SlidingExpirationList List = new(TimeSpan.FromSeconds(30));

private static void Update(CombatLogsMonitor.PlayerStats playerStats)
{
    List.AddOrUpdate(playerStats);
    var table = new Table()
        .AddColumn("Player").AddColumn("dps").AddColumn("(crit %)")
        .AddColumn("hps").AddColumn("(crit %)");
    foreach (var x in List.Items)
        table.AddRow(
            x.Player.Name ?? "",
            x.DPS.HasValue ? x.DPS.Value.ToString("N") : "-",
            x.DPSCritP.HasValue ? x.DPSCritP.Value.ToString("N") : "-",
            x.HPS.HasValue ? x.HPS.Value.ToString("N") : "-",
            x.HPSCritP.HasValue ? x.HPSCritP.Value.ToString("N") : "-");
    Console.Clear();                 // or AnsiConsole.Clear() — match the old full re-render
    AnsiConsole.Write(table);
}
```
`OnCombatLogAdded` (lines 63-69) and `ListCombatLogs` (lines 71-74) have no command-framework dependency and stay behavior-identical (`ListCombatLogs` iterates `CombatLogs.EnumerateCombatLogs()`).

**Static field note:** `private static readonly Region Region = new(0, 0);` (line 12) is System.CommandLine.Rendering — DELETE it. `List` (line 13) stays.

---

## Shared Patterns

### Central package version reference
**Source:** new `Directory.Packages.props`. **Apply to:** all 5 csproj — strip `Version=` from every `<PackageReference>`, keep child `<IncludeAssets>`/`<PrivateAssets>`.

### Ctrl+C cancellation (replaces `InvocationContext.GetCancellationToken()`)
**Apply to:** both Program.cs `MonitorCombatLogs`. Pattern: `CancellationTokenSource` + `Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); }`, then keep the existing `ManualResetEvent.SetSafeWaitHandle(token.WaitHandle.SafeWaitHandle)` + `WaitOne()` block and `CombatLogsMonitor.Instance.Start(token)` / `.Stop()` wiring untouched.

### Hand-rolled command dispatch
**Apply to:** both Program.cs `Main`. Pattern: `switch` on `args.Length > 0 ? args[0] : null` → `list` / `monitor` / default usage. Zero dependencies, AOT-safe.

---

## No Analog Found (use canonical examples above)

| File / Pattern | Reason |
|----------------|--------|
| `Directory.Packages.props` | First use of central package management in repo |
| Spectre.Console `Table` render | No Spectre.Console usage anywhere in repo |

---

## DO-NOT-BREAK LIST (invariants for the planner)

1. **Ctrl+C → token → `CombatLogsMonitor.Instance.Stop()`**: removing `System.CommandLine` removes the framework-supplied cancellation token. Both hosts MUST re-wire Ctrl+C via `Console.CancelKeyPress`/`CancellationTokenSource` feeding the SAME `Start(token)` and the `ManualResetEvent.SetSafeWaitHandle` blocking pattern.
2. **Native AOT publishability**: keep `<PublishAot>true</PublishAot>` and `IsAotCompatible=true`. Do NOT add `Spectre.Console` (or `Spectre.Console.Cli`) to `SwtorLogParser.Native.Cli`. `dotnet publish SwtorLogParser.Native.Cli -c Release` must still produce an AOT binary with no trim/AOT warnings.
3. **Behavior parity of `list`**: still enumerates `CombatLogs.EnumerateCombatLogs()` and `Console.WriteLine`s each — both hosts.
4. **Behavior parity of `monitor`**: subscribe to `CombatLogsMonitor.Instance.DpsHps`, feed `SlidingExpirationList(TimeSpan.FromSeconds(30))`, render the live table/rows.
5. **The 5 table columns**: headers `Player`, `dps`, `(crit %)`, `hps`, `(crit %)` in that order, with `"N"`-format values and `"-"` for nulls — identical in the Spectre.Console port (managed CLI). Native CLI's `FormatRow` (`"{0}: DPS: {1} ({2}%); HPS: {3} ({4}%)"`) is UNCHANGED.
6. **Native in-place renderer (PERF-02)**: the `SetCursorPosition` + `PadRight` + `_lastRowCount` clearing + `Console.IsOutputRedirected` guards in Native/Program.cs (lines 38-116) must remain byte-identical — only the command/cancellation plumbing changes.
7. **Logging.Abstractions availability**: after removing the 3 provider packages from the core lib, ensure `Microsoft.Extensions.Logging.Abstractions` is still resolvable (add explicit ref if core lib uses it directly). Build must stay green.
8. **Test child metadata**: keep `<IncludeAssets>`/`<PrivateAssets>` on `xunit.runner.visualstudio` and `coverlet.collector`; only `Version=` moves to props.
9. **Green gates**: `dotnet build SwtorLogParser.slnx` succeeds; `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` stays at 106 passing.
10. **NU1008**: no `Version=` left on any `<PackageReference>` once `ManagePackageVersionsCentrally=true`.

## Metadata
**Analog search scope:** all `*.csproj`, all `Program.cs`, repo root for `Directory.*.props` / `*.slnx`
**Files scanned:** 8
**Pattern extraction date:** 2026-06-12
