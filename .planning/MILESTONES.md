# Milestones

## v1.0 Hardening (Shipped: 2026-06-12)

**Phases completed:** 7 phases, 17 plans, 32 tasks

**Key accomplishments:**

- Characterization tests locking the two EAGER-throwing parse factories (GameObject non-numeric id, CombatLogLine non-parseable timestamp) plus delimiter/brace-edge matrices and golden-line regression locks — suite GREEN with zero skips.
- Ability LAZY inherited-Id throw characterized (Parse non-null, .Id throws FormatException) and Action graceful-null on malformed inner fragments locked via [Theory] — completing seven-model edge-case + golden coverage with zero production change.
- GameObject/Ability non-numeric ids now skip (null) instead of crashing the reader (BUG-05), and the two shared static parse caches are ConcurrentDictionary with first-writer-wins TryAdd (BUG-06) — no Dictionary.Add race.
- Locale-stable timestamp gate (TryParseExact/InvariantCulture), null-safe monitor Start/Stop with linked-token cancellation, guarded static-ctor filename split, and least-privilege read-only log opens — all five remaining correctness bugs landed with the suite green at 72/0/0.
- Unconditional NullLogger-backed `CombatLogsMonitor.Instance` + public DI constructor, plus Start/Stop lifecycle and Rx-delivery tests through a new internal push seam — DpsHps pipeline semantics unchanged.
- 1. [Rule 1 - Bug] Recharacterized `Game_Objects_Equality_Reflects_Backing_Memory` test
- Deterministic DPS/HPS/crit% and 10s sliding-window expiry tests calling the now-internal Accumulator + CalculateDpsHpsStats directly, bypassing the DateTime.Now pipeline filter — visibility-only production change, zero behavior change.
- Injectable `ICombatLogSource` seam (Directory.Exists-guarded) behind the static `CombatLogs` facade, making the two deferred Phase 1 tests `All_Logs_Are_Not_Null` and `Player_Is_Local_Is_True` hermetic and CI-safe — no real SWTOR folders, no TypeInitializationException.
- Replaced the per-line `char[]` allocation and parse-via-count in `CombatLog.cs` with a single offset-tracking line splitter that feeds zero-copy `string.AsMemory(start,len)` slices to `GetLogLines()` and a parse-free non-empty-line count to `ToString()`, with CRLF parity locked by Wave-0 tests.
- 1. [Rule 3 - Blocking] FormatRow helper typed to `PlayerStats`, not `Entry`
- Introduced a root Directory.Packages.props (CPM) pinning all seven managed NuGet packages to GA versions, stripped every Version= attribute across the solution, removed three dead core-lib refs, and added explicit Logging.Abstractions — solution restores/builds and all 106 tests stay green.
- Removed the last preview/alpha/beta dependencies (System.CommandLine beta + System.CommandLine.Rendering alpha) from both CLI hosts, replaced the RootCommand setup with hand-rolled switch(args[0]) dispatch, re-wired Ctrl+C through Console.CancelKeyPress -> CancellationTokenSource, ported the managed CLI's 5-column live table to Spectre.Console, kept the Native AOT host's PERF-02 renderer untouched and Spectre-free, and dropped DockerDefaultTargetOS from both csproj — solution builds and all 106 tests stay green.
- Mechanical net8.0 -> net10.0 LTS upgrade across all 5 projects with Logging.Abstractions bumped to 10.0.9 GA; restore + Release build green, 106/106 tests pass with zero skips, and Native AOT code-gen is IL2xxx/IL3xxx-clean — behavior preserved, no code changes.

---
