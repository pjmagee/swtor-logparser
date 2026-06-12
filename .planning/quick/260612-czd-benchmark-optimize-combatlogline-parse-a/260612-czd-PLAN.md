---
phase: quick-260612-czd
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - SwtorLogParser.Benchmarks/SwtorLogParser.Benchmarks.csproj
  - SwtorLogParser.Benchmarks/Program.cs
  - SwtorLogParser.Benchmarks/CombatLogLineParseBenchmarks.cs
  - SwtorLogParser.Benchmarks/Fixtures/sample-combat.log
  - SwtorLogParser.slnx
  - Directory.Packages.props
  - SwtorLogParser/Caching/BoundedCache.cs
  - SwtorLogParser/Model/GameObject.cs
  - SwtorLogParser/Model/Ability.cs
  - SwtorLogParser/Model/Action.cs
  - SwtorLogParser/Model/CombatLogLine.cs
  - SwtorLogParser.Tests/ParseCacheTests.cs
autonomous: true
requirements: [PERF-BENCH-01, PERF-CACHE-01, PERF-LAZY-01]

must_haves:
  truths:
    - "A BenchmarkDotNet [MemoryDiagnoser] harness builds and runs against CombatLogLine.Parse, reporting ns/op, bytes/op, and Gen0."
    - "Cache-hit Parse path (GameObject/Ability/Action) allocates no string for the cache key — rom.ToString() runs only on a miss/insert."
    - "Source/Target/Ability/Action/Value/Threat on CombatLogLine are parsed lazily; a dropped (filtered-out) line never pays their parse cost."
    - "All 106 existing tests in SwtorLogParser.Tests still pass — parser model and DPS/HPS stream behavior is byte-identical."
    - "BEFORE and AFTER allocation/timing numbers are recorded in SUMMARY.md."
  artifacts:
    - path: "SwtorLogParser.Benchmarks/CombatLogLineParseBenchmarks.cs"
      provides: "MemoryDiagnoser benchmark class parsing the sample fixture line-by-line"
      contains: "[MemoryDiagnoser]"
    - path: "SwtorLogParser.Benchmarks/Fixtures/sample-combat.log"
      provides: "Sanitized representative combat-log fixture (placeholder player handle)"
      min_lines: 200
    - path: "SwtorLogParser/Caching/BoundedCache.cs"
      provides: "Span-keyed alternate-lookup TryGetValue + GetOrAdd(ReadOnlySpan<char>)"
      contains: "GetAlternateLookup"
  key_links:
    - from: "SwtorLogParser/Model/GameObject.cs"
      to: "BoundedCache span lookup"
      via: "TryGetValue(rom.Span, out cached) before any rom.ToString()"
      pattern: "TryGetValue\\(rom\\.Span"
    - from: "SwtorLogParser/Model/CombatLogLine.cs"
      to: "lazy Actor/Value/Threat parse"
      via: "null-coalescing backing fields / parsed flags"
      pattern: "_source|_sourceParsed"
---

<objective>
Stand up a BenchmarkDotNet harness that measures `CombatLogLine.Parse` allocations, capture a BEFORE baseline, then apply two locked allocation optimizations and re-measure to prove the improvement — with zero behavior change to the parser model or the DPS/HPS stream.

Purpose: The parser runs on a hot path tailing high-volume combat logs. Two grounded allocation sources were identified by prior code review: (1) every cache lookup materializes a string key via `rom.ToString()` BEFORE the lookup, costing ~4 string allocs per damage line even on cache HITS; (2) `CombatLogLine`'s constructor eagerly parses all six sub-objects, but the Rx pipeline drops most lines (stale / NPC-vs-NPC) after only checking TimeStamp + Source.Name — paying full parse cost then discarding.

Output:
- New `SwtorLogParser.Benchmarks` console project (net10.0) with a committed sanitized fixture and a `[MemoryDiagnoser]` benchmark.
- Span-keyed cache lookup in `BoundedCache<TValue>` consumed by the three `Parse` hot paths.
- Lazy sub-parsing in `CombatLogLine`.
- BEFORE/AFTER numbers recorded in SUMMARY.md.
</objective>

<execution_context>
@D:/Projects/pjmagee/swtor-logparser/.claude/gsd-core/workflows/execute-plan.md
@D:/Projects/pjmagee/swtor-logparser/.claude/gsd-core/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@CLAUDE.md

# Hot-path source under optimization (read before editing)
@SwtorLogParser/Model/CombatLogLine.cs
@SwtorLogParser/Caching/BoundedCache.cs
@SwtorLogParser/Model/GameObject.cs
@SwtorLogParser/Model/Ability.cs
@SwtorLogParser/Model/Action.cs
@SwtorLogParser/Monitor/CombatLogs.cs
@SwtorLogParser/Monitor/CombatLogsMonitor.cs

# Build / packaging context
@Directory.Packages.props
@SwtorLogParser.slnx
@SwtorLogParser.Tests/ParseCacheTests.cs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Benchmark project + sanitized fixture + BEFORE baseline</name>
  <files>
    SwtorLogParser.Benchmarks/SwtorLogParser.Benchmarks.csproj,
    SwtorLogParser.Benchmarks/Program.cs,
    SwtorLogParser.Benchmarks/CombatLogLineParseBenchmarks.cs,
    SwtorLogParser.Benchmarks/Fixtures/sample-combat.log,
    Directory.Packages.props,
    SwtorLogParser.slnx
  </files>
  <action>
    Create a new console project `SwtorLogParser.Benchmarks` targeting net10.0 (match the repo: ImplicitUsings enable, Nullable enable, OutputType Exe). It references `..\SwtorLogParser\SwtorLogParser.csproj` and adds a `BenchmarkDotNet` PackageReference WITHOUT a Version attribute (CPM is in use). Add a `BenchmarkDotNet` PackageVersion to Directory.Packages.props — pin a current stable BenchmarkDotNet (resolve the latest stable via the package source; do not add preview). Do NOT set PublishAot on this project (benchmarks run JIT). Per PERF-BENCH-01.

    The benchmark must NOT build in Debug-by-default surprise: BenchmarkDotNet warns/refuses to produce trustworthy numbers under Debug, so the executor MUST run it with `-c Release`.

    Embed the fixture: add the .log file as `<Content Include="Fixtures\sample-combat.log"><CopyToOutputDirectory>PreserveNewer</CopyToOutputDirectory></Content>` so it lands next to the executable. Also confirm `.gitignore` already ignores `BenchmarkDotNet.Artifacts/` (it does, line 58) — do not regress that.

    Create the sanitized fixture `Fixtures/sample-combat.log` with a few thousand representative lines (target 2000–4000). Build it by REPEATING a small set of real-shaped line templates with varying numeric values so the file is large enough for stable timing but contains NO real player handle. Use a consistent placeholder player: `@Player#000000000000000`. Preserve the exact real structure `[time] [source] [target] [ability] [action] (value) <threat>` and include a representative MIX:
      - Player ApplyEffect Damage lines (the IsPlayerDamage hot path) — e.g. `[20:33:17.759] [@Player#000000000000000|(422.51,620.88,33.46,84.27)|(300469/379924)] [=] [Progressive Scan {3394132265402368}] [ApplyEffect {836045448945477}: Damage {836045448945501}] (8622) <3880>`
      - Player ApplyEffect Heal lines — same source shape, `Heal {836045448945500}`, with `(NNNN) <NNNN>`.
      - NPC-vs-NPC / AreaEntered lines that the Rx pipeline DROPS — e.g. `[18:12:13] [Yozusk Mauler {3158140992356352}:5577004295094|(4641.05,4529.71,694.02,-124.45)|(1/401177)] [=] [] [AreaEntered {836045448953664}: Imperial Fleet {137438989504}]`
    Vary timestamps across a spread (so not every line is the same TimeStamp) and vary the (value)/<threat> numbers per line. Reuse the line shapes already proven valid in `SwtorLogParser.Tests/CombatLogLineTests.cs` and `CombatLogLineParseBenchmarks` must successfully `CombatLogLine.Parse` a high fraction of them.

    Create `CombatLogLineParseBenchmarks.cs`: a `[MemoryDiagnoser]` class. In a `[GlobalSetup]` load every line of `Fixtures/sample-combat.log` into a `string[]` (or `ReadOnlyMemory<char>[]`) field. Provide at least two `[Benchmark]` methods:
      1. `ParseAllLines` — iterate the fixture once, calling `CombatLogLine.Parse(line.AsMemory())` and consuming a property (e.g. accumulate a hash or count of non-null results) so the JIT cannot elide the work. This is the cold/representative parse.
      2. `ParseAllLines_HotCache` — call `ParseAllLines`'s work twice (or run a warm-up parse pass in GlobalSetup then benchmark a repeat pass) so the cache is fully populated and the run exercises the cache-HIT path specifically — this is the benchmark that exposes the `rom.ToString()` key-allocation cost Optimization 1 removes.
    Mark the benchmark consumer of parsed objects so it touches `Source`, `Action`, and `Value` (this keeps the BEFORE numbers honest once Optimization 2 makes those lazy — the AFTER ParseAllLines benchmark that touches the properties must still allocate equivalently; the DROPPED-line win shows up via a third optional benchmark or via the overall ns/op once stale lines are NOT touched). Keep it simple: ParseAllLines touches no sub-properties (measures pure Parse + drop savings); add `ParseAllLines_TouchAll` that also reads Source/Action/Value to measure the full-parse cost. Three benchmarks total: pure-parse, touch-all, hot-cache.

    `Program.cs`: `BenchmarkRunner.Run<CombatLogLineParseBenchmarks>();` (or `BenchmarkSwitcher` over the assembly).

    Register the project in `SwtorLogParser.slnx` by adding `<Project Path="SwtorLogParser.Benchmarks/SwtorLogParser.Benchmarks.csproj" />` alongside the existing `<Project>` entries.

    CAPTURE BEFORE BASELINE: this task runs on UNMODIFIED parser code. After the project builds, run the benchmark in Release and record the summary table (ns/op, Allocated bytes/op, Gen0) for all three benchmarks. These are the BEFORE numbers — preserve them verbatim for SUMMARY.md. Do NOT edit BoundedCache / CombatLogLine in this task.
  </action>
  <verify>
    <automated>cd D:/Projects/pjmagee/swtor-logparser && dotnet build SwtorLogParser.Benchmarks/SwtorLogParser.Benchmarks.csproj -c Release</automated>
    <automated>cd D:/Projects/pjmagee/swtor-logparser && dotnet run --project SwtorLogParser.Benchmarks/SwtorLogParser.Benchmarks.csproj -c Release -- --filter "*"</automated>
  </verify>
  <done>
    Benchmark project builds in Release, is registered in the .slnx, BenchmarkDotNet version is centrally managed, the sanitized fixture (no real handle; consistent `@Player#000000000000000`) is committed and copied to output, the benchmark runs and emits a MemoryDiagnoser table, and the BEFORE numbers (3 benchmarks: pure-parse, touch-all, hot-cache) are captured for SUMMARY.md. No parser source files were modified.
  </done>
</task>

<task type="auto" tdd="true">
  <name>Task 2: Span-keyed cache lookup (kill ~4 string allocs/line on cache HIT)</name>
  <files>
    SwtorLogParser/Caching/BoundedCache.cs,
    SwtorLogParser/Model/GameObject.cs,
    SwtorLogParser/Model/Ability.cs,
    SwtorLogParser/Model/Action.cs,
    SwtorLogParser.Tests/ParseCacheTests.cs
  </files>
  <behavior>
    - Span lookup on HIT returns the SAME instance as the string lookup for identical content (Assert.Same).
    - A span-lookup HIT does NOT grow the cache (Count unchanged before/after).
    - On a MISS, the string key is materialized and inserted exactly once; FIFO eviction (cap) still bounds Count.
    - Lost-add-race semantics preserved: concurrent GetOrAdd on identical content converges on one instance.
  </behavior>
  <action>
    In `BoundedCache<TValue>` (PERF-CACHE-01): the backing `ConcurrentDictionary<string, TValue> _map` MUST be constructed with an ordinal comparer that supports span alternate lookup — change `new()` to `new(StringComparer.Ordinal)` (StringComparer.Ordinal implements IAlternateEqualityComparer<ReadOnlySpan<char>, string> on net9+/net10, satisfying GetAlternateLookup). In the ctor, build and store `_altLookup = _map.GetAlternateLookup<ReadOnlySpan<char>>()`.

    Add `public bool TryGetValue(ReadOnlySpan<char> key, out TValue value)` that delegates to `_altLookup.TryGetValue(key, out value!)` — ZERO string allocation on a hit.

    Add a span-keyed insert path. Add `public TValue GetOrAdd(ReadOnlySpan<char> key, Func<TValue> valueFactory)` (or `Func<string, TValue>`): first try `_altLookup.TryGetValue(key, out existing)` and return existing on hit; on miss materialize `var k = key.ToString();`, create `value = valueFactory();`, then run the EXACT existing GetOrAdd(string, value) insert/eviction body (TryAdd, Enqueue k onto `_order`, evict while Count > capacity, and on lost race return the cached winner). Materialize the string ONLY here on the miss path. Preserve the existing `GetOrAdd(string, TValue)` overload (other call sites / Action's static initializers may keep using it) — DO NOT delete it; the FIFO `_order` and lost-race return path must stay byte-identical.

    AOT note: GetAlternateLookup is AOT-safe and reflection-free — the core library stays IsAotCompatible=true. No reflection, no new package.

    Rewire the three hot paths so the string is NEVER built on a HIT:
      - `GameObject.Parse(rom)`: replace `var key = rom.ToString(); if (cache.TryGetValue(key, out cached)) return cached;` with `if (CombatLogs.GameObjectCache.TryGetValue(rom.Span, out var cached)) return cached;` FIRST. Only on miss construct `new GameObject(rom)`, run the existing `Id == null -> return null` guard, then insert via the span GetOrAdd (factory returns the constructed object) OR materialize the key once and call the string GetOrAdd. Keep the race-winner return semantics.
      - `Ability.Parse(rom)`: same pattern against `CombatLogs.AbilityCache`, preserving the `rom.Length == 0 || rom.IsEmpty -> null` guard first.
      - `Action.Parse(rom)`: same pattern against `CombatLogs.ActionCache`. Keep the `IndexOf(':') != -1` guard and the try/catch around `new Action(rom)` (Action's ctor calls GameObject.Parse twice and can throw). On hit, return cached with no ToString. Note: Action.ApplyEffectDamage/Heal/AbilityActivate static initializers call `Parse(string.AsMemory())` — they must still work unchanged.

    CAUTION — eviction correctness: only enqueue the materialized string onto `_order` on an ACTUAL insert (inside the existing TryAdd-success branch). Never enqueue on a hit. Do not change capacity (4096) or eviction order.

    Add focused tests to `ParseCacheTests.cs` (PERF-CACHE-01):
      - `Span_Lookup_Returns_Same_Instance_As_String_Lookup`: parse a unique content once (warms cache), then assert a fresh `BoundedCache.TryGetValue(content.AsSpan(), out v)` returns the SAME instance as the string overload, and that Count did not increase between the two lookups.
      - `Span_Lookup_Hit_Does_Not_Grow_Cache`: record Count, do N span-lookup hits on already-cached content, assert Count unchanged.
    Use UNIQUE literal contents (the static caches persist across tests — follow the existing ParseCacheTests convention of distinctive `Zqx...` literals).
  </action>
  <verify>
    <automated>cd D:/Projects/pjmagee/swtor-logparser && dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj -c Release --filter "FullyQualifiedName~ParseCacheTests"</automated>
    <automated>cd D:/Projects/pjmagee/swtor-logparser && dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj -c Release</automated>
  </verify>
  <done>
    BoundedCache exposes span TryGetValue + span GetOrAdd via GetAlternateLookup with StringComparer.Ordinal; GameObject/Ability/Action Parse hit the span lookup FIRST and never call rom.ToString() on a hit; the string overload and FIFO eviction are preserved; new span-lookup tests pass; all 106 existing tests still pass; core library still builds AOT-clean (no reflection added).
  </done>
</task>

<task type="auto" tdd="true">
  <name>Task 3: Lazy CombatLogLine sub-parsing + re-run benchmark + record before/after</name>
  <files>
    SwtorLogParser/Model/CombatLogLine.cs
  </files>
  <behavior>
    - Source/Target/Ability/Action/Value/Threat return byte-identical values to today (lazy must not change results).
    - Each sub-member is parsed at most once and memoized — including the legitimate "parsed to null" case (Actor.Parse/Value.Parse/Threat.Parse can return null).
    - A line that is read only for TimeStamp (and never touches the sub-properties) performs NO Actor/Ability/Action/Value/Threat parsing.
    - GetHashCode (Rom-based), Equals, and ToString output are unchanged.
  </behavior>
  <action>
    In `CombatLogLine.cs` (PERF-LAZY-01) make Source/Target/Ability/Action/Value/Threat LAZY. Today the private ctor eagerly assigns all six (lines 14–19) and they are get-only auto-properties. Keep TimeStamp parsing EAGER — it gates `Parse` returning null in the static factory and must stay in the factory/ctor.

    The slices stay valid because `Rom` and `Roms` are already retained as fields. Convert the six properties to lazily compute from those fields on first access.

    Nullability hazard: all six are nullable (`Actor?`, `Ability?`, `Action?`, `Value?`, `Threat?`) and their `Parse` can LEGITIMATELY return null, so a plain `_source ??= Actor.Parse(Roms[1])` would re-parse every access on a null result (correctness is preserved but the laziness benefit is lost and parse work repeats). Use a per-member parsed-flag to memoize null too. Cleanest approach: a `bool _xParsed` flag + nullable backing field per member, e.g.
      `private Actor? _source; private bool _sourceParsed;`
      `public Actor? Source { get { if (!_sourceParsed) { _source = Actor.Parse(Roms[1]); _sourceParsed = true; } return _source; } }`
    Apply the identical pattern to Target (`Actor.Parse(Roms[2])`), Ability (`Ability.Parse(Roms[3])`), Action (`Action.Parse(Roms[4])`), Value (`Value.Parse(Rom)`), Threat (`Threat.Parse(Rom)`). Keep the SAME parse calls / SAME source slices as the current ctor — do not change which Roms index or which Rom each uses.

    Do NOT make this thread-unsafe in a way that breaks the pipeline: CombatLogLine instances flow single-threaded through the Rx pipeline per line (produced on the reader task, consumed downstream); the memoization race at worst recomputes an identical value, which is benign and matches the existing lazy-property pattern used in GameObject/Actor (`_name ??= GetName()`). No lock needed — match the existing convention.

    Remove the six eager assignments from the ctor. Keep `Rom`, `Roms`, `TimeStamp` assignments. Preserve `GetHashCode`, `Equals`, `ToString` exactly (ToString already null-guards Value/Threat — it will now trigger lazy parsing, which is fine).

    Confirm no other code relied on eager construction side effects (there are none — parsing is pure/null-returning).

    RE-RUN THE BENCHMARK (Release) after both optimizations are in. Capture the AFTER numbers for all three benchmarks. Compute the delta (bytes/op and ns/op) versus the BEFORE baseline from Task 1.

    SUMMARY.md MUST record, in a before/after table:
      - The two optimizations applied (span-keyed cache lookup; lazy sub-parsing).
      - BEFORE vs AFTER for each benchmark: ns/op, Allocated bytes/op, Gen0.
      - Confirmation that all existing tests pass (count) and the parser/DPS behavior is unchanged.
      - A note that the harness is the regression gate and how to re-run it (`dotnet run --project SwtorLogParser.Benchmarks -c Release`).
  </action>
  <verify>
    <automated>cd D:/Projects/pjmagee/swtor-logparser && dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj -c Release</automated>
    <automated>cd D:/Projects/pjmagee/swtor-logparser && dotnet build SwtorLogParser/SwtorLogParser.csproj -c Release</automated>
    <automated>cd D:/Projects/pjmagee/swtor-logparser && dotnet run --project SwtorLogParser.Benchmarks/SwtorLogParser.Benchmarks.csproj -c Release -- --filter "*"</automated>
  </verify>
  <done>
    All six CombatLogLine sub-properties are lazy with null-memoizing parsed flags; TimeStamp stays eager; GetHashCode/Equals/ToString unchanged; all 106 existing tests pass in Release; core library builds AOT-clean; the benchmark re-runs and AFTER numbers are captured; SUMMARY.md contains the BEFORE/AFTER allocation+timing table and the two optimizations with measured deltas.
  </done>
</task>

</tasks>

<verification>
- `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj -c Release` — all 106+ tests pass (new span-lookup tests added; no regressions).
- `dotnet build SwtorLogParser/SwtorLogParser.csproj -c Release` — core library compiles; IsAotCompatible=true preserved (no reflection, GetAlternateLookup is AOT-safe).
- `dotnet run --project SwtorLogParser.Benchmarks -c Release` — MemoryDiagnoser table emitted; AFTER bytes/op for the hot-cache benchmark is strictly lower than BEFORE (cache-key allocations removed); pure-parse path shows reduced allocation/time for dropped lines (lazy sub-parsing).
- Sample fixture contains NO real player handle — only `@Player#000000000000000`.
- `BenchmarkDotNet.Artifacts/` remains gitignored (already present, line 58).
</verification>

<success_criteria>
- Benchmark harness exists, builds, runs, and is the documented regression gate.
- Cache-HIT Parse path allocates no string key (Optimization 1) — proven by the hot-cache benchmark delta and the new span-lookup unit tests.
- Dropped/filtered lines skip sub-object parsing (Optimization 2) — proven by the pure-parse benchmark delta.
- All 106 existing tests pass — parser model and DPS/HPS stream byte-identical.
- BEFORE/AFTER ns/op + bytes/op + Gen0 recorded in SUMMARY.md with measured deltas.
- Core library stays IsAotCompatible=true (no reflection added).
</success_criteria>

<output>
Create `.planning/quick/260612-czd-benchmark-optimize-combatlogline-parse-a/260612-czd-SUMMARY.md` when done — include the BEFORE/AFTER allocation + timing table for all three benchmarks and the measured deltas for both optimizations.
</output>
