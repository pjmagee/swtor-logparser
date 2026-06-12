---
phase: 05-dependency-upgrades
reviewed: 2026-06-12T00:00:00Z
depth: deep
files_reviewed: 7
files_reviewed_list:
  - Directory.Packages.props
  - SwtorLogParser.Cli/Program.cs
  - SwtorLogParser.Cli/SwtorLogParser.Cli.csproj
  - SwtorLogParser.Native.Cli/Program.cs
  - SwtorLogParser.Native.Cli/SwtorLogParser.Native.Cli.csproj
  - SwtorLogParser/SwtorLogParser.csproj
  - SwtorLogParser.Tests/SwtorLogParser.Tests.csproj
findings:
  critical: 0
  warning: 5
  info: 4
  total: 9
status: remediated
remediation:
  remediated_at: 2026-06-12T00:00:00Z
  fixed: [WR-01, WR-03, WR-04, WR-05]
  deferred: [WR-02, IN-01]
  accepted: [IN-02, IN-03, IN-04]
  build: green
  tests: 106 passed, 0 skipped
---

# Phase 5: Code Review Report

**Reviewed:** 2026-06-12
**Depth:** deep
**Files Reviewed:** 7
**Status:** issues_found

## Summary

Phase 5 removes the two preview/alpha/beta `System.CommandLine`* dependencies, introduces central package management (`Directory.Packages.props`, all GA versions), strips `Version=` and dead/Docker properties from the csproj files, and rewrites both CLI hosts to use hand-rolled `switch(args[0])` dispatch with a `Console.CancelKeyPress` → `CancellationTokenSource` Ctrl+C bridge. The managed CLI's `System.CommandLine.Rendering` `TableView` is ported to a Spectre.Console `Table`.

The CPM conversion is clean and correct: `Directory.Packages.props` is well-formed, contains only GA versions, no `Version=` remains on any managed `PackageReference` (the one match in `SwtorLogParser.Overlay` is inside an XML comment and inert), the test refs keep `IncludeAssets`/`PrivateAssets`, the core lib gets an explicit `Logging.Abstractions` ref, and AOT invariants hold (core `IsAotCompatible`, Native CLI `PublishAot=true` and verifiably Spectre-free in both csproj and usings). No package downgrade/conflict and no reflection in the new dispatch.

The Ctrl+C cancellation flow is **functionally correct on the happy path** in both hosts: the handler sets `e.Cancel = true` (so the runtime does not kill the process), cancels the CTS whose token is passed to `Start(token)`, the main thread unblocks on the wait handle, and `Stop()` runs — and the new explicit `Stop()` call is actually an improvement over the old code, which never stopped the monitor. However, several edge-case robustness gaps, one behavior-parity regression in the managed `OnCombatLogAdded`, and a latent handle-ownership quirk carried over from the original Native renderer warrant attention. No Critical issues found.

## Warnings

### WR-01: Ctrl+C handler can throw `ObjectDisposedException` on a disposed CTS (second Ctrl+C / late signal)

**File:** `SwtorLogParser.Cli/Program.cs:29-44` and `SwtorLogParser.Native.Cli/Program.cs:26-44`
**Issue:** The `Console.CancelKeyPress` lambda captures the local `cts` and is **never unsubscribed**. `cts` is owned by `using var cts` and is disposed when `MonitorCombatLogs` returns (right after `Stop()`). The handler remains registered on the process-global `Console.CancelKeyPress` event for the brief window between `WaitOne()` returning and process exit (and indefinitely if `MonitorCombatLogs` were ever called more than once, e.g. future re-entry). A second Ctrl+C — or any SIGINT delivered after `cts` is disposed — invokes the lambda and calls `cts.Cancel()` on a **disposed** `CancellationTokenSource`, throwing `ObjectDisposedException` on the SIGINT handler thread. The window is narrow in normal single-shot use, but it is a real unhandled-exception path on the cancellation route, which is exactly the area flagged as highest-risk.
**Fix:** Capture the subscription and unsubscribe before disposing, and guard the cancel:
```csharp
ConsoleCancelEventHandler handler = (_, e) =>
{
    e.Cancel = true;
    try { cts.Cancel(); } catch (ObjectDisposedException) { /* already shutting down */ }
};
Console.CancelKeyPress += handler;
try
{
    // ... Start / WaitOne / Stop ...
}
finally
{
    Console.CancelKeyPress -= handler;
}
```

### WR-02: Rx subscription `IDisposable` is dropped — subscription outlives `Stop()` and leaks on every `monitor` invocation

**File:** `SwtorLogParser.Cli/Program.cs:39` and `SwtorLogParser.Native.Cli/Program.cs:39`
**Issue:** `CombatLogsMonitor.Instance.DpsHps.Subscribe(...)` returns an `IDisposable` that is discarded. The event handlers `CombatLogAdded += OnCombatLogAdded` (line 38 in both) are likewise never detached. Because `CombatLogsMonitor.Instance` is a **static singleton** that outlives the method, the subscription and the event handler remain attached after `Stop()`. `Stop()` only cancels the worker tasks; it does not complete the `CombatLogLines` Subject or drop subscribers. The subscription's `Update` closure still holds the `SlidingExpirationList` (and its `Timer`) alive, and `OnCombatLogAdded` still touches the console. In single-shot CLI use the process exits immediately so impact is bounded, but this is a genuine resource/handler leak that would compound if `monitor` were ever dispatched twice in one process, and it means `Stop()` does not fully tear down the pipeline it started.
**Fix:** Capture and dispose the subscription, and detach the handler, in a `finally` after `WaitOne()`:
```csharp
CombatLogsMonitor.Instance.CombatLogAdded += OnCombatLogAdded;
using var sub = CombatLogsMonitor.Instance.DpsHps.Subscribe(Update);
CombatLogsMonitor.Instance.Start(cts.Token);
cts.Token.WaitHandle.WaitOne();
CombatLogsMonitor.Instance.CombatLogAdded -= OnCombatLogAdded;
CombatLogsMonitor.Instance.Stop();
```

### WR-03: Managed `OnCombatLogAdded` behavior regression — scrolling output instead of in-place header overwrite

**File:** `SwtorLogParser.Cli/Program.cs:70-73`
**Issue:** The old managed host overwrote row 0 in place (`Console.SetCursorPosition(0,0)` → blank the line → write the filename), keeping the filename pinned as a header above the live table. The new version calls `AnsiConsole.MarkupLineInterpolated($"[grey]{combatLog.FileInfo}[/]")`, which **writes a new line and advances the cursor (scrolls)**. Combined with the per-tick `AnsiConsole.Clear()` in `Update` (line 66), the on-screen result is materially different from the old fixed-region layout: the filename line and the table fight over the cleared screen and the header no longer stays pinned at row 0. The summary claims this is "behavior-equivalent," but it is not a faithful port of the in-place header behavior. (Not a crash; classified Warning as an output-parity regression in the exact area called out for parity review.)
**Fix:** If header parity matters, render the filename as part of the same cleared frame (e.g. write it immediately before `AnsiConsole.Write(table)` in `Update`, or store the latest filename in a field and emit it as the first line each frame) rather than emitting an independent scrolling line out-of-band on the Rx thread.

### WR-04: Per-frame full-screen `AnsiConsole.Clear()` reintroduces flicker and is not a faithful port of the old fixed-Region render

**File:** `SwtorLogParser.Cli/Program.cs:66-67`
**Issue:** The old `TableView.Render(renderer, Region)` redrew into a fixed `Region(0,0)` without clearing the whole console. The port calls `AnsiConsole.Clear()` (full screen clear) followed by `AnsiConsole.Write(table)` on **every** `DpsHps` tick. This is a visible behavioral change: full-screen clear-then-redraw flickers and erases the scrolled filename line(s) from `OnCombatLogAdded` (see WR-03), whereas the old region render did not clear the surrounding screen. The `Update` callback fires on the Rx subscription thread at the pipeline's emission rate, so the clear/redraw cadence is also driven by stat emission rather than a throttled frame loop. Functionally it renders, but it is a parity regression versus the locked old behavior the summary claims to match.
**Fix:** Prefer `AnsiConsole.Live(table)` (or render into a fixed region without a global clear) to update the table in place without clearing the whole console each tick; if full-clear must stay, document it as an intentional deviation rather than "parity-first."

### WR-05: `Console.CursorVisible = false` is not portable and can throw on non-Windows / redirected output

**File:** `SwtorLogParser.Cli/Program.cs:36`
**Issue:** `Console.CursorVisible` is documented to be supported only on Windows for the *getter*; the setter is broadly supported but throws/no-ops in some hosts, and more importantly it is invoked unconditionally with no `Console.IsOutputRedirected` guard. The Native host carefully guards every cursor/console operation behind `Console.IsOutputRedirected` (Program.cs:56, 106) precisely because cursor operations misbehave when output has no console buffer. The managed host sets `CursorVisible` and later calls `AnsiConsole.Clear()` with no such guard, so running the managed `monitor` with redirected/piped output (or in an environment without a console buffer) can throw an unhandled `IOException`/`PlatformNotSupportedException` before the monitor even starts. The old managed code went through `terminal.HideCursor()` which degraded more gracefully.
**Fix:** Guard cursor/clear operations: `if (!Console.IsOutputRedirected) Console.CursorVisible = false;` and consider the same guard around the `AnsiConsole.Clear()` render path for parity with the Native host's redirected-output handling.

## Info

### IN-01: Latent wait-handle ownership / double-dispose quirk carried into the Native host

**File:** `SwtorLogParser.Native.Cli/Program.cs:34-44`
**Issue:** `manualResetEvent.SetSafeWaitHandle(token.WaitHandle.SafeWaitHandle)` reassigns the MRE's internal `SafeWaitHandle` to the `CancellationToken`'s own wait handle. `SetSafeWaitHandle` disposes the MRE's original handle, and when `using var manualResetEvent` disposes at method end it disposes whatever `SafeWaitHandle` it currently owns — which is now the token's handle, not the MRE's. The CTS also owns/disposes that handle (`using var cts`). This is an ownership ambiguity (the same `SafeWaitHandle` reachable from two disposers). In practice .NET `SafeHandle` ref-counting tends to absorb this, and the construct is byte-identical to the pre-Phase-5 code (so not introduced here), but it is fragile. The managed host's simpler `cts.Token.WaitHandle.WaitOne()` (Cli/Program.cs:42) avoids the whole dance and is the better pattern.
**Fix:** Replace the `ManualResetEvent`/`SetSafeWaitHandle` plumbing in the Native host with the managed host's `token.WaitHandle.WaitOne();` to eliminate the dual-ownership handle and the extra disposable.

### IN-02: Unknown-argument and extra-argument handling diverges subtly from the old `RootCommand`

**File:** `SwtorLogParser.Cli/Program.cs:13-24` and `SwtorLogParser.Native.Cli/Program.cs:10-21`
**Issue:** The hand-rolled dispatch only inspects `args[0]`. The old `RootCommand` would have rejected unknown options/extra tokens (e.g. `monitor --bogus`) and produced System.CommandLine's standardized error + exit code, whereas the new switch ignores everything after `args[0]` (`monitor extra junk` silently runs `monitor`). Exit-code parity for the core cases is preserved (`list`/`monitor` → 0, no-args/unknown → 1), but trailing-argument validation and `--help`/`--version` behavior are dropped. This is acceptable for a deliberately minimal dispatcher but is a behavior delta worth recording against the "behaves identically" goal.
**Fix:** If strict parity matters, reject extra args (`args.Length != 1`) for `list`/`monitor`; otherwise document the intentional simplification.

### IN-03: No `Console.CursorVisible` restoration on exit (managed host)

**File:** `SwtorLogParser.Cli/Program.cs:36-44`
**Issue:** `Console.CursorVisible = false` is set but never restored to `true` before the method returns / process exits. After a clean Ctrl+C the terminal may be left with a hidden cursor in the user's shell (terminal-dependent). The old `terminal.HideCursor()` path had the same limitation, so this is not a regression, but with the cursor-hide now done via raw `Console` API it is easy to pair with a restore.
**Fix:** In a `finally`, restore: `if (!Console.IsOutputRedirected) Console.CursorVisible = true;`.

### IN-04: `Directory.Packages.props` omits `<ManagePackageVersionsCentrally>` scoping guard for non-source projects

**File:** `Directory.Packages.props:1-17`
**Issue:** The file is well-formed and correct, and enabling CPM repo-wide is intentional. Note for completeness: the root-level props applies CPM to **all** projects including `SwtorLogParser.Overlay`. Overlay's only `Version=`'d `PackageReference` is currently commented out (so no NU1008 today), but if that reference is ever uncommented without adding a corresponding `<PackageVersion Include="Microsoft.Windows.SDK.Contracts" .../>` entry, the Overlay build will fail under CPM. No action required now; flagged so the CPM boundary is understood by future maintainers.
**Fix:** None required. Optionally add `Microsoft.Windows.SDK.Contracts` as a `PackageVersion` entry (GA version) now so re-enabling the Overlay reference is friction-free, or document that Overlay packages must be added centrally.

---

## Remediation (2026-06-12)

Fixes applied by `gsd-code-fixer` against this review. `dotnet build SwtorLogParser.slnx` succeeds (1 pre-existing CS0108 warning in `SwtorLogParser.Overlay/ParserForm.cs`, unrelated); `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` → **106 passed, 0 skipped**. Core lib stays `IsAotCompatible`; Native CLI stays Spectre-free + `PublishAot`.

| Finding | Status | Resolution |
| --- | --- | --- |
| WR-01 | Fixed | Captured the `CancelKeyPress` handler, unsubscribe it in a `finally`, and wrapped `cts.Cancel()` in `try/catch (ObjectDisposedException)` in **both** hosts. `e.Cancel = true` preserved so the first Ctrl+C still drives a clean `Stop()`. (`SwtorLogParser.Cli/Program.cs`, `SwtorLogParser.Native.Cli/Program.cs`) |
| WR-03 | Fixed | Managed filename header is now pinned as the live table's `Title` and rebuilt in the same frame, instead of an out-of-band scrolling `MarkupLineInterpolated`. (`SwtorLogParser.Cli/Program.cs`) |
| WR-04 | Fixed | Replaced per-tick `AnsiConsole.Clear()` + full redraw with `AnsiConsole.Live(table)`: rows are rebuilt in place and `ctx.Refresh()` is called each tick — no full-screen clear, no flicker. Exact 5 columns, `"N"` format, `"-"` for null, and `SlidingExpirationList` behavior preserved. (`SwtorLogParser.Cli/Program.cs`) |
| WR-05 | Fixed | Guarded cursor/live operations behind `!Console.IsOutputRedirected && AnsiConsole.Profile.Capabilities.Interactive`; redirected/non-interactive output degrades to plain line writes (mirrors the Native host) so `monitor > out.txt` no longer throws. `CursorVisible` is also restored on exit (addresses IN-03 incidentally). (`SwtorLogParser.Cli/Program.cs`) |
| WR-02 | Deferred | Disposing the Rx subscription / detaching handlers after `Stop()` was deliberately skipped to avoid complicating the `AnsiConsole.Live` refactor; impact bounded by single-shot CLI process exit (as the review notes). |
| IN-01 | Accepted | Native `SetSafeWaitHandle` handle-ownership quirk left as-is — latent, byte-identical to pre-Phase-5 code, absorbed by `SafeHandle` ref-counting. |
| IN-02 / IN-04 | Accepted | Intentional dispatcher simplification / CPM boundary note; no action required. |

**Fix commits (branch fast-forwarded onto `main`):**

- `fix(05-review): guard CTS against late Ctrl+C (WR-01)`
- `fix(05-review): live-update managed CLI table without flicker (WR-03/04/05)`

---

_Reviewed: 2026-06-12_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: deep_
_Remediated: 2026-06-12 by Claude (gsd-code-fixer)_
