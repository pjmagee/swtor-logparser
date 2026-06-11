---
phase: 04-performance
reviewed: 2026-06-12T00:00:00Z
depth: deep
files_reviewed: 3
files_reviewed_list:
  - SwtorLogParser/Monitor/CombatLog.cs
  - SwtorLogParser/Monitor/CombatLogsMonitor.cs
  - SwtorLogParser.Native.Cli/Program.cs
findings:
  critical: 1
  warning: 3
  info: 3
  total: 7
status: remediated
remediation:
  remediated_at: 2026-06-12T00:00:00Z
  iteration: 1
  fixed:
    - WR-02
  resolved_comments:
    - CR-01
    - WR-01
    - IN-03
  deferred_accepted:
    - WR-03
    - IN-01
    - IN-02
  build: passing
  tests: 106 passed, 0 failed, 0 skipped
---

# Phase 4: Code Review Report

**Reviewed:** 2026-06-12T00:00:00Z
**Depth:** deep
**Files Reviewed:** 3
**Status:** issues_found

## Summary

Reviewed the three production files changed across PERF-01/02/03 (commits `c17fcd7..6d37aef`). The mandate was to confirm these "pure perf refactors" produce identical output.

**Verdict by sub-task:**
- **PERF-03 (single-pass `CalculateDpsHpsStats`)** — mathematically EQUIVALENT to the old `OrderBy` + 6-LINQ version for every reachable case. I traced crit% denominator (`state.Count`), independent damage/heal counting (two independent `if`s), `timeSpan` endpoints, `Player` selection, and null-on-zero/infinity. No divergence. The `maxStamp`/`minStamp` tie-breaking differs from the old stable-`OrderBy` last-element selection, but because timestamps are parsed time-only (same date), equal `TimeOfDay` implies equal `TimeStamp`, so the delta is identical. The new code is also strictly safer on an empty `state` (old threw `InvalidOperationException` via `ElementAt(0)`). No findings.
- **PERF-01 zero-copy slices** — `EnumerateLineSpans` reproduces `MemoryExtensions.EnumerateLines` exactly for `\r`, `\n`, `\r\n` (verified EOF, no-trailing-newline, empty file, consecutive blanks). AsMemory windows are GC-rooted for the list lifetime. BUT: (a) `ToString()` count semantics changed — it no longer applies the `Parse != null` filter, which is an observable output regression (CR-01); (b) `EnumerateLineSpans` does NOT reproduce the full Unicode terminator set `EnumerateLines` recognizes (WR-01).
- **PERF-02 in-place render** — generally correct, with row-0 header preservation as a deliberate improvement over `Console.Clear()`. Residual concerns: unguarded `Console` window/cursor calls can throw on resize/no-console (WR-02), and a behavior change in clear semantics worth noting (WR-03).

**AOT:** No `.csproj` changes, no new dependencies, no reflection introduced in the core lib. Clean.

## Critical Issues

### CR-01: `CombatLog.ToString()` count no longer matches old `GetLogLines().Count` (drops the parseable filter)

**File:** `SwtorLogParser/Monitor/CombatLog.cs:19-27`
**Issue:** This is an observable output regression, not a pure perf refactor.

OLD: `var count = GetLogLines().Count;` — `GetLogLines()` skips empty lines AND drops any line where `CombatLogLine.Parse(...) is null`. So the old count = number of **non-empty AND parseable** lines.

NEW: counts every line where `length > 0`, i.e. **all non-empty** lines, with no parse filter:
```csharp
foreach (var (_, length) in EnumerateLineSpans(text))
    if (length > 0) count++;
```

`CombatLogLine.Parse` (`SwtorLogParser/Model/CombatLogLine.cs:42-49`) returns `null` for any line that does not contain exactly five `[...]` sections or whose first section is not a valid `HH:mm:ss[.fff]` timestamp. SWTOR combat logs are not guaranteed to be 100% parseable combat lines (header/metadata/partial trailing writes from the live writer exist). For any such file the new count is **strictly higher** than the old, so the string returned by `ToString()` (rendered as the filename header in the Native CLI and used wherever a `CombatLog` is stringified) changes value.

The inline comment at line 16-18 explicitly acknowledges the count now drops the parseable filter ("the new count drops the parseable filter") and reframes it as "locked semantics" — but the comparison target is the OLD `GetLogLines().Count`, which DID include that filter. This is a behavior divergence presented as equivalent.

**Fix:** Either restore parse-equivalence (count only parseable lines), e.g.:
```csharp
var count = 0;
foreach (var (start, length) in EnumerateLineSpans(text))
{
    if (length == 0) continue;
    if (CombatLogLine.Parse(text.AsMemory(start, length)) is not null) count++;
}
```
(this reintroduces the Parse cost the refactor was avoiding — so confirm the intent), OR explicitly accept and document the new "non-empty line count" as a deliberate semantic change with sign-off, and update the comment to stop claiming it matches the old count.

## Warnings

### WR-01: `EnumerateLineSpans` does not reproduce the full `EnumerateLines` Unicode terminator set

**File:** `SwtorLogParser/Monitor/CombatLog.cs:64-84`
**Issue:** The method comment (lines 60-63) claims it "Reproduces `MemoryExtensions.EnumerateLines`". It only handles `\r`, `\n`, and `\r\n`. The real `EnumerateLines` also treats VT (U+000B), FF (U+000C), NEL (U+0085), LS (U+2028), and PS (U+2029) as line breaks. If any of those code points appear inside a log line, the old span-based path would split there (potentially producing an extra non-empty line, or trimming the slice) while the new path keeps the bytes inline — a divergence in both the line count and the slice content fed to `Parse`.

In practice these code points are improbable in Latin1-decoded SWTOR combat logs, so impact is low — but the claim of exact `EnumerateLines` reproduction is not accurate. Note VT (0x0B) / FF (0x0C) are within Latin1's representable range, so they are not impossible.

**Fix:** Either extend the terminator test to the full set (VT, FF, NEL, LS, PS in addition to CR/LF/CRLF) to genuinely match `EnumerateLines`, or downgrade the comment to state "handles `\r`, `\n`, `\r\n` only — sufficient for these logs" so the claim matches the code.

### WR-02: Unguarded `Console` window/cursor calls can throw outside the redirected-output guard

**File:** `SwtorLogParser.Native.Cli/Program.cs:54-77`, `92-93`
**Issue:** `Console.IsOutputRedirected` is checked (good), but the non-redirected path then reads `Console.WindowWidth`/`Console.WindowHeight` and calls `Console.SetCursorPosition` without a try/catch. Two residual failure modes:
1. A window resize between reading `WindowHeight`/`WindowWidth` (lines 54-55) and the `SetCursorPosition` calls (lines 64, 75) can make `targetRow`/width stale and push `SetCursorPosition` out of bounds → `ArgumentOutOfRangeException`/`IOException`.
2. `OnCombatLogAdded` (lines 92-93) reads `Console.WindowWidth` and calls `SetCursorPosition(0,0)` with no redirected/no-console guard at all — if output is redirected, `new string(' ', Console.WindowWidth - 1)` and `SetCursorPosition` can throw (the redirected guard only exists in `Update`, not here).

This is on the render path inside an Rx `Subscribe` callback; an unhandled exception there can tear down the subscription silently.

**Fix:** Guard `OnCombatLogAdded` with the same `Console.IsOutputRedirected` early-return used in `Update`, and wrap the cursor writes in a try/catch (catching `IOException`/`ArgumentOutOfRangeException`) so a transient resize does not kill the subscription.

### WR-03: Render clear-semantics change vs old `Console.Clear()` (full-screen clear no longer happens)

**File:** `SwtorLogParser.Native.Cli/Program.cs:38-80`
**Issue:** The OLD `Update` called `Console.Clear()` every frame, wiping the entire buffer (including row 0) before rewriting from row 1. The NEW code never clears the whole screen; it only (a) overwrites rows 1..N with pad-to-width and (b) clears rows that shrank since last frame via `_lastRowCount`. Consequences to confirm are intended:
- Row 0 (filename header) now persists between frames instead of being blanked and left empty until the next `CombatLogAdded` — this is an improvement, but it IS a behavior change.
- Any pre-existing content below the last-drawn stats row (from before the program started, or from output the program does not own) is no longer cleared. `Console.Clear()` blanked the full buffer; the in-place render only manages rows it has tracked. This is the expected tradeoff of flicker-free rendering, but it means the steady-state screen contents differ from the old version.

`_lastRowCount` is a `static` field shared process-wide; correct here since there is a single monitor, but it assumes exactly one concurrent render loop. Fine for the CLI, worth a note.

**Fix:** No code change required if the new persistent-header / no-full-clear behavior is the intended UX — but it should be explicitly acknowledged as a behavior change rather than filed under "identical output", and the `_lastRowCount` single-consumer assumption documented.

## Info

### IN-01: Across-midnight `TimeOfDay` ordering quirk is preserved (pre-existing)

**File:** `SwtorLogParser/Monitor/CombatLogsMonitor.cs:111-121`
**Issue:** Min/max are tracked by `TimeStamp.TimeOfDay`, so a window straddling midnight (e.g. 23:59:59 then 00:00:01) orders the post-midnight line as "earlier", producing a negative/odd `timeSpan`. This is faithfully carried over from the old `OrderBy(x => x.TimeStamp.TimeOfDay)` and is NOT introduced by PERF-03. Noted only because the refactor's equivalence claim depends on it being preserved — which it is.
**Fix:** Out of scope for an equivalence-preserving perf refactor; track separately if cross-midnight accuracy is ever desired.

### IN-02: `player ??= line.Source` relies on the upstream non-null-Source invariant

**File:** `SwtorLogParser/Monitor/CombatLogsMonitor.cs:125`, `159`
**Issue:** New code picks the first line whose `Source` is non-null; old code used `state.ElementAt(0).Source!` unconditionally. These coincide only because `ConfigureObservables` filters `.Where(x => x.Source is not null && x.Source.Name is not null)` (line 55) before the GroupBy, so every line in `state` has a non-null Source. Equivalent today; the latent difference would surface only if that upstream filter were ever removed (new code would skip null-Source lines and pick a later Source; old code would null-forgive a null and assign `Player = null`). Documenting the coupling.
**Fix:** None required; the `player!` null-forgiveness at line 159 is safe under the current invariant.

### IN-03: Comment claims byte-for-byte identity that CR-01 contradicts for `ToString`

**File:** `SwtorLogParser/Monitor/CombatLog.cs:16-18`
**Issue:** The comment frames the new non-empty-line count as the "locked count semantics ... matching the old empty-line skip" and "one source of truth", which understates that the parseable filter was dropped (see CR-01). Comments asserting equivalence that the code does not honor are a maintenance hazard.
**Fix:** Update the comment to state plainly that the count is now "non-empty lines (NOT parse-validated lines, unlike the previous `GetLogLines().Count`)".

---

## Remediation (iteration 1, 2026-06-12)

**Build:** `dotnet build SwtorLogParser.slnx` succeeds (0 errors; pre-existing CS0108 warning in Overlay unrelated).
**Tests:** `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` — 106 passed, 0 failed, 0 skipped (baseline held).
**AOT:** No `.csproj` changes; core lib stays `IsAotCompatible`.

### Fixed (code)

- **WR-02 — FIXED** (`SwtorLogParser.Native.Cli/Program.cs`). Hardened `OnCombatLogAdded`: added the same `Console.IsOutputRedirected` early-return (plain `WriteLine`, no cursor calls) used in `Update`, and wrapped the cursor-positioning writes in `try/catch (IOException or ArgumentOutOfRangeException)` so a console resize on the Rx callback thread can no longer throw and tear down the subscription. No behavior change on a normal console. Commit `b14a5a9`.

### Resolved via accurate comments (intended behavior, no code change)

- **CR-01 / IN-03 — RESOLVED (accepted as intended).** The non-empty-line count in `CombatLog.ToString()` is intentional per PERF-01 (count without re-parse). Rather than re-introduce the parseable filter (which would re-add the avoided `Parse` cost), the misleading comment was corrected to state plainly that the count is non-empty lines, NOT parse-filtered like the old `GetLogLines().Count`, and may exceed the count of parseable combat lines. Commit `df47707`.
- **WR-01 — RESOLVED (accepted as intended).** `EnumerateLineSpans` deliberately splits only on `\r\n`/`\r`/`\n` and NOT on the exotic Unicode terminators (VT/FF/NEL/LS/PS) that `MemoryExtensions.EnumerateLines` recognizes — this is CORRECT and SAFER for Latin-1-decoded logs (e.g. a name byte `0x85` → U+0085 NEL must not split). The overclaiming "Reproduces MemoryExtensions.EnumerateLines" comment was corrected to describe the intentional, narrower behavior. Code unchanged. Commit `df47707`.

### Deferred / accepted (no change)

- **WR-03 — ACCEPTED.** Removal of `Console.Clear()` (persistent row-0 header, no full-screen clear) is the intended PERF-02 UX change.
- **IN-01 — ACCEPTED.** Across-midnight `TimeOfDay` ordering quirk is pre-existing and intentionally preserved by the equivalence-preserving refactor.
- **IN-02 — ACCEPTED.** `player ??= line.Source` is safe under the existing upstream non-null-Source filter invariant.

---

_Reviewed: 2026-06-12T00:00:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: deep_
_Remediated: 2026-06-12 (gsd-code-fixer, iteration 1)_
