# Phase 4: Performance - Context

**Gathered:** 2026-06-11
**Status:** Ready for planning

<domain>
## Phase Boundary

Make the live stats pipeline avoid wasteful full-file re-parses, per-line heap allocations, and full-window re-sorts, and make the Native CLI render without full-screen flicker. Requirements: PERF-01, PERF-02, PERF-03.

**Critical invariant:** Output must be IDENTICAL after optimization. The DPS/HPS/crit%/sliding-window numbers are locked by the Phase 3 TEST-02 `DpsHpsMathTests`; parser results are locked by the Phase 1/2 characterization tests. `dotnet test` must stay green (102 tests) every commit. These are pure performance refactors — zero behavior change.

**In scope:** `SwtorLogParser/Monitor/CombatLog.cs` (PERF-01), `SwtorLogParser.Native.Cli/Program.cs` (PERF-02), `SwtorLogParser/Monitor/CombatLogsMonitor.cs` `CalculateDpsHpsStats`/`Accumulator` (PERF-03).

**Out of scope:** The `DateTime.Now` window filter (a known correctness/testability item, IN-01 — NOT a perf requirement; leave it). The managed CLI renderer (System.CommandLine.Rendering is replaced in Phase 5). Dependency upgrades (Phase 5), CI (Phase 6).

</domain>

<decisions>
## Implementation Decisions

### PERF-01 — CombatLog read (CombatLog.cs)
- `ToString()` reports the line count WITHOUT building `CombatLogLine` objects — count lines (newlines / `EnumerateLines`) only, no parse.
- Eliminate the per-line `char[]` allocation: `GetLogLines()` currently does `new ReadOnlyMemory<char>(line.ToArray())` per line. Instead slice the original string via `string.AsMemory().Slice(start, length)` so each `CombatLogLine` references a window into the single backing string — true zero-copy, preserving the `ReadOnlyMemory<char>` intent end-to-end.
- Keep `ReadToEnd()` for the batch `GetLogLines()` path (then slice into the returned string); the live reader (`ReadAsync`) is already line-by-line and is not the PERF-01 target.

### PERF-02 — Native CLI render (SwtorLogParser.Native.Cli/Program.cs)
- Replace `Console.Clear()` + full redraw per event with cursor repositioning: `Console.SetCursorPosition(0,0)` and overwrite rows in place, padding each line to clear any trailing characters from a previously longer row — eliminating the full-screen repaint flicker.
- Scope is the Native CLI ONLY. The managed CLI uses `System.CommandLine.Rendering` (replaced in Phase 5); do not touch it here.

### PERF-03 — Accumulator / CalculateDpsHpsStats (CombatLogsMonitor.cs)
- Avoid the full re-sort/re-scan per line: drop the `OrderBy(x => x.TimeStamp.TimeOfDay)` (it is only used to get first/last timestamps — track min/max directly) and collapse the multiple `Where`/`Sum`/`Count` passes over the window into a SINGLE pass in `CalculateDpsHpsStats`.
- OUTPUT MUST BE IDENTICAL: DPS, HPS, crit%, and the null-on-zero/infinity behavior must match exactly — the TEST-02 `DpsHpsMathTests` are the contract. If any optimization would change a number, do not make it.
- Keep the 10s sliding-window semantics exactly (the `Accumulator` `RemoveWhere` at `combatLog.TimeStamp.AddSeconds(-10)` stays). Do NOT touch the `DateTime.Now` window filter (IN-01, separate).

### Claude's Discretion
- Whether PERF-03 keeps incremental running totals in the `Accumulator` vs a single-pass `CalculateDpsHpsStats`, the exact slicing helper for PERF-01, and the precise in-place render layout for PERF-02 are at Claude's discretion — guided by identical output and green tests. Add a micro-benchmark or before/after note only if cheap; not required.

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets
- `CombatLog.GetLogLines()` (`CombatLog.cs:20-40`): `reader.ReadToEnd().AsSpan()` → `EnumerateLines()` → `CombatLogLine.Parse(new ReadOnlyMemory<char>(line.ToArray()))`. The `.ToArray()` is the per-line alloc; `ToString()` calls `GetLogLines().Count` (full parse just to count).
- `CalculateDpsHpsStats` + `Accumulator` are now `internal` (Phase 3) and covered by `DpsHpsMathTests` — safe to optimize behind those tests.
- Native CLI `Program.cs` `Update` does `Console.Clear()` then re-renders the `SlidingExpirationList` rows.

### Established Patterns
- Zero-allocation span/memory parsing is the project's core design value — PERF-01 restores it where `.ToArray()` broke it.
- Core lib stays `IsAotCompatible=true`.
- `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` (102 tests) + `dotnet build SwtorLogParser.slnx`.

### Integration Points
- `CombatLogLine.Parse(ReadOnlyMemory<char>)` is the consumer of PERF-01's slices — slices must stay valid (the backing string lives as long as the returned `CombatLogLine` list references it; ensure no use-after-free of a disposed buffer — `ReadToEnd` returns a heap string, safe to slice).
- `DpsHps` observers (all 3 hosts) consume `PlayerStats` from `CalculateDpsHpsStats` — output identity is mandatory.

</code_context>

<specifics>
## Specific Ideas

- PERF-01 backing-string safety: `ReadToEnd()` yields a `string` on the GC heap; slicing it via `AsMemory()` is safe for the lifetime of the returned `List<CombatLogLine>` (the string is reachable through the memory slices). This is the correct zero-copy approach (the old `.ToArray()` defeated it).
- PERF-03: the `OrderBy` result is consumed only as `items[0]` / `items[^1]` for `timeSpan` and `state.ElementAt(0)` for `Player` — these can come from tracked min/max + any element, avoiding the sort. Verify against `DpsHpsMathTests` (esp. the window-expiry and multi-line cases).
- The `Accumulator` runs under a `static readonly object Lock` — keep the locking; only reduce per-call work.

</specifics>

<deferred>
## Deferred Ideas

- `DateTime.Now` window filter → testability/correctness item (IN-01), not a Phase 4 perf requirement.
- BoundedCache soft-cap-under-concurrency hardening (WR-01) — accepted as-is.
- Dependency GA upgrades (Phase 5), CI (Phase 6).

</deferred>
