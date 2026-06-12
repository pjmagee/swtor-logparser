---
phase: 02-correctness-bugs
reviewed: 2026-06-11T00:00:00Z
depth: deep
files_reviewed: 8
files_reviewed_list:
  - SwtorLogParser/Model/Threat.cs
  - SwtorLogParser/Model/Actor.cs
  - SwtorLogParser/Model/Value.cs
  - SwtorLogParser/Model/GameObject.cs
  - SwtorLogParser/Model/Ability.cs
  - SwtorLogParser/Model/Action.cs
  - SwtorLogParser/Model/CombatLogLine.cs
  - SwtorLogParser/Monitor/CombatLogs.cs
  - SwtorLogParser/Monitor/CombatLogsMonitor.cs
  - SwtorLogParser/Monitor/CombatLog.cs
findings:
  critical: 0
  high: 1
  medium: 3
  low: 4
  total: 8
status: remediated
remediation:
  remediated_at: 2026-06-11
  fixed:
    - HI-01  # Actor.GetMaxHealth bounds-guard (completes BUG-05) + characterization test
    - ME-01  # linked CancellationTokenSource dispose/orphan leak
    - ME-03  # ReadAsync busy-loop when no file selected
    - LO-03  # static Lock -> static readonly object
  deferred:
    - ME-02  # content-vs-reference cache key -> planned Phase 3 RFCT-03 redesign
  open:
    - LO-01  # Threat.Parse unbalanced-scope slice (optional)
    - LO-02  # IsLocalPlayer Name! null-forgiving smell (optional)
    - LO-04  # malformed settings filename dropped silently (none required)
  test_suite: 75 passed / 0 failed / 0 skipped
---

# Phase 2: Code Review Report

**Reviewed:** 2026-06-11
**Depth:** deep (cross-file: cache key chain, cancellation chain, Threat int? ripple)
**Files Reviewed:** 8 production files (+2 cache consumers Ability/Action)
**Status:** findings

## Summary

Phase 2 hardens the parser against malformed input by replacing eager `int/long/ulong.Parse`
with `TryParse`, gates the timestamp through `TryParseExact + InvariantCulture`, migrates the
shared parse caches to `ConcurrentDictionary`, wires cancellation through a linked CTS, and
opens log files read-only. The bulk of the milestone is correct and the `TryParse` conversions
are faithful (no valid-parse result changed, out/nullability handled correctly, `Threat.Value`
`int?` ripple to `IsPositive`/`IsNegative` is safe via `is >=/<` relational patterns which
yield `false` on null).

However, **BUG-05 was applied incompletely**: `Actor.GetMaxHealth` retains an unguarded
`Roms[2]` index access that still throws `IndexOutOfRangeException` for non-empty actors with
fewer than 3 sections — directly contradicting the plan's stated behavior ("Actor.MaxHealth
returns null instead of throwing"). This is the one HIGH finding. The remaining findings are
robustness/quality issues: a CTS dispose/orphan leak, a pre-existing content-vs-reference hash
cache-miss that the BUG-06 migration does not address (and the new concurrency test cannot
detect), and a tight busy-loop in `ReadAsync`.

## High

### HI-01: `Actor.GetMaxHealth` still throws IndexOutOfRangeException — BUG-05 fix incomplete

**File:** `SwtorLogParser/Model/Actor.cs:70-77`
**Issue:** `GetMaxHealth` guards only on `IsEmpty`, then unconditionally indexes `Roms[2]`:
```csharp
private int? GetMaxHealth()
{
    if (IsEmpty) return null;
    var health = Roms[2].Span;   // <-- throws if Roms.Count < 3
    ...
}
```
`IsEmpty` is `Roms.Count == 0 || (Roms.Count == 1 && Roms[0] == "=")`. An actor with
`Roms.Count == 1` (a single non-`=` section) or `Roms.Count == 2` is **not** empty, so
`Roms[2]` throws `IndexOutOfRangeException`. The companion getter `GetHealth` (Actor.cs:62-68)
was correctly hardened in commit 6a9fbfc with `if (Roms.Count != 3) return null;`, but
`GetMaxHealth` was left with only the `int.Parse → TryParse` swap and no count guard. Because
`MaxHealth` is a public lazy property, any caller (e.g. `Actor.ToString()` at Actor.cs:124,
which interpolates `{MaxHealth}`) on such an actor crashes. The plan
(`02-01-PLAN.md:91`, `02-01-SUMMARY.md:9`) explicitly claims `Actor.MaxHealth` returns null
instead of throwing — so this is a stated-but-unmet acceptance criterion and a regression
surface the milestone intended to close.
**Fix:**
```csharp
private int? GetMaxHealth()
{
    if (Roms.Count != 3) return null;
    var health = Roms[2].Span;
    var slash = health.IndexOf('/');
    if (slash == -1) return null;
    var maxStart = slash + 1;
    var maxLength = health.Length - maxStart - 1;
    if (maxLength < 0) return null;
    return int.TryParse(Roms[2].Slice(maxStart, maxLength).Span, out var mh) ? mh : (int?)null;
}
```

## Medium

### ME-01: CancellationTokenSource is never disposed and is orphaned on repeated Start()

**File:** `SwtorLogParser/Monitor/CombatLogsMonitor.cs:116-138`
**Issue:** BUG-01/02 correctly link the token so `Stop()` cancels both `MonitorAsync` and
`ReadAsync`, and the null-guard makes `Stop()`-before-`Start()` safe. But the linked
`CancellationTokenSource` created in `Start()` (line 118) is never `Dispose()`d. `Stop()`
only calls `Cancel()` and nulls the task references; the CTS object leaks. Worse, calling
`Start()` again (e.g. restart after `Stop()`) overwrites `_cancellationTokenSource` with a new
linked source without cancelling/disposing the previous one — the old CTS stays registered as a
callback on the outer `cancellationToken`, leaking a registration for the lifetime of the outer
token. `CancellationTokenSource.CreateLinkedTokenSource` explicitly requires disposal.
**Fix:** Dispose the previous CTS at the top of `Start()` and in `Stop()`:
```csharp
public void Start(CancellationToken cancellationToken)
{
    _cancellationTokenSource?.Cancel();
    _cancellationTokenSource?.Dispose();
    _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    ...
}
public void Stop()
{
    try { _cancellationTokenSource?.Cancel(); }
    catch (Exception e) { _logger?.LogError(e, "Cancel failed"); }
    finally { _cancellationTokenSource?.Dispose(); _cancellationTokenSource = null; _monitor = null; _reader = null; }
}
```

### ME-02: BUG-06 cache migration does not fix (and cannot hit) the real cache key — content vs. reference hash

**File:** `SwtorLogParser/Model/GameObject.cs:101-115`, `Ability.cs:15-25`, `Action.cs:47-59`, `CombatLogs.cs:9-10`
**Issue:** The `ConcurrentDictionary` migration makes the `TryGetValue → construct → TryAdd`
sequence *race-safe* (no torn writes, first-writer-wins, no lost `Dictionary.Add` remains — all
three sites converted, verified). That part is correct. However, the cache **key** is
`rom.GetHashCode()` / `Rom.GetHashCode()`, and `ReadOnlyMemory<char>.GetHashCode()` is derived
from the backing object reference + index + length, **not** the character content. Two distinct
`ReadOnlyMemory<char>` instances with identical text (the common case across log lines, since
each line is sliced from a fresh `string`/array per `CombatLog.GetLogLines` and `ReadAsync`)
produce different hash codes, so the cache effectively never hits across lines. The lookup at
GameObject.cs:103 uses `rom.GetHashCode()` while the store at line 109 uses
`gameObject.GetHashCode()` (== `Rom.GetHashCode()` on the same instance), so within a single
`Parse` call they agree — but cross-call sharing (the stated goal: "shared GameObject/Ability
cache still behaves correctly") does not occur. This is pre-existing, not introduced by Phase 2,
but the BUG-06 commit message claims to "preserve shared GameObject/Ability cache key
(Rom.GetHashCode)" — preserving a key that doesn't dedupe by content means the cache silently
grows unbounded with one entry per parsed slice and provides ~no hit benefit. The new
concurrency smoke test (ca9863f) cannot surface this because it likely reuses one rom instance.
**Fix:** Use a content-based key (e.g. `string.GetHashCode(rom.Span)` or a small custom key
struct over the span) for both lookup and store, or key on the materialized `Rom.ToString()`.
If content-dedup is not actually wanted, drop the cache entirely to avoid an unbounded leak.

### ME-03: `ReadAsync` busy-loops at 100% CPU when no file is selected yet

**File:** `SwtorLogParser/Monitor/CombatLogsMonitor.cs:166-167`
**Issue:** While `_lastFileName` is still null (before `MonitorAsync` has discovered a log file),
the loop hits `if (string.IsNullOrWhiteSpace(current)) continue;` and spins the `while
(!cancellationToken.IsCancellationRequested)` loop with no delay, pegging a CPU core. The
`Task.Delay` at line 202 is only reached after the inner read loop, which is skipped via the
`continue`. This is a correctness-adjacent robustness defect (not pure perf): the tight loop
also makes cancellation latency spiky and burns battery on the WinForms overlay use case.
**Fix:** Add a short awaited delay before `continue`:
```csharp
if (string.IsNullOrWhiteSpace(current))
{
    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
    continue;
}
```

## Low

### LO-01: `Threat.Parse` Slice can throw when `>` precedes `<` (unbalanced scope)

**File:** `SwtorLogParser/Model/Threat.cs:26-35`
**Issue:** `start = LastIndexOf('<')`, `end = LastIndexOf('>')`. If a line contains a `<` after
the last `>` (so `start > end`), and `exists` is true, then
`rom.Slice(start + 1, end - start - 1)` is called with a negative length → throws
`ArgumentOutOfRangeException`. This is outside the BUG-05 numeric scope but is the same class of
unguarded-slice defect the milestone targets; real SWTOR lines make it unlikely, hence Low.
**Fix:** `if (start == -1 || end <= start) return null;` before slicing.

### LO-02: `IsLocalPlayer` dereferences `Name!` with null-forgiving despite `Name` being nullable

**File:** `SwtorLogParser/Model/Actor.cs:36`
**Issue:** `IsLocalPlayer => IsPlayer && CombatLogs.PlayerNames.Contains(Name!)`. `Name` is
`string?` and `GetName()` can return null (its catch returns null). `IsPlayer` being true does
not guarantee `Name != null` (e.g. a player section that throws inside `GetName`). The `Name!`
suppresses the warning but `HashSet<string>.Contains(null)` does not throw — it returns false —
so this is not a crash, only a latent correctness smell. Note for future hardening, not a
blocker.
**Fix:** `IsPlayer && Name is not null && CombatLogs.PlayerNames.Contains(Name)`.

### LO-03: `Lock` field naming and `static` mutability convention

**File:** `SwtorLogParser/Monitor/CombatLogsMonitor.cs:62`
**Issue:** `private static object Lock = new object();` — a static, non-`readonly` lock object
with PascalCase field naming (reads like a property/type). It is never reassigned so should be
`readonly`; a reassignable lock target is a classic concurrency footgun. Out of the diff's
direct scope but adjacent to the reviewed concurrency work.
**Fix:** `private static readonly object _lock = new();` and update the `lock` site.

### LO-04: `BUG-04` guard correct, but silently drops malformed settings filenames without logging

**File:** `SwtorLogParser/Monitor/CombatLogs.cs:22-35`
**Issue:** `SecondSegmentOrNull` correctly length-guards `Split('_')` (`parts.Length > 1`),
so a filename without `_` is skipped instead of throwing `IndexOutOfRange` /
`TypeInitializationException` in the static ctor — the BUG-04 fix is correct and the helper is
pure/testable. Minor: a malformed `*PlayerGUIState.ini` name now vanishes from `PlayerNames`
with no diagnostic, which could mask a missing local-player detection. Acceptable for a static
ctor (cannot easily log), noted for completeness.
**Fix:** None required; optionally surface skipped files via a debug trace if a logger becomes
available outside the static ctor.

## Verified Correct (no finding)

- **BUG-03 timestamp gate:** `TimeFormats = { "HH:mm:ss", "HH:mm:ss.fff" }` with
  `TryParseExact(..., InvariantCulture, DateTimeStyles.None, ...)` covers both real SWTOR
  time-only formats. No valid line is dropped; locale-variant/`FormatException` paths now return
  null cleanly. `DateTimeStyles.None` is appropriate for time-only stamps.
- **BUG-05 numeric conversions (except HI-01):** `Threat.Value` `int?` via `int.TryParse`,
  `Actor.GetHealth/GetId`, `Value.Id`, `GameObject` id parsing — all faithful, no valid-parse
  result changed, out/nullability handled. `Threat.IsPositive => Value is >= 0` /
  `IsNegative => Value is < 0` correctly yield `false` on null (no ripple regression to
  consumers; tests `<0>/<123>/<-123>/<abc>` confirm).
- **BUG-06 race-safety:** `TryGetValue → new → TryAdd → fallback TryGetValue` is race-safe;
  first-writer-wins loses nothing because cached instances are immutable value-wrappers; no
  `Dictionary.Add` remains in any of the 3 Parse sites. (Keying defect tracked separately as
  ME-02.)
- **BUG-01/02 cancellation:** `Start()` links the outer token into a CTS and passes
  `_cancellationTokenSource.Token` (not the outer token) to both `MonitorAsync` and `ReadAsync`,
  so `Stop()`'s `Cancel()` halts both. No outer/linked token confusion. (Dispose leak tracked as
  ME-01.)
- **BUG-07 read-only open:** `CombatLog.GetLogLines` and `ReadAsync` open with
  `FileAccess.Read` + `FileShare.ReadWrite`, not blocking the live game writer. Correct.

---

_Reviewed: 2026-06-11_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: deep_
