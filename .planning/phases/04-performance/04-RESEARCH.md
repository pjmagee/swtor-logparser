# Phase 4: Performance - Research

**Researched:** 2026-06-11
**Domain:** .NET 8 zero-allocation span/memory refactoring, console rendering, single-pass aggregation — all under a strict "output identical / tests stay green" constraint
**Confidence:** HIGH (all three requirements are localized refactors of source files read directly this session; the behavioral contract is locked by 102 currently-passing tests, verified this session)

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**PERF-01 — CombatLog read (`CombatLog.cs`)**
- `ToString()` reports the line count WITHOUT building `CombatLogLine` objects — count lines (newlines / `EnumerateLines`) only, no parse.
- Eliminate the per-line `char[]` allocation: `GetLogLines()` currently does `new ReadOnlyMemory<char>(line.ToArray())` per line. Instead slice the original string via `string.AsMemory().Slice(start, length)` so each `CombatLogLine` references a window into the single backing string — true zero-copy, preserving the `ReadOnlyMemory<char>` intent end-to-end.
- Keep `ReadToEnd()` for the batch `GetLogLines()` path (then slice into the returned string); the live reader (`ReadAsync`) is already line-by-line and is NOT the PERF-01 target.

**PERF-02 — Native CLI render (`SwtorLogParser.Native.Cli/Program.cs`)**
- Replace `Console.Clear()` + full redraw per event with cursor repositioning: `Console.SetCursorPosition(0,0)` and overwrite rows in place, padding each line to clear any trailing characters from a previously longer row — eliminating the full-screen repaint flicker.
- Scope is the Native CLI ONLY. The managed CLI uses `System.CommandLine.Rendering` (replaced in Phase 5); do not touch it here.

**PERF-03 — Accumulator / CalculateDpsHpsStats (`CombatLogsMonitor.cs`)**
- Avoid the full re-sort/re-scan per line: drop the `OrderBy(x => x.TimeStamp.TimeOfDay)` (it is only used to get first/last timestamps — track min/max directly) and collapse the multiple `Where`/`Sum`/`Count` passes over the window into a SINGLE pass in `CalculateDpsHpsStats`.
- OUTPUT MUST BE IDENTICAL: DPS, HPS, crit%, and the null-on-zero/infinity behavior must match exactly — the TEST-02 `DpsHpsMathTests` are the contract. If any optimization would change a number, do not make it.
- Keep the 10s sliding-window semantics exactly (the `Accumulator` `RemoveWhere` at `combatLog.TimeStamp.AddSeconds(-10)` stays). Do NOT touch the `DateTime.Now` window filter (IN-01, separate).

### Claude's Discretion
- Whether PERF-03 keeps incremental running totals in the `Accumulator` vs a single-pass `CalculateDpsHpsStats`, the exact slicing helper for PERF-01, and the precise in-place render layout for PERF-02 are at Claude's discretion — guided by identical output and green tests. Add a micro-benchmark or before/after note only if cheap; not required.

### Deferred Ideas (OUT OF SCOPE)
- `DateTime.Now` window filter → testability/correctness item (IN-01), not a Phase 4 perf requirement. DO NOT touch the `.Where(x => x.TimeStamp > DateTime.Now.AddSeconds(-10))` filter in `ConfigureObservables`.
- BoundedCache soft-cap-under-concurrency hardening (WR-01) — accepted as-is.
- Dependency GA upgrades (Phase 5), CI (Phase 6).
- The managed CLI renderer (`System.CommandLine.Rendering`) — replaced in Phase 5, not Phase 4.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| PERF-01 | `CombatLog` no longer re-parses the whole file just to count lines, and `GetLogLines()` avoids a `char[]` allocation per line — `Monitor/CombatLog.cs:16,28,33` | Manual offset-tracking line splitter over the `ReadToEnd()` backing string + `AsMemory().Slice(start,len)` (see PERF-01 section). `ToString()` uses a pure newline-count helper. CRLF/LF parity rules documented. |
| PERF-02 | The Native CLI renders incrementally instead of `Console.Clear()` + full redraw per event — `Native.Cli/Program.cs:40-49` | `SetCursorPosition(0,0)` + per-row pad-to-width overwrite + trailing-row clearing when the row count shrinks (see PERF-02 section). Edge cases for redirected output, window width/height, and `list` vs `monitor` documented. |
| PERF-03 | The stats accumulator avoids re-scanning and re-sorting the entire window on every line — `CombatLogsMonitor.cs:58-100` | Single-pass min/max + sum/count over `state` replacing `OrderBy` + 6 LINQ passes. Player-identity-invariant-to-ordering proof via `GroupBy(Source.Name)`. Identical-number checklist mapped to the 7 `DpsHpsMathTests` (see PERF-03 section). |
</phase_requirements>

## Summary

All three requirements are **localized, behavior-preserving refactors** of three source files, fully read this session. The behavioral contract is locked by the test suite — **verified green at 102/102 this session** (`dotnet test`, net8.0, 83 ms). PERF-01 and PERF-03 touch the core library (`IsAotCompatible=true`); none of the recommended approaches introduce reflection or any AOT-incompatible pattern. PERF-02 touches only the Native AOT CLI host and uses `System.Console` APIs that are AOT-safe.

The central risk is not the optimizations themselves but **silently changing output**. The two landmines flagged by the orchestrator are real and both are resolvable with HIGH confidence from the source:

1. **HashSet enumeration order for Player** (PERF-03): `CalculateDpsHpsStats` reads `state.ElementAt(0).Source` for `Player`. Because the Rx pipeline does `GroupBy(x => x.Source?.Name)` *before* the `Scan`/`Accumulator`, **every line in a given `state` HashSet belongs to the same player**. `Source.Name` is invariant across the set, so `Player` is invariant to which element is chosen. Dropping `OrderBy` cannot change `Player`. (The `DpsHpsMathTests` build all lines from one actor `@Aegrae#689921479616853` and never assert `Player` — only DPS/HPS/crit — so this is doubly safe.)
2. **CRLF vs LF for slicing** (PERF-01): `MemoryExtensions.EnumerateLines` treats a rich set of line terminators (`\r\n`, `\n`, `\r`, plus the Unicode breaks `\v` U+000B, `\f` U+000C, U+0085 NEL, U+2028 LS, U+2029 PS) as breaks and **excludes the terminator from each returned line**. A naive manual splitter that only handles `\n` and forgets to strip a trailing `\r` would feed `"...text\r"` into `CombatLogLine.Parse`, changing the slice content. The line-count and the slice boundaries must reproduce `EnumerateLines` semantics exactly.

**Primary recommendation:** Implement a single private offset-tracking line enumerator over the `ReadToEnd()` string for PERF-01 (one source of truth for both `ToString()`'s count and `GetLogLines()`'s slices); collapse `CalculateDpsHpsStats` into one `foreach` over `state` tracking min/max timestamp + per-category sum/critCount for PERF-03; and replace `Console.Clear()` with cursor-home + pad-to-width writes plus explicit clearing of rows vacated since the previous frame for PERF-02. Run the full 102-test suite on every commit; if any number moves, revert that change.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| File read + line slicing (PERF-01) | Core library (`SwtorLogParser.Monitor.CombatLog`) | — | Batch-read path used by `list`/diagnostics; owns IO + parse dispatch. Must stay AOT-compatible. |
| DPS/HPS aggregation (PERF-03) | Core library (`SwtorLogParser.Monitor.CombatLogsMonitor`) | — | Single producer of `PlayerStats`; all three hosts consume identical output. Must stay AOT-compatible. |
| Console rendering (PERF-02) | Native AOT CLI host (`SwtorLogParser.Native.Cli.Program`) | — | Pure presentation; no parsing/aggregation logic. Host-local, AOT-safe `System.Console` use. |

## Standard Stack

No new packages. This phase is pure refactoring against the existing BCL and the already-referenced libraries. The "stack" is the .NET 8 BCL surface used.

### Core
| API | Namespace / Type | Purpose | Why Standard |
|-----|------------------|---------|--------------|
| `string.AsMemory(int start, int length)` | `System.MemoryExtensions` | Zero-copy slice of the backing string into `ReadOnlyMemory<char>` | The BCL's canonical zero-allocation windowing over an existing string; the slice keeps the backing string alive (GC root) so it is safe for the lifetime of the returned list `[CITED: learn.microsoft.com/dotnet/api/system.memoryextensions.asmemory]` |
| `MemoryExtensions.EnumerateLines` | `System.MemoryExtensions` | Reference semantics for line splitting (what the *old* code did, what new code must match) | Defines the exact terminator set and terminator-exclusion behavior the count + slices must reproduce `[CITED: learn.microsoft.com/dotnet/api/system.memoryextensions.enumeratelines]` |
| `Console.SetCursorPosition` / `Console.Write` / `Console.WindowWidth` / `Console.WindowHeight` / `Console.IsOutputRedirected` | `System.Console` | In-place cursor-home overwrite render | Standard flicker-free console UI technique; all AOT-safe `[CITED: learn.microsoft.com/dotnet/api/system.console.setcursorposition]` |

### Supporting
| API | Purpose | When to Use |
|-----|---------|-------------|
| `ReadOnlySpan<char>.IndexOf('\n')` / manual index loop | Find line boundaries on the backing string | PERF-01 splitter — operate on `.AsSpan()` for the scan, slice with `.AsMemory()` for the result |
| `string.PadRight(width)` or `Console.Write(new string(' ', n))` | Pad a rendered row to console width to erase trailing chars from a longer previous frame | PERF-02 per-row overwrite |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Manual offset-tracking splitter (PERF-01) | `string.Split('\n')` | `Split` allocates a `string[]` plus a `string` per line — defeats the zero-copy goal entirely. Reject. |
| Manual offset-tracking splitter (PERF-01) | `ReadOnlySpan<char>.EnumerateLines()` directly | `EnumerateLines` yields `ReadOnlySpan<char>` (stack-only) and gives you no start/length offset back into the source, so you cannot produce an `AsMemory().Slice` from it without re-deriving offsets. You still need manual index math. A hybrid (use `EnumerateLines` to validate parity in a test, manual index math in production) is the cleanest. |
| Single-pass `foreach` over `HashSet` (PERF-03) | Incremental running totals in `Accumulator` | Running totals are harder to keep correct across the 10s `RemoveWhere` eviction (you'd have to subtract evicted lines) and risk drift from the test-locked numbers. Single-pass per call is O(n) over a ≤10s window (small), trivially correct, and keeps the contract obvious. Prefer single-pass unless a benchmark shows it matters (it won't at this window size). |

**Installation:** None. `git diff` only.

## Package Legitimacy Audit

Not applicable — this phase installs **no external packages**. All work is against the .NET 8 BCL and already-referenced project dependencies. No registry verification required.

## Architecture Patterns

### System Architecture Diagram

```
                         PERF-01 (batch read path)
  CombatLog.GetLogLines()
        │
        ▼
  FileStream(Read,ReadWrite) ──► StreamReader(Latin1) ──► ReadToEnd() ──► single backing string S
        │                                                                        │
        │  ToString():                                                           │
        │    CountLines(S)  ◄── shared splitter, no Parse ──────────────────────┤
        │                                                                        │
        ▼                                                                        ▼
  for each (start,len) line in S:   AsMemory(start,len) ──► CombatLogLine.Parse(rom)
        │                                   (rom is a window INTO S; S stays rooted by the list)
        ▼
  List<CombatLogLine>   ── slices remain valid as long as the list is reachable ──►


                         PERF-03 (live aggregation path) — UNCHANGED PIPELINE SHAPE
  Subject<CombatLogLine>
        │  .Where(TimeStamp > Now-10s)        ◄── DO NOT TOUCH (IN-01, deferred)
        │  .Where(Source/Name not null)
        ▼
  GroupBy(Source.Name)  ──►  per-player stream  (★ this is why Player is order-invariant)
        │
        ▼  per group: Where(IsPlayerDamage||IsPlayerHeal).DistinctUntilChanged()
  Scan(HashSet, Accumulator)   ── 10s RemoveWhere + Add (UNCHANGED) ──► state (single-player set)
        │
        ▼
  CalculateDpsHpsStats(state)
        │  OLD: OrderBy(TimeOfDay) + 6 LINQ passes (Where/Sum/Count)
        │  NEW: ONE foreach → min/max TimeStamp, dmgTotal, healTotal, dmgCount, healCount,
        │       dmgCritCount, healCritCount, totalCount;  Player = any element's Source
        ▼
  PlayerStats  ──► (identical numbers) ──► all 3 host observers


                         PERF-02 (Native CLI render)
  DpsHps.Subscribe(Update)
        │
        ▼
  Update(list, stats):  list.AddOrUpdate(stats)
        │  OLD: Console.Clear() + WriteLine per row  (full-screen flicker)
        │  NEW: SetCursorPosition(0, headerRow); for each row → pad-to-width Write;
        │       then clear any rows that existed last frame but not this frame
        ▼
  flicker-free in-place table
```

### Recommended Project Structure
No structural change. Edits are confined to:
```
SwtorLogParser/
├── Monitor/CombatLog.cs            # PERF-01: ToString() + GetLogLines() + new private splitter
└── Monitor/CombatLogsMonitor.cs    # PERF-03: CalculateDpsHpsStats() body only
SwtorLogParser.Native.Cli/
└── Program.cs                      # PERF-02: Update() (+ small render-state field if needed)
```

### Pattern 1: Offset-tracking line enumerator over a backing string (PERF-01)
**What:** A single private helper that walks the `ReadToEnd()` string once, yielding `(start, length)` pairs for each non-terminator line, with the terminator excluded — reproducing `EnumerateLines` semantics. `ToString()` consumes it for a count; `GetLogLines()` consumes it for `AsMemory().Slice(start, length)`.
**When to use:** Any time you need both a count and zero-copy slices from the same buffer and must not diverge between them. One source of truth prevents count-vs-slice drift.
**Example (illustrative — Claude's discretion on exact shape; must reproduce `EnumerateLines`):**
```csharp
// Source: pattern derived from MemoryExtensions.EnumerateLines semantics
// [CITED: learn.microsoft.com/dotnet/api/system.memoryextensions.enumeratelines]
// Reproduce: terminator EXCLUDED from each line; \r\n is a single break; bare \r and \n each break.
// NOTE: the OLD code used span.EnumerateLines() which ALSO treats \v \f NEL(U+0085) LS(U+2028) PS(U+2029)  
// as breaks. If the SWTOR logs only ever contain \r\n (verify), a \r\n + \n + \r splitter
// matches in practice; to be byte-for-byte safe, mirror the full terminator set OR validate
// parity against EnumerateLines in a Wave-0 test (see Validation Architecture).
private static IEnumerable<(int Start, int Length)> EnumerateLineSpans(string s)
{
    int i = 0, n = s.Length;
    while (i < n)
    {
        int start = i;
        while (i < n && s[i] != '\r' && s[i] != '\n') i++;   // (+ other terminators if matching EnumerateLines fully)
        yield return (start, i - start);                      // terminator excluded
        if (i < n)
        {
            if (s[i] == '\r' && i + 1 < n && s[i + 1] == '\n') i += 2; // CRLF as one break
            else i += 1;
        }
    }
}
```
- `ToString()`: count the pairs, applying the SAME empty-line skip the old code used (`if (line.IsEmpty) continue;`). **Important parity note:** the OLD `ToString()` counted `GetLogLines().Count`, which is the count of lines that **parsed successfully AND were non-empty**, not the raw line count. See Pitfall 1 — the count contract is "parseable non-empty lines," not "newlines."
- `GetLogLines()`: for each pair with `Length > 0`, `var rom = s.AsMemory(start, length); var cll = CombatLogLine.Parse(rom);` add if non-null.

### Pattern 2: Single-pass aggregation replacing OrderBy + N LINQ passes (PERF-03)
**What:** One `foreach` over the `HashSet<CombatLogLine> state` computing every needed quantity; no sort, no intermediate lists.
**When to use:** When a sort exists only to read endpoints (min/max) and multiple `Where/Sum/Count` re-scan the same collection.
**Example (illustrative; must yield identical numbers):**
```csharp
// Replaces CombatLogsMonitor.CalculateDpsHpsStats lines 88-118 (read this session).
// Player is order-invariant: GroupBy(Source.Name) upstream => all lines share Source.
internal PlayerStats CalculateDpsHpsStats(HashSet<CombatLogLine> state)
{
    DateTime min = DateTime.MaxValue, max = DateTime.MinValue;
    int dmgTotal = 0, healTotal = 0, dmgCount = 0, healCount = 0, dmgCrit = 0, healCrit = 0;
    Actor? player = null;

    foreach (var line in state)
    {
        var ts = line.TimeStamp;
        if (ts < min) min = ts;
        if (ts > max) max = ts;
        player ??= line.Source;                       // any element; invariant within the group

        if (line.IsPlayerDamage())
        {
            dmgCount++;
            dmgTotal += line.Value!.Total;
            if (line.Value!.IsCritical) dmgCrit++;
        }
        else if (line.IsPlayerHeal())                 // see Pitfall 3: damage/heal are mutually exclusive here
        {
            healCount++;
            healTotal += line.Value!.Total;
            if (line.Value!.IsCritical) healCrit++;
        }
    }

    var timeSpan = state.Count > 1 ? (max - min) : TimeSpan.FromSeconds(1);

    double dpsCrit = (double)dmgCrit / state.Count * 100;
    double hpsCrit = (double)healCrit / state.Count * 100;

    double? dps = dmgCount > 0 ? dmgTotal / timeSpan.TotalSeconds : null;
    double? hps = healCount > 0 ? healTotal / timeSpan.TotalSeconds : null;

    double? dpsCritP = double.IsInfinity(dpsCrit) || dpsCrit == 0.0d ? null : dpsCrit;
    double? hpsCritP = double.IsInfinity(hpsCrit) || hpsCrit == 0.0d ? null : hpsCrit;

    return new PlayerStats { Player = player!, DPS = dps, HPS = hps, DPSCritP = dpsCritP, HPSCritP = hpsCritP };
}
```
**Equivalence to the locked original (each must match the read source at `CombatLogsMonitor.cs:88-118`):**
- `timeSpan`: original is `items.Count > 1 ? items[^1].TimeStamp - items[0].TimeStamp : 1s`, where `items` is `state.OrderBy(TimeOfDay)`. After sort, `items[0]` = earliest TimeOfDay, `items[^1]` = latest TimeOfDay → identical to `max - min` **provided all timestamps fall on the same calendar day** (the sort key is `TimeOfDay`, not full `DateTime`). The 10s window guarantees ≤10s spread, so `max-min` on full `DateTime` equals `latestTimeOfDay - earliestTimeOfDay` except across a midnight boundary — see Pitfall 4. Use `state.Count` for the `>1` guard (original used `items.Count`, which equals `state.Count`).
- `dpsCrit`/`hpsCrit`: original divides by `state.Count` (NOT damage/heal count) — preserved exactly. Crit numerator counts `IsCritical` within the damage/heal subset — preserved.
- `dps`/`hps` null-on-zero-count and `dpsCritP`/`hpsCritP` null-on-infinity-or-exactly-0.0 — preserved verbatim.

### Pattern 3: Cursor-home in-place overwrite with vacated-row clearing (PERF-02)
**What:** Move cursor to a fixed origin each frame, overwrite each active row padded to the console width, then blank out any rows that were drawn last frame but have no content this frame.
**When to use:** Live console dashboards where `Console.Clear()` causes visible flicker.
**Example (illustrative):**
```csharp
private static int _lastRowCount;   // host-local render state

private static void Update(SlidingExpirationList list, CombatLogsMonitor.PlayerStats playerStats)
{
    list.AddOrUpdate(playerStats);
    if (Console.IsOutputRedirected) { /* fall back to plain WriteLine, no cursor games */ }

    int width = Math.Max(1, Console.WindowWidth - 1);
    Console.SetCursorPosition(0, 1);           // row 0 reserved for the filename header (OnCombatLogAdded)

    int row = 0;
    foreach (var item in list.Items)
    {
        string text = string.Format("{0}: DPS: {1} ({2}%); HPS: {3} ({4}%)", /* ...same format as today... */);
        Console.SetCursorPosition(0, 1 + row);
        Console.Write(text.Length > width ? text[..width] : text.PadRight(width));
        row++;
    }
    for (int r = row; r < _lastRowCount; r++)  // clear rows that shrank away this frame
    {
        Console.SetCursorPosition(0, 1 + r);
        Console.Write(new string(' ', width));
    }
    _lastRowCount = row;
}
```

### Anti-Patterns to Avoid
- **`line.ToArray()` per line (the current PERF-01 defect):** allocates a `char[]` + a `ReadOnlyMemory<char>` per line, defeating the zero-copy design. Replace with `AsMemory().Slice`.
- **`GetLogLines().Count` to count lines (the current `ToString()` defect):** parses every line just to count. Count via the splitter only.
- **`Console.Clear()` per event (the current PERF-02 defect):** full-screen repaint → flicker. Use cursor-home + pad.
- **Dropping `OrderBy` but then re-deriving endpoints with `state.Min()/state.Max()` (two passes):** that's still two scans. Track min/max in the single `foreach`.
- **Subtracting evicted lines from running totals in `Accumulator`:** error-prone vs. the locked numbers; not needed at a ≤10s window.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Slicing a string without copying | A `char[]` buffer copy | `string.AsMemory(start, len)` | BCL gives true zero-copy + keeps the source rooted; copying is exactly the defect being removed |
| Knowing what counts as a "line" | Ad-hoc `\n`-only splitter | Mirror `MemoryExtensions.EnumerateLines` terminator set (or validate against it in a test) | The old code used `EnumerateLines`; a narrower splitter silently changes counts/slices on `\r`-only or unusual terminators |
| Padding/erasing console rows | Manual VT escape sequences | `Console.SetCursorPosition` + `PadRight`/space-fill | AOT-safe, cross-terminal, no escape-sequence portability issues |

**Key insight:** every "custom" path here either reintroduces an allocation the project explicitly removed, or risks diverging from the test-locked output. The BCL primitives are both faster and safer.

## Runtime State Inventory

Not a rename/refactor-of-identifiers phase, but it is a behavior-preserving refactor, so the relevant runtime-state question is **"what cached/stored state could a perf change corrupt or invalidate?"** Answered explicitly:

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | None — no databases/datastores. Logs are read-only files; the parser never writes. | None |
| Live service config | None — no external services hold state for this app. | None |
| OS-registered state | None — no scheduled tasks / services / pm2 / systemd entries reference this code. | None |
| Secrets/env vars | None — STACK.md confirms no env vars or config files are read. | None |
| Build artifacts | `obj/`/`bin/` for `SwtorLogParser` and `SwtorLogParser.Native.Cli` will rebuild; no stale package metadata (no `egg-info`/global installs). | Standard `dotnet build`; for PERF-02 confidence, also `dotnet publish` the Native AOT CLI once to confirm no AOT regression (see Environment Availability). |
| In-process caches (project-specific) | `CombatLogs.GameObjectCache`/`ActionCache` (`BoundedCache`), `Ability` cache. **PERF-01's `AsMemory` slices now share a backing string** — but caches key on **content** (RFCT-03 made keys content-based, not `Rom.GetHashCode()`), so slice identity does not affect cache correctness. | None — verified caches are content-keyed (CLAUDE.md RFCT-03), so zero-copy slicing is safe w.r.t. caching |

**Nothing found in categories Stored/Live/OS/Secrets:** stated explicitly above — verified against STACK.md ("No environment variables or external configuration files are read") and ARCHITECTURE.md (hosts are pure consumers; single in-process producer).

## Common Pitfalls

### Pitfall 1: `ToString()` count contract is "parseable non-empty lines," not "newline count"
**What goes wrong:** A reviewer assumes `ToString()` should report the raw number of newlines. The OLD code returned `GetLogLines().Count` — i.e. lines that are **non-empty AND parsed to a non-null `CombatLogLine`** (`Parse` returns null when sections != 5 or the timestamp doesn't parse). A blank-line-skipping, parse-failure-skipping count is the locked behavior.
**Why it happens:** The CONTEXT says "count lines (newlines / `EnumerateLines`) only, no parse," which is true for *avoiding the parse cost* but the resulting NUMBER must still equal the old `GetLogLines().Count`.
**How to avoid:** Decide the count semantics deliberately. The cheapest *correct-to-old-behavior* count still has to drop empty lines AND lines that would fail `Parse`. A pure newline count will over-count if the file has trailing blank lines or malformed lines. Two safe options: (a) accept that `ToString()` is diagnostic-only and the exact number is not test-locked (confirm: no test asserts `CombatLog.ToString()` — verified, `DpsHpsMathTests` and the parser tests do not) and use a fast non-empty-line count; or (b) keep exact parity by counting lines that `Parse` accepts (which reintroduces parse cost — defeats the goal). **Recommendation:** option (a) — fast non-empty-line count — and note in the plan that `ToString()` output may differ for files containing malformed lines, which is acceptable because it is unobserved by tests and is a human-readable diagnostic string. `[ASSUMED]` that no consumer asserts the exact `ToString()` integer — see Assumptions Log A1.
**Warning signs:** A new test pinning `CombatLog.ToString()` to an exact number would convert this from "discretionary" to "locked"; none exists today.

### Pitfall 2: Backing-string lifetime / use-after-free for `AsMemory` slices
**What goes wrong:** Slicing a `string` that gets collected or comes from a pooled/`stackalloc` buffer would dangle.
**Why it happens:** Confusion between `ReadToEnd()` (heap `string`, GC-rooted) and span-over-stack data.
**How to avoid:** `ReadToEnd()` returns a normal heap `string`. Each `AsMemory().Slice` holds a reference to that string, so the string stays alive exactly as long as any `CombatLogLine` in the returned list is reachable. This is safe — explicitly stated in CONTEXT specifics and confirmed by `CombatLogLine.Parse` storing the `rom` in a field. **Do not** slice a `Span`/`stackalloc` buffer; **do** slice the `string` from `ReadToEnd()`. No `using`-scope trap: the `string` is independent of the disposed `StreamReader`/`FileStream`.
**Warning signs:** Slicing inside the `using` block is fine; the danger would be slicing a `Span<char>` rented from `ArrayPool` and returning it — not what we're doing.

### Pitfall 3: Damage and heal are NOT guaranteed mutually exclusive — match the original's independent passes
**What goes wrong:** Using `else if` for damage/heal in the single pass assumes a line can't be both. The ORIGINAL code ran `items.Where(IsPlayerHeal)` and `items.Where(IsPlayerDamage)` as **independent** filters, so if a line satisfied both predicates it would be counted in BOTH totals.
**Why it happens:** `IsPlayerDamage` requires `Action == ApplyEffectDamage`; `IsPlayerHeal` requires `Action == ApplyEffectHeal`. A single `CombatLogLine` has exactly one `Action`, so in practice the two are mutually exclusive and `else if` is equivalent. BUT to be byte-for-byte identical to the locked behavior without relying on that invariant, prefer two independent `if`s (not `else if`) in the single pass.
**How to avoid:** Use two separate `if (line.IsPlayerDamage())` / `if (line.IsPlayerHeal())` checks in the one `foreach` (still single-pass — one iteration, two predicate evaluations), matching the original's independent `Where` semantics exactly. The `Crit_Percent_Computed` test divides crit by `state.Count` and expects 50% for 1-of-2; verify against that.
**Warning signs:** If a test starts failing on crit% after switching to `else if`, this is the cause.

### Pitfall 4: `OrderBy(TimeOfDay)` vs `max-min` on full `DateTime` differ across midnight
**What goes wrong:** The original sorts by `TimeStamp.TimeOfDay` (clock time within a day) and subtracts full `DateTime`s. If the 10s window straddled midnight (e.g. 23:59:57 and 00:00:02), `TimeOfDay` ordering would put 00:00:02 *before* 23:59:57, so `items[0]`=00:00:02, `items[^1]`=23:59:57, giving a **negative or wrong** `timeSpan`. A `max-min` on full `DateTime` would also be affected because `CombatLogLine` parses only `HH:mm:ss[.fff]` with no date — all timestamps get the same `DateTime.Today`-style date, so a wrap actually produces a *negative* delta in BOTH old and new code.
**Why it happens:** SWTOR timestamps are time-of-day only (`TimeFormats = { "HH:mm:ss", "HH:mm:ss.fff" }`, verified `CombatLogLine.cs:7`); there is no date component, so any across-midnight window is already mishandled by the ORIGINAL code.
**How to avoid:** Reproduce the ORIGINAL's behavior, do not "fix" it. To match `OrderBy(TimeOfDay)` endpoints exactly, track min/max **by `TimeStamp.TimeOfDay`** (or equivalently by full `TimeStamp`, since all lines share the same date) and compute `timeSpan = maxByTimeOfDay - minByTimeOfDay`. Because Phase 4 is zero-behavior-change, the across-midnight quirk must be PRESERVED, not corrected (correcting it is out of scope — that's an IN-01-style correctness item, not PERF-03). Track min/max on the same key the original sorted by.
**Warning signs:** None of the current 7 tests cross midnight (all at 20:00:xx), so this is unobserved — but the plan must explicitly track min/max by the same `TimeOfDay` ordering to avoid an accidental "improvement." `[ASSUMED]` the across-midnight quirk is intended-preserve, not test-asserted — see Assumptions Log A2.

### Pitfall 5: PERF-02 cursor positioning when output is redirected or window is tiny
**What goes wrong:** `Console.SetCursorPosition`/`WindowWidth`/`WindowHeight` throw or misbehave when stdout is redirected (no console buffer) or the requested row exceeds the buffer height.
**Why it happens:** A redirected `monitor` (piped to a file) has no cursor; `WindowWidth` can be 0; many rows can exceed `WindowHeight`.
**How to avoid:** Guard with `Console.IsOutputRedirected` (fall back to plain `WriteLine`, no cursor calls). Clamp `width = Math.Max(1, Console.WindowWidth - 1)`. Cap the number of in-place rows to `Console.WindowHeight - headerRows` (or accept scrolling, matching today's `WriteLine` behavior). The `list` command does plain `WriteLine` and is NOT touched (no cursor logic) — only `monitor`'s `Update` and the existing `OnCombatLogAdded` header use the cursor. Keep `OnCombatLogAdded`'s row-0 header (it already does pad-to-`WindowWidth-1`) consistent with `Update` starting at row 1.
**Warning signs:** `ArgumentOutOfRangeException` from `SetCursorPosition` (row/col outside buffer) or `IOException` when redirected.

## Code Examples

(See Patterns 1–3 above for the three concrete implementations. Each is illustrative; final shape is Claude's discretion per CONTEXT.)

### Verified existing behavior to preserve (read this session)
```csharp
// CombatLogsMonitor.cs:90-118 — the LOCKED original. New single-pass output must match this.
var items = state.OrderBy(x => x.TimeStamp.TimeOfDay).ToList();
var heals  = items.Where(pe => pe.IsPlayerHeal()).ToList();
var damage = items.Where(pe => pe.IsPlayerDamage()).ToList();
var timeSpan = items.Count > 1 ? (items[^1].TimeStamp - items[0].TimeStamp) : TimeSpan.FromSeconds(1);
int damageTotal = damage.Sum(pe => pe.Value!.Total);
int healTotal   = heals.Sum(pe => pe.Value!.Total);
double dpsCrit = (double)damage.Count(pe => pe.Value!.IsCritical) / state.Count * 100;
double hpsCrit = (double)heals.Count(pe => pe.Value!.IsCritical)  / state.Count * 100;
double? dps = damage.Count > 0 ? damageTotal / timeSpan.TotalSeconds : null;
double? hps = heals.Count  > 0 ? healTotal   / timeSpan.TotalSeconds : null;
double? dpsCritP = double.IsInfinity(dpsCrit) || dpsCrit == 0.0d ? null : dpsCrit;
double? hpsCritP = double.IsInfinity(hpsCrit) || hpsCrit == 0.0d ? null : hpsCrit;
Player = state.ElementAt(0).Source!;   // ★ order-invariant because GroupBy(Source.Name) upstream
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `new ReadOnlyMemory<char>(line.ToArray())` per line | `string.AsMemory(start, len)` zero-copy slice | This phase (PERF-01) | Removes one `char[]` + one wrapper alloc per line |
| Count lines by full parse (`GetLogLines().Count`) | Count via lightweight splitter, no `Parse` | This phase (PERF-01) | Removes full parse cost from `ToString()` |
| `OrderBy(TimeOfDay)` + 6 LINQ passes per line | Single `foreach` min/max + sums/counts | This phase (PERF-03) | O(n log n) sort + 6 scans → 1 scan; identical numbers |
| `Console.Clear()` + full redraw | Cursor-home + pad-to-width overwrite | This phase (PERF-02) | Eliminates full-screen flicker |

**Deprecated/outdated:** none introduced. `ReadOnlySpan<char>.EnumerateLines` / `string.AsMemory` are stable .NET 8 BCL APIs (no deprecation).

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | No consumer/test asserts the exact integer from `CombatLog.ToString()`, so a fast non-empty-line count that may differ from the old parse-filtered count for malformed-line files is acceptable. | Pitfall 1 | If a hidden assertion exists, `ToString()` parity breaks. Mitigation: grep tests for `ToString`/`.Count` on `CombatLog` before choosing count semantics (none found this session). |
| A2 | The across-midnight `timeSpan` quirk in the original `OrderBy(TimeOfDay)` is intended to be preserved (Phase 4 is zero-behavior-change), not corrected. | Pitfall 4 | If the intent were to fix it, that's a correctness change — but that belongs to IN-01 (deferred), so preserving is correct for THIS phase. |
| A3 | SWTOR combat logs use `\r\n` (Windows CRLF) line endings; a CRLF+LF+CR splitter matches `EnumerateLines` in practice for these files. | PERF-01 / Pitfall (CRLF) | If logs contain exotic terminators (`` etc.), a narrow splitter could miscount/mis-slice vs the old `EnumerateLines`. Mitigation: add the parity test in Validation Architecture, or mirror the full `EnumerateLines` terminator set. |

**These three assumptions should be confirmed (grep for `ToString` assertions; confirm log line-ending convention; decide preserve-vs-fix on midnight) before or during planning. None blocks progress; all have stated mitigations.**

## Open Questions

1. **Exact `ToString()` count semantics (fast newline-ish count vs. old parse-filtered count).**
   - What we know: old `ToString()` = count of non-empty, successfully-parsed lines. No test pins it.
   - What's unclear: whether any human-facing diagnostic relies on the exact number for malformed-line files.
   - Recommendation: use a fast non-empty-line count (Pitfall 1 option a), note the minor divergence in the plan, and add a grep-for-assertions check in Wave 0. Re-evaluate only if a `ToString` assertion surfaces.

2. **Full `EnumerateLines` terminator-set parity vs. CRLF-only splitter.**
   - What we know: old code used `EnumerateLines` (rich terminator set, terminator excluded). Logs are presumably CRLF.
   - What's unclear: whether any log line uses a non-CRLF/LF terminator.
   - Recommendation: add a Wave-0 parity test asserting the new splitter yields the same line set as `EnumerateLines` over a fixture containing `\r\n`, `\n`, and a trailing blank line. Cheapest way to lock CRLF correctness.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK | build/test all phases | ✓ | 10.0.301 (builds net8.0 targets fine) | — |
| `dotnet test` (xUnit) | regression gate (102 tests) | ✓ | suite runs green, 102/102, 83 ms (verified this session) | — |
| Native AOT toolchain | PERF-02 publish smoke-test (`dotnet publish` Native.Cli) | ? (not probed) | — | Build + run the Native CLI in JIT mode (`dotnet run`) to verify console behavior; AOT publish is a nice-to-have confidence check, not a blocker since no new AOT-incompatible code is added |
| An interactive console (not redirected) | PERF-02 manual flicker verification | ✓ (Windows terminal) | — | If only redirected output is available, rely on the `IsOutputRedirected` guard + code review; flicker is visually verified |

**Missing dependencies with no fallback:** none.
**Missing dependencies with fallback:** Native AOT publish for PERF-02 — fall back to JIT `dotnet run` for behavior verification; AOT compatibility is preserved by construction (no reflection added).

## Validation Architecture

> `workflow.nyquist_validation` is `true` (config.json) — section included.

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit `2.5.0-pre.44` (net8.0) |
| Config file | none (SDK-style `.csproj`; `SwtorLogParser.Tests.csproj` verified) |
| Quick run command | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj --nologo` |
| Full suite command | `dotnet test SwtorLogParser.slnx --nologo` (or the test csproj — only one test project exists) |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| PERF-03 | DPS identical (3000 over 1.0s) | unit | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj --filter Dps_Computed_From_Known_Damage` | ✅ `DpsHpsMathTests.cs:58` |
| PERF-03 | HPS identical (2000 over 1.0s) | unit | `... --filter Hps_Computed_From_Known_Heals` | ✅ `DpsHpsMathTests.cs:75` |
| PERF-03 | crit% identical (50% of 2 lines) | unit | `... --filter Crit_Percent_Computed` | ✅ `DpsHpsMathTests.cs:93` |
| PERF-03 | zero crit → null | unit | `... --filter Zero_Crit_Maps_To_Null` | ✅ `DpsHpsMathTests.cs:112` |
| PERF-03 | 10s window evicts old line | unit | `... --filter Window_Expiry_Removes_Old_Lines` | ✅ `DpsHpsMathTests.cs:129` |
| PERF-03 | 9s line kept | unit | `... --filter Window_Keeps_Recent_Lines` | ✅ `DpsHpsMathTests.cs:149` |
| PERF-03 | `timeSpan`=1s when count≤1 → DPS/HPS not divided-by-zero (covered indirectly; add explicit single-line case in Wave 0) | unit | (new) `Single_Line_Uses_OneSecond_Window` | ❌ Wave 0 |
| PERF-01 | `GetLogLines()` parses a known fixture to the same `CombatLogLine` set (zero-copy slices valid) | unit | (new) over a temp log file | ❌ Wave 0 |
| PERF-01 | new splitter matches `MemoryExtensions.EnumerateLines` line set (CRLF/LF/trailing-blank parity) | unit | (new) parity test | ❌ Wave 0 |
| PERF-01 | `ToString()` count semantics locked (or documented as diagnostic-only) | unit | (new, optional per A1) | ❌ Wave 0 |
| PERF-02 | render path: no exception when output redirected; rows pad/clear correctly | manual + smoke | run `monitor` in a console; pipe `monitor` to a file (redirected guard) | ❌ manual (console visual) |

### Sampling Rate
- **Per task commit:** `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj --nologo` (full 102 — suite runs in <100 ms, no reason to sub-sample).
- **Per wave merge:** full suite + `dotnet build SwtorLogParser.slnx`.
- **Phase gate:** full suite green (102+ with Wave-0 additions) + manual PERF-02 flicker check before `/gsd-verify-work`.

### Wave 0 Gaps
- [ ] `SwtorLogParser.Tests/CombatLogReadTests.cs` (new) — PERF-01: write a temp fixture file (mixed CRLF/LF, a blank line, a malformed line), assert `GetLogLines()` returns the expected parsed `CombatLogLine` set and that slices remain valid after the read scope; assert the splitter matches `EnumerateLines`.
- [ ] `DpsHpsMathTests.cs` — add `Single_Line_Uses_OneSecond_Window` to lock the `count<=1 ⇒ 1s` branch explicitly (currently only covered transitively).
- [ ] (Optional, per A1) `ToString()` count assertion or an explicit decision note that it is diagnostic-only.
- [ ] Framework install: none — xUnit already present and green.

*PERF-02 has no automated test added (console rendering is visual); it is covered by code review + the `IsOutputRedirected` guard + a manual run. This is acceptable and called out so the verifier doesn't expect an automated flicker test.*

## Security Domain

> `security_enforcement` is `true`, `security_asvs_level` 1. This phase performs no new IO sinks, no parsing of new untrusted formats, no network/auth/crypto. It refactors existing read-only file parsing and console output.

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | n/a — desktop log reader, no auth |
| V3 Session Management | no | n/a |
| V4 Access Control | no | n/a |
| V5 Input Validation | yes (already enforced) | `CombatLogLine.Parse` returns null on malformed input (5-section + timestamp guards); BUG-05 made numeric parses skip malformed lines. PERF-01 must keep feeding the SAME `Parse` (do not bypass validation by slicing differently). |
| V6 Cryptography | no | n/a |

### Known Threat Patterns for this stack

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Malformed/oversized log line causing exception or OOB slice | Denial of Service / Tampering | Keep routing every slice through `CombatLogLine.Parse` (null-on-invalid). PERF-01's `AsMemory(start,len)` must use validated `start`/`len` within bounds (the splitter's indices are derived from the string length — bounded by construction). |
| `SetCursorPosition` out-of-range under redirected/odd console | Denial of Service (crash) | `IsOutputRedirected` guard + width/height clamps (Pitfall 5). |
| Backing-string dangling slice | Tampering (memory) | Slice only the heap `string` from `ReadToEnd()`, never a `stackalloc`/pooled buffer (Pitfall 2). |

No new attack surface is introduced; the phase reduces allocation and rendering work only.

## Sources

### Primary (HIGH confidence)
- Source files read this session: `SwtorLogParser/Monitor/CombatLog.cs`, `SwtorLogParser/Monitor/CombatLogsMonitor.cs`, `SwtorLogParser.Native.Cli/Program.cs`, `SwtorLogParser/Model/CombatLogLine.cs`, `SwtorLogParser/Model/Actor.cs`, `SwtorLogParser/Model/Value.cs`, `SwtorLogParser/Model/CombatLogLineComparer.cs`, `SwtorLogParser/Extensions/CombatLogLineExtensions.cs`, `SwtorLogParser/View/SlidingExpirationList.cs`, `SwtorLogParser/View/Entry.cs`, `SwtorLogParser.Tests/DpsHpsMathTests.cs`, `SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` — all line numbers in this doc verified against current (post-Phase-3) source.
- `dotnet test` run this session: 102/102 passed, net8.0 (regression contract confirmed green).
- `CLAUDE.md`, `04-CONTEXT.md`, `REQUIREMENTS.md`, `.planning/config.json` — read this session.

### Secondary (MEDIUM confidence)
- .NET BCL behavior for `string.AsMemory`, `MemoryExtensions.EnumerateLines`, and `Console.SetCursorPosition` `[CITED: learn.microsoft.com/dotnet/api]` — stable, well-documented .NET 8 APIs; exact terminator set of `EnumerateLines` should be re-confirmed against the docs when writing the parity test.

### Tertiary (LOW confidence)
- None.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new packages; only BCL APIs already in use by the project.
- Architecture (the three refactors): HIGH — each is a localized rewrite of code read this session, with the equivalence to locked behavior worked out line-by-line.
- Pitfalls: HIGH for the in-source landmines (Player order-invariance proven via `GroupBy`; midnight quirk; damage/heal independence); MEDIUM for the `EnumerateLines` terminator-set parity (depends on actual log line endings — mitigated by the proposed parity test).

**Research date:** 2026-06-11
**Valid until:** 2026-07-11 (stable .NET 8 BCL + frozen source; refresh only if the source files or `DpsHpsMathTests` change before planning).
