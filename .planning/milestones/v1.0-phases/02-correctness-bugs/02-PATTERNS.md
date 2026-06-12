# Phase 2: Correctness Bugs - Pattern Map

**Mapped:** 2026-06-11
**Files analyzed:** 10 production files (7 bug fixes)
**Analogs found:** 5 in-repo analogs / 6 distinct patterns (1 pattern — `ConcurrentDictionary` — has NO in-repo analog; canonical example supplied)

This phase MODIFIES production code. Each fix below pairs the bug with the closest EXISTING
pattern already in this codebase to copy, plus the exact current parse-site code so the
planner's edits are surgical.

---

## File Classification

| File | Bug | Change Class | Closest In-Repo Analog | Match Quality |
|------|-----|--------------|------------------------|---------------|
| `Monitor/CombatLogsMonitor.cs` | BUG-01 | wiring (token) | n/a (logic fix, no analog needed) | n/a |
| `Monitor/CombatLogsMonitor.cs` | BUG-02 | null-guard | `CombatLogsMonitor.cs:168` `is null` | partial |
| `Model/CombatLogLine.cs` | BUG-03 | culture + guard | `Actor.cs:147,154` TryParse+InvariantCulture | role-match |
| `Monitor/CombatLogs.cs` | BUG-04 | guard (static ctor) | `Threat.cs:24` length-guard `return null/skip` | role-match |
| `Model/Threat.cs` | BUG-05 | TryParse guard | `Actor.cs:147` `float.TryParse` | role-match |
| `Model/Actor.cs` | BUG-05 | TryParse guard | `Actor.cs:147` `float.TryParse` (same file) | exact |
| `Model/Value.cs` | BUG-05 | TryParse guard | `Actor.cs:147` `float.TryParse` | role-match |
| `Model/GameObject.cs` | BUG-05 | TryParse guard | `Actor.cs:147` `float.TryParse` | role-match |
| `Model/Action.cs` | BUG-06 | concurrency | NONE — `ConcurrentDictionary` is new | no-analog |
| `Model/GameObject.cs` | BUG-06 | concurrency | NONE — `ConcurrentDictionary` is new | no-analog |
| `Model/Ability.cs` | BUG-06 | concurrency | NONE — `ConcurrentDictionary` is new | no-analog |
| `Monitor/CombatLogs.cs` | BUG-06 | concurrency (cache decl) | NONE — `ConcurrentDictionary` is new | no-analog |
| `Monitor/CombatLog.cs` | BUG-07 | file access | `CombatLogsMonitor.cs:170-175` FileAccess.Read | partial |

---

## Shared Patterns

### Pattern A — Numeric guard via TryParse (canonical in-repo analog)
**Source:** `SwtorLogParser/Model/Actor.cs:147-155`
**Apply to:** all BUG-05 sites and BUG-03.
This is the ONLY established TryParse pattern in the repo — copy its exact shape (Span overload,
`out var`, branch that skips/returns null on failure). Allocation-free, no try/catch.
```csharp
if (float.TryParse(content.Slice(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture,
        out var number)) position.Add(number);
```
For the integer/long/ulong sites no culture argument is needed (integers are culture-invariant
for these inputs), e.g. `int.TryParse(span, out var v)`. Use `NumberStyles`/`CultureInfo` only
where the existing direct-parse call did, which it does NOT for the integer sites.

### Pattern B — Null-return-on-invalid (pervasive)
**Source:** `Threat.cs:23-24`, `Value.cs:64`, `CombatLogLine.cs:40`, `Actor.cs:115`
**Apply to:** every BUG-05 getter and the BUG-03/BUG-04 guards.
The repo's universal error policy is "return null / skip, never throw":
```csharp
if (rom.IsEmpty) return null;
if (rom.Length < 3) return null;
```
A failed `TryParse` should follow the same shape: `return null;` (nullable getter) — the reader
loop at `CombatLogsMonitor.cs:185-191` already filters `item is not null`, so a null line is skipped.

### Pattern C — `is null` guard (in-repo)
**Source:** `CombatLogsMonitor.cs:168` `if (fileStream is null)`
**Apply to:** BUG-02 — null-guard `_cancellationTokenSource` in `Stop()`.

### Pattern D — Thread-safe cache (NO in-repo analog — canonical example)
**Apply to:** BUG-06 sites. There is currently NO `ConcurrentDictionary` anywhere in the repo;
all caches are plain `Dictionary<int,T>` with `TryGetValue` + `.Add` (the race). `ConcurrentDictionary`
is AOT-safe (per CONVENTIONS.md:55) so it is allowed in the `IsAotCompatible` core lib.
Canonical minimal replacement — declaration + `GetOrAdd`:
```csharp
using System.Collections.Concurrent;

internal static readonly ConcurrentDictionary<int, Action> ActionCache = new();

// at the cache site, replace TryGetValue/new/Add with a single atomic GetOrAdd:
var action = CombatLogs.ActionCache.GetOrAdd(rom.GetHashCode(), _ => new Action(rom));
```
Note: keep the existing `rom.GetHashCode()` KEY (Phase 2 fixes the race only; key redesign is Phase 3).
Caveat for the planner: the current code keys reads on `rom.GetHashCode()` but ADDS using
`obj.GetHashCode()` (which for these types also returns `Rom.GetHashCode()` — see `Action.cs:66`,
`GameObject.cs:53`), so the keys are equivalent and `GetOrAdd(rom.GetHashCode(), …)` preserves behavior.

---

## Pattern Assignments (per bug, with exact current code)

### BUG-01 — cancellation wiring (`CombatLogsMonitor.cs:118-123`)
Current:
```csharp
_cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
_monitor = Task.Factory.StartNew(() => MonitorAsync(cancellationToken), cancellationToken);
_reader  = Task.Factory.StartNew(() => ReadAsync(cancellationToken), cancellationToken);
```
Fix: pass `_cancellationTokenSource.Token` into `MonitorAsync(...)` and `ReadAsync(...)` (the inner
worker arg), so `Stop()`'s `Cancel()` reaches the loops at `:148` and `:216`. (The outer
`StartNew` token arg may stay as-is or also use the linked token.) No analog — pure logic fix.

### BUG-02 — `Stop()` before `Start()` NRE (`CombatLogsMonitor.cs:125-137`)
`_cancellationTokenSource` is non-nullable but unassigned until `Start()`. Current:
```csharp
_cancellationTokenSource.Cancel();
```
Fix (apply Pattern C): null-guard, e.g. `_cancellationTokenSource?.Cancel();` (and make the field
`CancellationTokenSource?`). Also note `_logger` in the catch (`:135`) is only assigned in the
logger ctor — fine here. Making `Stop()` a safe no-op satisfies the decision at CONTEXT line 32.

### BUG-03 — timestamp parse (`CombatLogLine.cs:9`)
Current (inside the private ctor, throws on bad input):
```csharp
TimeStamp = DateTime.Parse(Roms[0].Span);
```
Fix (apply Pattern A + B): use `DateTime.TryParse(Roms[0].Span, CultureInfo.InvariantCulture,
DateTimeStyles.None, out var ts)` (or `TryParseExact` with an explicit format — format string is
Claude's discretion per CONTEXT line 42). On failure the LINE must be SKIPPED, not throw. Because
this runs in the ctor, the cleanest fix is to move the parse/guard into the static `Parse` factory
(`CombatLogLine.cs:38-43`, which already returns null on `sections.Count != 5`) and pass a parsed
`DateTime` into the ctor — keeping the "guard in Parse, return null" shape (Pattern B).

### BUG-04 — static ctor `Split('_')[1]` (`CombatLogs.cs:23`)
Current:
```csharp
PlayerNames = SettingsDirectory.EnumerateFiles("*PlayerGUIState.ini")
    .Select(x => x.Name.Split('_')[1]).ToHashSet();
```
A filename without `_` yields a 1-element array → `IndexOutOfRange` → `TypeInitializationException`
at first static access. Fix (apply Pattern B "skip"): filter to names containing `_` / `Length > 1`
before indexing, e.g. `.Select(x => x.Name.Split('_')).Where(p => p.Length > 1).Select(p => p[1])`.

### BUG-05 — unguarded numeric parses (apply Pattern A to every site)
Exact current call sites (all throw on malformed input):

| Site | Current code |
|------|--------------|
| `Threat.cs:14` | `public int Value => int.Parse(Rom.Span);` |
| `Actor.cs:64` | `int.Parse(Roms[2].Slice(1, Roms[2].Span.IndexOf('/') - 1).Span)` |
| `Actor.cs:73` | `int.Parse(Roms[2].Slice(maxStart, maxLength).Span)` |
| `Actor.cs:93` | `long.Parse(Roms[0].Span.Slice(idStart + 1, idEnd - idStart - 1))` |
| `Actor.cs:100` | `long.Parse(Roms[0].Span.Slice(hash + 1, Roms[0].Span.Length - 1 - hash))` |
| `Actor.cs:107` | `long.Parse(Roms[0].Span.Slice(openIndex + 1, closeIndex - openIndex - 1))` |
| `Value.cs:47` | `ulong.Parse(Rom.Span.Slice(start + 1, end - start - 1))` |
| `GameObject.cs:75` | `ulong.Parse(Rom.Span.Slice(startIndex + 1, endIndex - startIndex - 1))` |
| `GameObject.cs:87` | `ulong.Parse(Rom.Span.Slice(startIndex + 1, endIndex - startIndex - 1))` |
| `GameObject.cs:95` | `ulong.Parse(Rom.Span.Slice(startIndex + 1, endIndex - startIndex - 1))` |

Each becomes `Xxx.TryParse(span, out var v) ? v : null` (these are nullable returns — `int?`,
`long?`, `ulong?` — except `Threat.Value` which is `int`; for Threat either change the property to
`int?` or guard the slice and return a sentinel — note `Threat.Parse` already returns the Threat,
the throw is LAZY on the `Value` property, see RESEARCH 01). The integer sites need NO `CultureInfo`
argument (Pattern A's culture arg is only for the float sites); a bare `int.TryParse(span, out v)` matches.

### BUG-06 — cache race (apply Pattern D)
Exact current sites:
- Declarations — `CombatLogs.cs:8-9`:
  ```csharp
  internal static readonly Dictionary<int, Action> ActionCache = new();
  internal static readonly Dictionary<int, GameObject> GameObjectCache = new();
  ```
- `Action.cs:47,53` — `TryGetValue(...)` then `CombatLogs.ActionCache.Add(action.GetHashCode(), action);`
- `GameObject.cs:103,108` — `TryGetValue(...)` then `CombatLogs.GameObjectCache.Add(gameObject.GetHashCode(), gameObject);`
- `Ability.cs:15,19` — `TryGetValue(...)` then `CombatLogs.GameObjectCache.Add(ability.GetHashCode(), ability);`

Convert both dictionaries to `ConcurrentDictionary<int,T>` and replace each `TryGetValue`+`Add`
pair with `GetOrAdd`. Watch the `Action.cs` case: it has a `try/catch` around construction
(`:50-59`) because the ctor can throw ("Event/Effect is null"); a naive `GetOrAdd(key, _ => new Action(rom))`
would let that exception escape. Preserve current semantics: keep the try/catch, build the Action
first, then `GetOrAdd(key, action)` (value-overload) — or only `GetOrAdd` after successful
construction. Also `GameObject.Parse` returns null when `Id == null` (`:107`) — do NOT cache a null;
construct, check Id, then `GetOrAdd`.

### BUG-07 — open log file read-only (`CombatLog.cs:24`)
Current:
```csharp
using (var stream = FileInfo.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
```
Fix (apply Pattern partial-analog from `CombatLogsMonitor.cs:170-175`, which already opens with
`FileAccess.Read, FileShare.ReadWrite`): change to `FileAccess.Read, FileShare.Read` per CONTEXT
line 34. Closest in-repo open-for-read analog:
```csharp
fileStream = new FileStream(current, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
```

---

## No Analog Found

| Pattern | Bug | Reason | Resolution |
|---------|-----|--------|------------|
| `ConcurrentDictionary` / `GetOrAdd` | BUG-06 | Repo has only plain `Dictionary` caches; no concurrent collection exists yet | Use canonical Pattern D above; AOT-safe per CONVENTIONS.md:55 |
| outer-CTS cancellation wiring | BUG-01 | No second monitor/CTS to copy from | Pure logic fix, no analog needed |

## Metadata

**Analog search scope:** `SwtorLogParser/Monitor/`, `SwtorLogParser/Model/`
**Files scanned:** 10 production files + CONVENTIONS.md + CONTEXT.md
**Grep verification:** `ConcurrentDictionary` → 0 hits (confirmed new); `TryParse`+`InvariantCulture` → `Actor.cs:147,154` only; `is null` → `CombatLogsMonitor.cs:168`.
