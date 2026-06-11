# Phase 4: Performance - Pattern Map

**Mapped:** 2026-06-11
**Files analyzed:** 3 (all modify-existing, no new files)
**Analogs found:** 2 strong in-repo / 3 total (1 has no in-repo analog — minimal example supplied)

## File Classification

| Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---------------|------|-----------|----------------|---------------|
| `SwtorLogParser/Monitor/CombatLog.cs` | service (file reader) | file-I/O / batch | `SwtorLogParser/Model/CombatLogLine.cs` `GetSections` | exact (zero-copy slice) |
| `SwtorLogParser/Monitor/CombatLogsMonitor.cs` (`CalculateDpsHpsStats`/`Accumulator`) | service (aggregation) | transform / single-pass | none ideal — repo has no single-pass min/max aggregator | role-match (span-scan loops in Model) |
| `SwtorLogParser.Native.Cli/Program.cs` (`Update`) | view (console render) | request-response (event-driven redraw) | `OnCombatLogAdded` in same file (in-place overwrite) | partial (canonical example supplied) |

---

## Pattern Assignments

### `SwtorLogParser/Monitor/CombatLog.cs` (PERF-01)

**EXACT current code that changes** (`CombatLog.cs:14-40`):

```csharp
public override string ToString()
{
    var count = GetLogLines().Count;          // line 16 — full parse just to count
    return $"{FileInfo.Name}: {count}";
}

public List<CombatLogLine> GetLogLines()
{
    var items = new List<CombatLogLine>();

    using (var stream = FileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
    {
        using (var reader = new StreamReader(stream, System.Text.Encoding.Latin1))
        {
            var span = reader.ReadToEnd().AsSpan();           // line 28

            foreach (var line in span.EnumerateLines())
            {
                if (line.IsEmpty) continue;
                var combatLogLine = CombatLogLine.Parse(new ReadOnlyMemory<char>(line.ToArray()));  // line 33 — per-line char[] alloc
                if (combatLogLine is not null) items.Add(combatLogLine);
            }
        }
    }

    return items;
}
```

**Two changes:**
1. `ToString()` (line 16): count lines without parsing. `ReadToEnd()` then count via `ReadOnlySpan<char>.EnumerateLines()` (skip empty lines to match `GetLogLines` semantics) — no `CombatLogLine.Parse`.
2. `GetLogLines()` (line 33): drop `line.ToArray()`. Slice the backing string instead. Because `EnumerateLines()` over a `Span` does not expose offsets, switch the backing to a `string text = reader.ReadToEnd();` and slice via `text.AsMemory(start, length)`, OR keep `EnumerateLines` over the string and compute offsets. The slice must reference the single heap `string` returned by `ReadToEnd()` (it stays reachable through the returned `List<CombatLogLine>` Roms — safe, no use-after-free).

**Analog — zero-copy slice via index math** (`CombatLogLine.cs:61-90`, `GetSections`). This is the canonical in-repo pattern: scan, track `start`/`end` indices, emit `rom.Slice(start, end - start)` with NO `.ToArray()`:

```csharp
private static List<ReadOnlyMemory<char>> GetSections(ReadOnlyMemory<char> rom)
{
    var roms = new List<ReadOnlyMemory<char>>();
    const char sectionOpen = '[';
    const char sectionClose = ']';
    var start = -1;
    var end = -1;

    for (var i = 0; i < rom.Length; i++)
    {
        if (rom.Span[i] == sectionOpen)      start = i + 1;
        else if (rom.Span[i] == sectionClose)
        {
            end = i;
            if (start != -1)
            {
                roms.Add(rom.Slice(start, end - start));   // <-- zero-copy slice, no ToArray()
                start = -1;
            }
        }
    }
    return roms;
}
```

Also see `Actor.cs:48-54`, `:99`, `:106`, `:117` for `rom.Slice(start, length)` / `Span.Slice(...)` index math. Copy the `start`/index-tracking shape and the `.Slice(start, length)` call; apply it to line boundaries over the `ReadToEnd()` string.

**Anti-pattern to AVOID (do not copy):** `Actor.GetSubSections` (`Actor.cs:170-186`) wraps slices in `new ReadOnlyMemory<char>(rom.Slice(...).ToArray())` — the SAME `.ToArray()` defeat PERF-01 removes. It is out of scope here; just don't mirror it.

---

### `SwtorLogParser/Monitor/CombatLogsMonitor.cs` — `CalculateDpsHpsStats` / `Accumulator` (PERF-03)

**EXACT current code that changes** (`CombatLogsMonitor.cs:87-118`):

```csharp
internal PlayerStats CalculateDpsHpsStats(HashSet<CombatLogLine> state)
{
    // Oldest to latest
    var items = state.OrderBy(x => x.TimeStamp.TimeOfDay).ToList();        // line 90 — DROP the sort

    var heals  = items.Where(pe => pe.IsPlayerHeal()).ToList();            // line 92 — pass 2
    var damage = items.Where(pe => pe.IsPlayerDamage()).ToList();          // line 93 — pass 3

    var timeSpan =
        items.Count > 1 ? (items[^1].TimeStamp - items[0].TimeStamp) : TimeSpan.FromSeconds(1);  // line 95-96 — only needs min/max ts

    int damageTotal = damage.Sum(pe => pe.Value!.Total);                   // line 98 — pass 4
    int healTotal   = heals.Sum(pe => pe.Value!.Total);                    // line 99 — pass 5

    double dpsCrit = (double)damage.Count(pe => pe.Value!.IsCritical) / state.Count * 100;  // line 101 — pass 6
    double hpsCrit = (double)heals.Count(pe => pe.Value!.IsCritical) / state.Count * 100;   // line 102 — pass 7

    double? dps = damage.Count > 0 ? damageTotal / timeSpan.TotalSeconds : null;
    double? hps = heals.Count  > 0 ? healTotal   / timeSpan.TotalSeconds : null;

    double? dpsCritP = double.IsInfinity(dpsCrit) || dpsCrit == 0.0d ? null : dpsCrit;
    double? hpsCritP = double.IsInfinity(hpsCrit) || hpsCrit == 0.0d ? null : hpsCrit;

    return new PlayerStats
    {
        Player    = state.ElementAt(0).Source!,    // line 112 — ANY element, not the sorted first
        DPS       = dps,
        HPS       = hps,
        DPSCritP  = dpsCritP,
        HPSCritP  = hpsCritP,
    };
}
```

**The collapse (single pass over `state`):** one `foreach (var line in state)` that, per line, tracks:
- `minTs` / `maxTs` (replaces the `OrderBy` + `items[0]` / `items[^1]` — see min/max note below)
- `damageCount`, `damageTotal`, `damageCrit`
- `healCount`, `healTotal`, `healCrit`
- `firstSource` (any element's `Source`, replacing `state.ElementAt(0).Source` — order does not matter)

Then compute `timeSpan = state.Count > 1 ? (maxTs - minTs) : TimeSpan.FromSeconds(1)` and the SAME four output formulas verbatim.

**Output-identity rules (must preserve byte-for-byte vs. TEST-02):**
- `timeSpan` denominator: `items.Count > 1` → use `state.Count > 1`; `(items[^1].TimeStamp - items[0].TimeStamp)` over the time-of-day sort == `(maxTs - minTs)` (the sort key was `TimeStamp.TimeOfDay`; track min/max on the SAME `.TimeStamp.TimeOfDay` key — do not switch to full `DateTime` if test inputs ever cross midnight; the current code uses `.TimeOfDay`, so match it).
- crit denominator is `state.Count` (NOT `damage.Count`) — keep dividing by total `state.Count` (locked by `Crit_Percent_Computed`: 1 crit of 2 lines → 50; `Zero_Crit_Maps_To_Null`).
- `dpsCritP`/`hpsCritP`: `double.IsInfinity(x) || x == 0.0d ? null : x` — keep the exact null-mapping (zero AND infinity → null).
- `dps`/`hps` null-on-zero-count: `damage.Count > 0 ? ... : null` — keep.
- `Player`: any element's `Source` is fine — the comparer hashes on raw Rom, every line in a HashSet group shares the same `Source.Name` (grouped upstream), so `state.ElementAt(0)` vs. first-enumerated are equivalent. Capture the first iterated `Source`.

**`Accumulator` (`CombatLogsMonitor.cs:71-82`) — DO NOT change behavior:**

```csharp
lock (Lock)
{
    state.RemoveWhere(line => line.TimeStamp < combatLog.TimeStamp.AddSeconds(-10));  // line 78 — KEEP exactly
    state.Add(combatLog);
    return state;
}
```

Keep the `static readonly object Lock`, the 10s `RemoveWhere`, and the `Add`. Locked by `Window_Expiry_Removes_Old_Lines` / `Window_Keeps_Recent_Lines`. (Optional discretion: maintain running totals in the Accumulator instead — but only if every TEST-02 number stays identical; the safer, sufficient win is the single-pass `CalculateDpsHpsStats`.)

**Analog — min/max + accumulate in one scan.** No dedicated single-pass aggregator exists in the repo, but the span-scan loop shape is established: `Actor.ExtractPosition` (`Actor.cs:134-168`) and `CombatLogLine.GetSections` (`CombatLogLine.cs:71-87`) both do a single `for`/`foreach` accumulating state per element. Copy that "one loop, accumulate locals, no intermediate `List`/LINQ" shape. The min/max idiom itself (`if (ts < min) min = ts; if (ts > max) max = ts;`) has no existing instance — it is a standard minimal pattern.

---

### `SwtorLogParser.Native.Cli/Program.cs` — `Update` (PERF-02)

**EXACT current code that changes** (`Program.cs:36-50`):

```csharp
private static void Update(SlidingExpirationList list, CombatLogsMonitor.PlayerStats playerStats)
{
    list.AddOrUpdate(playerStats);

    Console.Clear();                    // line 40 — full-screen repaint = flicker. REPLACE.
    Console.SetCursorPosition(0, 1);

    foreach (var item in list.Items)
        Console.WriteLine("{0}: DPS: {1} ({2}%); HPS: {3} ({4}%)",
            item.Player.Name!,
            item.DPS.HasValue ? item.DPS.Value.ToString("N") : "-",
            item.DPSCritP.HasValue ? item.DPSCritP.Value.ToString("N") : "-",
            item.HPS.HasValue ? item.HPS.Value.ToString("N") : "-",
            item.HPSCritP.HasValue ? item.HPSCritP.Value.ToString("N") : "-");
}
```

**Analog — in-place overwrite, SAME FILE** (`OnCombatLogAdded`, `Program.cs:52-58`). This is the exact pattern PERF-02 should generalize to the stats rows: reposition cursor, pad to clear stale chars, rewrite:

```csharp
private static void OnCombatLogAdded(object? _, CombatLog combatLog)
{
    Console.SetCursorPosition(0, 0);
    Console.Write(new string(' ', Console.WindowWidth - 1));   // pad-clear the row
    Console.SetCursorPosition(0, 0);
    Console.Write(combatLog.FileInfo);
}
```

Note this header writes row 0; the stats block must start at row 1 (the existing `SetCursorPosition(0, 1)` already reserves row 0 for the filename header — preserve that).

**Canonical minimal example (no whole-screen analog exists — supply this):**

```csharp
private static void Update(SlidingExpirationList list, CombatLogsMonitor.PlayerStats playerStats)
{
    list.AddOrUpdate(playerStats);

    var row = 1;                                  // row 0 is the filename header (OnCombatLogAdded)
    var width = Console.WindowWidth - 1;
    foreach (var item in list.Items)
    {
        Console.SetCursorPosition(0, row);
        var text = string.Format("{0}: DPS: {1} ({2}%); HPS: {3} ({4}%)",
            item.Player.Name!,
            item.DPS.HasValue ? item.DPS.Value.ToString("N") : "-",
            item.DPSCritP.HasValue ? item.DPSCritP.Value.ToString("N") : "-",
            item.HPS.HasValue ? item.HPS.Value.ToString("N") : "-",
            item.HPSCritP.HasValue ? item.HPSCritP.Value.ToString("N") : "-");
        Console.Write(text.Length < width ? text.PadRight(width) : text);   // pad-clear trailing chars from a previously longer row
        row++;
    }
}
```

Key points: `SetCursorPosition` per row, `PadRight(WindowWidth-1)` to erase a previously longer line (same intent as the `new string(' ', ...)` clear in `OnCombatLogAdded`), no `Console.Clear()`. The visible text format string is IDENTICAL — only the repaint mechanism changes. (If `list.Items` can shrink between updates, blank any rows below the new count; with the 30s `SlidingExpirationList` this is an edge case — pad/clear trailing rows if cheap.)

**Contrast only — managed CLI is OUT OF SCOPE.** `SwtorLogParser.Cli/Program.cs` uses `System.CommandLine.Rendering` (`ConsoleRenderer` + `TableView.Render(renderer, Region)`, `Cli/Program.cs:3-4,15,26,54`). It does its own region-based diff rendering and is replaced in Phase 5. Do NOT touch it; do not port the Native pattern to it.

---

## Shared Patterns

### Zero-copy `ReadOnlyMemory<char>` slicing
**Source:** `CombatLogLine.cs:61-90` (`GetSections`), `Actor.cs:48-54/99/106/117`
**Apply to:** PERF-01 `GetLogLines`
**Rule:** slice the single backing buffer via `.Slice(start, length)` / `.AsMemory(start, length)`; never `.ToArray()` a per-element copy.

### Single span-scan accumulation (no intermediate LINQ collections)
**Source:** `Actor.cs:134-168` (`ExtractPosition`), `CombatLogLine.cs:71-87`
**Apply to:** PERF-03 single-pass aggregation
**Rule:** one loop, accumulate locals, compute results after.

### In-place console row overwrite
**Source:** `Native.Cli/Program.cs:52-58` (`OnCombatLogAdded`)
**Apply to:** PERF-02 `Update`
**Rule:** `SetCursorPosition` + pad-to-width write; no `Console.Clear()`.

### Null-on-zero/infinity parse/compute policy
**Source:** existing `CalculateDpsHpsStats:107-108`; mirrors the project-wide "null over throw/zero" convention (CONVENTIONS.md Error Handling)
**Apply to:** PERF-03 — preserve the exact `IsInfinity || == 0.0d ? null` branches.

---

## No Analog Found

| File / Concern | Role | Data Flow | Reason | Mitigation |
|----------------|------|-----------|--------|------------|
| `Native.Cli/Program.cs` whole-screen in-place render | view | event-driven | Repo has no multi-row in-place console renderer (only single-row `OnCombatLogAdded`; managed CLI uses System.CommandLine.Rendering, out of scope) | Canonical minimal example supplied above |
| `CalculateDpsHpsStats` min/max timestamp tracking | service | transform | No `min`/`max` running-tracker exists anywhere in repo | Standard `if (ts < min) min = ts;` idiom; span-scan shape from `ExtractPosition` |

---

## DO-NOT-BREAK List

1. **Identical DPS/HPS/crit% output.** TEST-02 `DpsHpsMathTests` (7 facts) is the contract: `Dps_Computed_From_Known_Damage` (3000), `Hps_Computed_From_Known_Heals` (2000), `Crit_Percent_Computed` (50, HPSCritP null), `Zero_Crit_Maps_To_Null`, `Window_Expiry_Removes_Old_Lines`, `Window_Keeps_Recent_Lines`. All 102 tests must stay green every commit.
2. **crit denominator = `state.Count`** (not damage/heal count). Null-maps zero AND infinity.
3. **`timeSpan`:** `Count > 1 ? (max - min) : TimeSpan.FromSeconds(1)` on the `.TimeStamp.TimeOfDay` key (same key as the dropped `OrderBy`).
4. **`Accumulator` byte-identical:** keep `static readonly object Lock`, the 10s `RemoveWhere(line => line.TimeStamp < combatLog.TimeStamp.AddSeconds(-10))`, and `Add`.
5. **Do NOT touch the `DateTime.Now` window filter** (`ConfigureObservables:54`, IN-01 — separate item, deferred).
6. **Zero-copy intent:** PERF-01 slices must reference the `ReadToEnd()` heap string for the lifetime of the returned list (safe — string is reachable via Roms). No `.ToArray()` per line. Do not dispose/reuse a buffer the slices point into.
7. **AOT core lib:** `SwtorLogParser` stays `<IsAotCompatible>true</IsAotCompatible>` — no reflection-heavy patterns in PERF-01/PERF-03.
8. **Native CLI rendered text format unchanged:** same format string, same `"N"` formatting, same `"-"` for null; only `Console.Clear()` → cursor-reposition changes. Row 0 stays the filename header.
9. **Managed CLI (`SwtorLogParser.Cli`) untouched** — replaced in Phase 5.

## Metadata

**Analog search scope:** `SwtorLogParser/Model/`, `SwtorLogParser/Monitor/`, `SwtorLogParser/View/`, `SwtorLogParser.Native.Cli/`, `SwtorLogParser.Cli/`, `SwtorLogParser.Tests/`
**Files scanned:** ~12
**Pattern extraction date:** 2026-06-11
