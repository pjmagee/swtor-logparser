---
quick_id: 260612-hjb
phase: quick-260612-hjb
plan: "01"
type: execute
mode: quick-full
wave: 1
depends_on: []
autonomous: true
files_modified:
  - SwtorLogParser.Cli.Common/SwtorLogParser.Cli.Common.csproj
  - SwtorLogParser.Cli.Common/SwtorCliApp.cs
  - SwtorLogParser.Cli/Program.cs
  - SwtorLogParser.Cli/SwtorLogParser.Cli.csproj
  - SwtorLogParser.Native.Cli/Program.cs
  - SwtorLogParser.Native.Cli/SwtorLogParser.Native.Cli.csproj
  - SwtorLogParser.slnx

must_haves:
  truths:
    - "Running `SwtorLogParser.Cli monitor` renders the same Spectre live table it did before (Player/dps/(crit %)/hps/(crit %), filename as grey table title)."
    - "Running `SwtorLogParser.Native.Cli monitor` renders the SAME Spectre live table as the managed CLI — no more hand-rolled Console.SetCursorPosition output."
    - "Both hosts' `list` command still prints every combat-log path via CombatLogs.EnumerateCombatLogs."
    - "Piping/redirecting either host's output degrades to plain per-row line writes (no cursor/live ops), preserving WR-05."
    - "A late or second Ctrl+C after the monitor loop returns does NOT throw ObjectDisposedException."
    - "`dotnet build SwtorLogParser.slnx -c Release` succeeds for the whole solution."
    - "`dotnet publish SwtorLogParser.Native.Cli -c Release -r win-x64` (Native AOT) completes; any IL2026/IL2104/IL3050/IL3053 trim/AOT warnings are surfaced (not silently suppressed)."
  artifacts:
    - path: "SwtorLogParser.Cli.Common/SwtorLogParser.Cli.Common.csproj"
      provides: "Shared net10.0 IsAotCompatible library carrying the single Spectre.Console PackageReference + core ProjectReference"
      contains: "IsAotCompatible"
    - path: "SwtorLogParser.Cli.Common/SwtorCliApp.cs"
      provides: "Single static Run(string[]) entry owning arg dispatch, CTS/CancelKeyPress wiring, interactive detection, Spectre Table+Live loop, redirected fallback, list command"
      contains: "public static int Run"
      min_lines: 100
    - path: "SwtorLogParser.Cli/Program.cs"
      provides: "Thin managed-CLI Main forwarding to SwtorCliApp.Run"
      contains: "SwtorCliApp.Run(args)"
    - path: "SwtorLogParser.Native.Cli/Program.cs"
      provides: "Thin Native-AOT Main forwarding to SwtorCliApp.Run"
      contains: "SwtorCliApp.Run(args)"
  key_links:
    - from: "SwtorLogParser.Cli/Program.cs"
      to: "SwtorLogParser.Cli.Common.SwtorCliApp.Run"
      via: "static call from Main"
      pattern: "SwtorCliApp\\.Run\\(args\\)"
    - from: "SwtorLogParser.Native.Cli/Program.cs"
      to: "SwtorLogParser.Cli.Common.SwtorCliApp.Run"
      via: "static call from Main"
      pattern: "SwtorCliApp\\.Run\\(args\\)"
    - from: "SwtorLogParser.Cli.Common/SwtorCliApp.cs"
      to: "CombatLogsMonitor.Instance.DpsHps"
      via: "Subscribe in the monitor loop"
      pattern: "DpsHps\\.Subscribe"
    - from: "SwtorLogParser.slnx"
      to: "SwtorLogParser.Cli.Common/SwtorLogParser.Cli.Common.csproj"
      via: "<Project Path=...> entry"
      pattern: "SwtorLogParser\\.Cli\\.Common"
---

<objective>
Eliminate the duplicated console-rendering and host-wiring between `SwtorLogParser.Cli` and `SwtorLogParser.Native.Cli` by extracting ONE shared Spectre.Console live renderer into a new `SwtorLogParser.Cli.Common` library that both hosts consume. The managed CLI is already the reference UX (Spectre `Table` + `AnsiConsole.Live` with a redirected/non-interactive plain-write fallback); the Native AOT CLI currently hand-rolls `Console.SetCursorPosition` cursor math and must be brought to the same Spectre rendering through the shared component.

Purpose: Closes the CONCERNS.md "duplicated View/host wiring across CLI hosts" item and unifies CLI UX, while proving the shared Spectre path stays Native-AOT-safe (IsAotCompatible surfaces IL warnings at build time, AOT publish is the gate).
Output: New shared library `SwtorLogParser.Cli.Common`; both `Program.cs` reduced to thin one-line forwarders; `.slnx` + both csproj rewired; whole-solution Release build and Native AOT publish pass with any trim/AOT warnings surfaced.
</objective>

<execution_context>
@D:/Projects/pjmagee/swtor-logparser/.claude/gsd-core/workflows/execute-plan.md
@D:/Projects/pjmagee/swtor-logparser/.claude/gsd-core/templates/summary.md
</execution_context>

<context>
@./CLAUDE.md
@.planning/STATE.md
@SwtorLogParser.Cli/Program.cs
@SwtorLogParser.Native.Cli/Program.cs
@SwtorLogParser.Cli/SwtorLogParser.Cli.csproj
@SwtorLogParser.Native.Cli/SwtorLogParser.Native.Cli.csproj
@SwtorLogParser/View/SlidingExpirationList.cs
@SwtorLogParser/View/Entry.cs
@Directory.Packages.props
@SwtorLogParser.slnx
</context>

<tasks>

<task type="auto">
  <name>Task 1: Scaffold the SwtorLogParser.Cli.Common shared library and wire it into the solution</name>
  <files>SwtorLogParser.Cli.Common/SwtorLogParser.Cli.Common.csproj, SwtorLogParser.slnx</files>
  <action>
Create `SwtorLogParser.Cli.Common/SwtorLogParser.Cli.Common.csproj` as an SDK-style library (Microsoft.NET.Sdk, NO OutputType — it is a library, not an Exe). Per the locked design decision: set `TargetFramework` net10.0, `ImplicitUsings` enable, `Nullable` enable, and `<IsAotCompatible>true</IsAotCompatible>` (this is the early-warning system — it makes IL2xxx/IL3xxx trim/AOT analyzer warnings surface at BUILD time, not only at publish time). Add `<PackageReference Include="Spectre.Console" />` with NO Version attribute (version 0.57.0 is centrally managed in Directory.Packages.props — adding a Version attribute would error under ManagePackageVersionsCentrally). Add `<ProjectReference Include="..\SwtorLogParser\SwtorLogParser.csproj" />`. Match the existing csproj formatting in the repo (4-space indent, PropertyGroup/ItemGroup layout like SwtorLogParser.Cli.csproj).

Then register the new project in `SwtorLogParser.slnx`: add a `<Project Path="SwtorLogParser.Cli.Common/SwtorLogParser.Cli.Common.csproj" />` line alongside the existing `<Project Path=...>` entries (keep the existing alphabetical-ish ordering — place it adjacent to the other Cli entries). Do NOT touch the `<Configurations>` block. This is the slnx XML format, not classic .sln — edit it as XML.
  </action>
  <verify><automated>dotnet build SwtorLogParser.Cli.Common/SwtorLogParser.Cli.Common.csproj -c Release 2>&1 | tee /tmp/hjb-scaffold.log; grep -qi "Build succeeded" /tmp/hjb-scaffold.log && grep -q "SwtorLogParser.Cli.Common" SwtorLogParser.slnx && echo SCAFFOLD-OK</automated></verify>
  <done>The new csproj builds standalone in Release (empty library is fine at this point), it carries IsAotCompatible=true and a version-less Spectre.Console reference, and SwtorLogParser.slnx lists the new project.</done>
</task>

<task type="auto">
  <name>Task 2: Author SwtorCliApp — the single shared Spectre live renderer (port the managed CLI verbatim)</name>
  <files>SwtorLogParser.Cli.Common/SwtorCliApp.cs</files>
  <action>
Create `SwtorLogParser.Cli.Common/SwtorCliApp.cs` (one public type per file, file-scoped `namespace SwtorLogParser.Cli.Common;`, Allman braces, 4-space indent, nullable enabled). This type is the single source of truth for BOTH hosts and must port the MANAGED CLI's behavior (SwtorLogParser.Cli/Program.cs) verbatim — the Native CLI's old `Console.SetCursorPosition` cursor math and its "DPS: x (y%); HPS:" string format are DISCARDED.

Public API: `public static int Run(string[] args)`. Internally mirror the managed CLI structure with private helpers `Monitor()` and `List()` (rename the current `MonitorCombatLogs`/`ListCombatLogs`); keep the private static fields (`List` = `new SlidingExpirationList(TimeSpan.FromSeconds(30))`, the `Table` with columns Player / dps / (crit %) / hps / (crit %), `_currentFile`, `_live`, `_interactive`) and the private helpers `Update`, `RebuildTable`, `FormatRow`, `OnCombatLogAdded` exactly as in SwtorLogParser.Cli/Program.cs.

MUST preserve these behaviors verbatim (they are hard constraints):
  - `Run` dispatches on `args[0]`: "list" -> List(); "monitor" -> Monitor(); default -> `Console.Error.WriteLine("Usage: SwtorLogParser.Cli [list|monitor]")` and return 1. Keep the "list"/"monitor"/return-code contract identical.
  - The CancellationTokenSource + `Console.CancelKeyPress` handler INCLUDING the `try { cts.Cancel(); } catch (ObjectDisposedException) { }` guard on the late/second Ctrl+C path, and the `Console.CancelKeyPress -= handler` in `finally`.
  - Interactive detection: `_interactive = !Console.IsOutputRedirected && AnsiConsole.Profile.Capabilities.Interactive;` — drives Spectre live rendering only when interactive (WR-04), else plain line writes (WR-05).
  - The `AnsiConsole.Live(Table).Start(ctx => { _live = ctx; cts.Token.WaitHandle.WaitOne(); })` refresh loop and the non-interactive `cts.Token.WaitHandle.WaitOne()` branch.
  - `RebuildTable` pins the current filename as a grey `TableTitle(Markup.Escape(_currentFile), ...)` (WR-03) and rebuilds rows in place (WR-04).
  - `CursorVisible` toggling guarded by `_interactive && !Console.IsOutputRedirected` in finally.
  - `List()` prints each `CombatLogs.EnumerateCombatLogs()` entry via `Console.WriteLine`.
Required usings: `using Spectre.Console;`, `using SwtorLogParser.Monitor;`, `using SwtorLogParser.View;`. Do NOT introduce reflection, dynamic, or any new dependency — the shared lib must stay AOT-safe. Do NOT place fenced code in this action; the source of truth is the managed CLI already in context.
  </action>
  <verify><automated>dotnet build SwtorLogParser.Cli.Common/SwtorLogParser.Cli.Common.csproj -c Release 2>&1 | tee /tmp/hjb-common.log; grep -qi "Build succeeded" /tmp/hjb-common.log && grep -q "public static int Run" SwtorLogParser.Cli.Common/SwtorCliApp.cs && grep -q "ObjectDisposedException" SwtorLogParser.Cli.Common/SwtorCliApp.cs && grep -q "AnsiConsole.Live" SwtorLogParser.Cli.Common/SwtorCliApp.cs && echo COMMON-OK</automated></verify>
  <done>SwtorCliApp.cs compiles; exposes `public static int Run(string[] args)`; retains the ObjectDisposedException guard, the interactive-vs-redirected branch, the Spectre Table+Live loop, the grey TableTitle filename pin, and the list command — a verbatim port of the managed CLI renderer.</done>
</task>

<task type="auto">
  <name>Task 3: Reduce both Program.cs to thin forwarders and add the shared ProjectReference to both hosts</name>
  <files>SwtorLogParser.Cli/Program.cs, SwtorLogParser.Cli/SwtorLogParser.Cli.csproj, SwtorLogParser.Native.Cli/Program.cs, SwtorLogParser.Native.Cli/SwtorLogParser.Native.Cli.csproj</files>
  <action>
Replace the body of BOTH host `Program.cs` files with a thin forwarder. Each becomes (adjusting the host's own namespace): a file-scoped `namespace SwtorLogParser.Cli;` (and `SwtorLogParser.Native.Cli;` for the native one), `public static class Program`, with `public static int Main(string[] args) => SwtorLogParser.Cli.Common.SwtorCliApp.Run(args);`. Delete ALL the old rendering/monitor/list code from both files. Keep one public type per file, file-scoped namespace.

In `SwtorLogParser.Cli/SwtorLogParser.Cli.csproj`: add `<ProjectReference Include="..\SwtorLogParser.Cli.Common\SwtorLogParser.Cli.Common.csproj" />` to the existing ProjectReference ItemGroup. The direct `<PackageReference Include="Spectre.Console" />` now flows transitively through the shared lib and is redundant — REMOVE it (the design decision permits removal; removing keeps the host honest about its single dependency path). Keep `OutputType=Exe`, `TargetFramework` net10.0, `RootNamespace`, and `PublishAot=false`.

In `SwtorLogParser.Native.Cli/SwtorLogParser.Native.Cli.csproj`: add `<ProjectReference Include="..\SwtorLogParser.Cli.Common\SwtorLogParser.Cli.Common.csproj" />` to the existing ProjectReference ItemGroup. PRESERVE the existing `<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />` (do not remove — it is not proven unused). Keep `OutputType=Exe`, `TargetFramework` net10.0, `RootNamespace`, and `PublishAot=true`.
  </action>
  <verify><automated>grep -q "SwtorCliApp.Run(args)" SwtorLogParser.Cli/Program.cs && grep -q "SwtorCliApp.Run(args)" SwtorLogParser.Native.Cli/Program.cs && ! grep -q "SetCursorPosition" SwtorLogParser.Native.Cli/Program.cs && grep -q "SwtorLogParser.Cli.Common" SwtorLogParser.Cli/SwtorLogParser.Cli.csproj && grep -q "SwtorLogParser.Cli.Common" SwtorLogParser.Native.Cli/SwtorLogParser.Native.Cli.csproj && grep -q "Microsoft.Extensions.Logging.Abstractions" SwtorLogParser.Native.Cli/SwtorLogParser.Native.Cli.csproj && echo REWIRE-OK</automated></verify>
  <done>Both Program.cs are one-line forwarders to SwtorCliApp.Run; the Native CLI no longer references SetCursorPosition; both csproj reference the shared lib; the Native CLI keeps its Logging.Abstractions reference; the managed CLI's redundant Spectre PackageReference is removed.</done>
</task>

<task type="auto">
  <name>Task 4: Gate — whole-solution Release build + Native AOT publish with trim/AOT warning surfacing</name>
  <files>SwtorLogParser.slnx</files>
  <action>
Run the two mandatory gates and record results in SUMMARY.md.

Gate 1 (whole solution): `dotnet build SwtorLogParser.slnx -c Release`. MUST succeed. This proves both hosts, both overlays, Tests, and the new shared lib all compile together and that the new ProjectReferences resolve.

Gate 2 (Native AOT publish — the real AOT-safety proof): `dotnet publish SwtorLogParser.Native.Cli -c Release -r win-x64`. Capture the full publish output to a log. Scan the log for trim/AOT analyzer warnings — specifically IL2026, IL2104, IL3050, IL3053 (these indicate Spectre.Console or its transitive deps doing reflection/dynamic-codegen that AOT cannot guarantee). If the native-link step fails because the MSVC toolchain is not on the shell PATH (MSB3073), note that the IL ANALYSIS portion still ran and report the IL-warning findings; the IL analysis (not the final link) is what proves AOT-safety here, consistent with prior phases.

In SUMMARY.md, explicitly record: (a) whether Gate 1 succeeded, (b) the exact count of each IL2026/IL2104/IL3050/IL3053 warning from Gate 2 (0 is the target), and (c) if any are non-zero, quote the offending Spectre.Console member/message — do NOT suppress them with NoWarn or `<TrimmerRootAssembly>` hacks; surface them so the AOT constraint is visible. If Gate 2 emits AOT-breaking warnings that did not exist before this refactor, flag it as a finding requiring a follow-up decision.
  </action>
  <verify><automated>dotnet build SwtorLogParser.slnx -c Release 2>&1 | tee /tmp/hjb-sln.log; grep -qi "Build succeeded" /tmp/hjb-sln.log && echo SLN-BUILD-OK; dotnet publish SwtorLogParser.Native.Cli -c Release -r win-x64 2>&1 | tee /tmp/hjb-aot.log; echo "IL2026:$(grep -c IL2026 /tmp/hjb-aot.log) IL2104:$(grep -c IL2104 /tmp/hjb-aot.log) IL3050:$(grep -c IL3050 /tmp/hjb-aot.log) IL3053:$(grep -c IL3053 /tmp/hjb-aot.log)"</automated></verify>
  <done>`dotnet build SwtorLogParser.slnx -c Release` succeeds. The Native AOT publish ran its IL analysis; the per-code IL2026/IL2104/IL3050/IL3053 counts are recorded in SUMMARY.md (target 0), and any non-zero Spectre-originated warning is quoted rather than suppressed.</done>
</task>

</tasks>

<verification>
- Whole-solution Release build passes: `dotnet build SwtorLogParser.slnx -c Release`.
- Native AOT publish IL analysis ran: `dotnet publish SwtorLogParser.Native.Cli -c Release -r win-x64`; IL2026/IL2104/IL3050/IL3053 counts recorded (target 0, any non-zero surfaced).
- Both `Program.cs` are thin forwarders to `SwtorLogParser.Cli.Common.SwtorCliApp.Run`; no `SetCursorPosition` remains in the Native CLI.
- WR-03 (filename pinned as grey TableTitle), WR-04 (in-place Live refresh), WR-05 (redirected -> plain writes), and the ObjectDisposedException guard are all present in SwtorCliApp.cs.
- `SwtorLogParser.Cli.Common` is registered in `.slnx` and referenced by both hosts; Native CLI keeps `Microsoft.Extensions.Logging.Abstractions`.
</verification>

<success_criteria>
- One shared `SwtorLogParser.Cli.Common` library (net10.0, IsAotCompatible=true) owns ALL CLI arg dispatch, CTS/Ctrl+C wiring, interactive detection, the Spectre Table+Live loop, the redirected fallback, and the list command.
- Both CLI hosts are reduced to a single `Main` forwarder; zero duplicated rendering/host-wiring code remains.
- The Native AOT CLI renders the identical Spectre table the managed CLI does — the hand-rolled cursor math is gone.
- `dotnet build SwtorLogParser.slnx -c Release` passes; the Native AOT publish IL analysis is recorded with explicit IL-warning counts.
- No regression to the live DPS/HPS stream, the redirected/piped fallback (WR-05), the in-frame filename title (WR-03), the in-place Live refresh (WR-04), or the late-SIGINT ObjectDisposedException guard.
</success_criteria>

<output>
Create `.planning/quick/260612-hjb-unify-both-cli-hosts-behind-a-shared-spe/260612-hjb-SUMMARY.md` when done.
</output>
