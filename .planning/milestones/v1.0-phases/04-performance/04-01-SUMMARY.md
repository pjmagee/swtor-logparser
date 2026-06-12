---
phase: 04-performance
plan: 01
subsystem: performance
tags: [csharp, dotnet8, zero-copy, span, readonlymemory, file-io, xunit]

# Dependency graph
requires:
  - phase: 02-correctness
    provides: BUG-07 locked file-open mode (FileAccess.Read, FileShare.ReadWrite, Latin1) and CombatLogLine.Parse null-on-invalid contract
provides:
  - "Zero-copy line slicing in CombatLog.GetLogLines() via string.AsMemory(start,len) into the ReadToEnd() backing string (no per-line char[])"
  - "Parse-free line count in CombatLog.ToString() (counts non-empty spans, no CombatLogLine construction)"
  - "Single offset-tracking EnumerateLineSpans splitter as the one source of truth for both count and slices, with CRLF/LF parity (terminator excluded)"
  - "CombatLogReadTests.cs Wave-0 read/slice + EnumerateLines parity + ToString count tests"
affects: [04-02-native-cli-render, 04-03-accumulator]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Offset-tracking line splitter (yield (start,length)) reproducing MemoryExtensions.EnumerateLines, shared by count and slice paths"
    - "Zero-copy ReadOnlyMemory<char> windows into a single GC-rooted ReadToEnd() backing string"

key-files:
  created:
    - SwtorLogParser.Tests/CombatLogReadTests.cs
  modified:
    - SwtorLogParser/Monitor/CombatLog.cs

key-decisions:
  - "ToString() count semantics locked to 'non-empty lines' (diagnostic-only; no test pins the exact integer per 04-RESEARCH A1)"
  - "Splitter handles \\r\\n (one break), bare \\r, bare \\n — the terminators these logs use — rather than the full Unicode EnumerateLines set, validated by a parity test"
  - "Extracted a private ReadAllText() helper so ToString() and GetLogLines() share one file-read path (no divergent second read path)"

patterns-established:
  - "Single offset-tracking splitter as one source of truth for count + zero-copy slices"
  - "Slice the heap string from ReadToEnd() (GC-rooted via the returned list), never a stackalloc/pooled buffer"

requirements-completed: [PERF-01]

# Metrics
duration: 9min
completed: 2026-06-12
---

# Phase 4 Plan 01: PERF-01 Zero-Copy CombatLog Read Summary

**Replaced the per-line `char[]` allocation and parse-via-count in `CombatLog.cs` with a single offset-tracking line splitter that feeds zero-copy `string.AsMemory(start,len)` slices to `GetLogLines()` and a parse-free non-empty-line count to `ToString()`, with CRLF parity locked by Wave-0 tests.**

## Performance

- **Duration:** ~9 min
- **Started:** 2026-06-12 (Task 1 test-first)
- **Completed:** 2026-06-12
- **Tasks:** 2
- **Files modified:** 2 (1 created, 1 modified)

## Accomplishments
- `GetLogLines()` now slices a single `ReadToEnd()` backing string via `text.AsMemory(start, length)` — eliminates one `char[]` plus one `ReadOnlyMemory<char>` wrapper allocation per line, restoring the project's zero-copy span/memory design.
- `ToString()` reports the line count by counting non-empty spans from the shared splitter — no `CombatLogLine.Parse`, no `GetLogLines()` call.
- Introduced one private `EnumerateLineSpans` splitter (the single source of truth) reproducing `EnumerateLines` semantics: `\r\n` is one break, bare `\r`/`\n` each break, terminator excluded (no trailing `\r` in any slice).
- Test-first: 3 Wave-0 tests written and committed GREEN against the unrefactored code before the refactor, locking the parsed-set, CRLF-no-trailing-`\r`, and count behavior.

## Task Commits

Each task was committed atomically:

1. **Task 1: Wave-0 read/slice + EnumerateLines parity tests** - `c17fcd7` (test)
2. **Task 2: Zero-copy slicing + parse-free count in CombatLog.cs** - `226570f` (perf)

## Files Created/Modified
- `SwtorLogParser.Tests/CombatLogReadTests.cs` - Created. Three facts: `GetLogLines_Yields_Same_Parsed_Lines` (mixed CRLF/LF + blank + malformed fixture), `Splitter_Matches_EnumerateLines` (CRLF no-trailing-`\r` parity, cross-checked against `EnumerateLines`), `ToString_Reports_Line_Count` (non-empty count shape).
- `SwtorLogParser/Monitor/CombatLog.cs` - Modified. Added `EnumerateLineSpans` splitter + `ReadAllText()` helper; `GetLogLines()` slices via `AsMemory`; `ToString()` counts non-empty spans without parsing. File open mode unchanged (BUG-07 locked: `FileAccess.Read`, `FileShare.ReadWrite`, `Encoding.Latin1`).

## Verification
- `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj --nologo`: **105 passed, 0 failed, 0 skipped** (102 baseline + 3 Wave-0), after every commit.
- `dotnet build SwtorLogParser.slnx -c Debug`: **Build succeeded, 0 errors** (1 pre-existing, out-of-scope `CS0108` warning in `ParserForm.cs` — not touched by this plan).
- Grep guards on `CombatLog.cs`: no `line.ToArray()`, no per-line `new ReadOnlyMemory<char>(` copy; `GetLogLines()` uses `text.AsMemory(...)`; `ToString()` does not call `GetLogLines()`/`Parse`.

## Decisions Made
- **ToString() count semantics = non-empty lines:** Per 04-RESEARCH Pitfall 1 / Assumption A1, the exact integer is diagnostic-only and not test-pinned. The non-empty-line count is computed without parsing. (For the fixtures used, this equals the old `GetLogLines().Count`; it may differ only for files containing malformed-but-non-empty lines — accepted, unobserved.)
- **Splitter terminator set:** Implemented `\r\n` / `\r` / `\n` (the terminators these logs use) rather than the full Unicode `EnumerateLines` set (`\v`, `\f`, NEL, LS, PS). Parity is validated by `Splitter_Matches_EnumerateLines`. This matches A3 and keeps the hot loop tight.
- **Shared `ReadAllText()` helper:** Extracted so `ToString()` and `GetLogLines()` use one file-read path, avoiding a divergent second read (per the plan's "do not introduce a second file-read path" guidance).

## Deviations from Plan

None - plan executed exactly as written. The two-task test-first sequence was followed verbatim; all Wave-0 tests passed against the current code before the refactor, and the refactor kept the suite green.

## Issues Encountered
None. (Git emits an expected `LF will be replaced by CRLF` warning on commit due to the repo's autocrlf setting — cosmetic, no impact on content.)

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- PERF-01 complete; `CombatLog.cs` is the only file touched, leaving `CombatLogsMonitor.cs` (04-03) and `Native.Cli/Program.cs` (04-02) untouched as required.
- Core library stays `IsAotCompatible=true` — no reflection added.
- Ready for 04-02 (Native CLI render) and 04-03 (accumulator single-pass).

## Self-Check: PASSED

---
*Phase: 04-performance*
*Completed: 2026-06-12*
