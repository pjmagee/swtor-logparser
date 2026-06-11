# Phase 2: Correctness Bugs - Research

**Researched:** 2026-06-11
**Domain:** Defensive span-parsing hardening in .NET 8 (TryParse guards, InvariantCulture, ConcurrentDictionary, CancellationToken wiring, read-only file IO) under a green-every-commit characterization-test contract
**Confidence:** HIGH (every claim below verified against actual source read this session; the Phase-1 cite map was re-verified line-by-line and several line numbers HAD shifted after the formatter chore — corrected map below)

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Malformed-input & parse guards (BUG-03, BUG-05)**
- On a failed numeric parse at the unguarded sites, the parse path returns `null` / skips the line — it never throws. Reader loop skips a null line.
- Use `int.TryParse` / `long.TryParse` / `ulong.TryParse` at the currently-unguarded sites (`Threat.cs:14`, `Actor.cs:64,73,93,100,107`, `Value.cs:47`, `GameObject.cs:75,87,95`). Allocation-free, no try/catch.
- `DateTime` parsing (`CombatLogLine.cs:9`, BUG-03) uses `CultureInfo.InvariantCulture` with an explicit format; an unparseable timestamp causes the line to be skipped (consistent with the numeric policy), not an exception.
- **Flip the Phase 1 characterization tests:** the EAGER `Assert.Throws` tests (GameObject non-numeric id, CombatLogLine bad timestamp) and LAZY property-throw tests (Actor/Threat/Value/Ability) become `Assert.Null` / graceful-skip assertions as their production fix lands, in the SAME commit. The suite stays green every commit.

**Cache thread-safety (BUG-06)**
- Fix the race NOW — it is a correctness bug. Convert the shared static parse caches to `ConcurrentDictionary` and use `GetOrAdd` (lock-free reads, eliminates the `Dictionary.Add` race from the reader task). Sites: `Action.cs:47-53`, `GameObject.cs:103-108`, `Ability.cs:15-18`, `CombatLogs.cs:8-9`.
- The FULL redesign (content-based keys instead of `ReadOnlyMemory<char>.GetHashCode()`, bounded growth) remains Phase 3 (RFCT-03). Phase 2 keeps the existing hash-code key but makes access thread-safe.

**Monitor lifecycle & file access (BUG-01, BUG-02, BUG-04, BUG-07)**
- BUG-01: pass the linked `_cancellationTokenSource.Token` to `MonitorAsync` and `ReadAsync` (not the outer `cancellationToken`) so `Stop()`'s cancel actually reaches the worker tasks.
- BUG-02: null-guard `_cancellationTokenSource` in `Stop()` so calling `Stop()` before `Start()` is a safe no-op (no NRE).
- BUG-04: guard the `CombatLogs` static constructor — filenames without `_` are skipped instead of throwing `IndexOutOfRange`/`TypeInitializationException` at startup.
- BUG-07: open combat-log files `FileAccess.Read` / `FileShare.Read` in `CombatLog.GetLogLines()` (`CombatLog.cs:24`).

**Tests for the fixes**
- Phase 2 adds/updates only parser-level + the flipped characterization tests + unit tests for the now-guarded parse sites, plus a focused test for the static-ctor guard and (where feasible without the Phase 3 DI refactor) the `Stop()`-before-`Start()` no-op and cancellation wiring.
- Monitor-lifecycle / Rx pipeline (TEST-01) and DPS/HPS math (TEST-02) full suites stay Phase 3.
- Each bug fix commits together with its flipped/added test; `dotnet test` stays GREEN with zero skips on every commit.

### Claude's Discretion
- Exact `DateTime` format string(s) to accept, helper/extension placement for TryParse, and whether `Stop()`/cancellation can be unit-tested now vs. deferred to Phase 3 are at Claude's discretion, guided by keeping the suite green and not pulling Phase 3 DI work forward.

### Deferred Ideas (OUT OF SCOPE)
- Singleton → DI refactor (RFCT-02), content-keyed/bounded cache redesign (RFCT-03), view dedup (RFCT-01) → Phase 3.
- Monitor lifecycle / Rx / DPS-HPS test suites (TEST-01/02) → Phase 3.
- The window filter comparing to `DateTime.Now` (`CombatLogsMonitor.cs:51`) — catalogued in CONCERNS but NOT assigned a BUG id; out of Phase 2 scope.
- `ReadAsync`'s own `FileShare.ReadWrite` (`CombatLogsMonitor.cs:174`) is a SEPARATE site from BUG-07 (`CombatLog.cs:24`). BUG-07 names ONLY `CombatLog.cs:24`. See Open Question 1.
- Package GA upgrades (DEP-01), perf (PERF-01..03) → later phases.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| BUG-01 | `Stop()` actually cancels monitor + reader (workers observe `_cancellationTokenSource.Token`) | Verified: `Start` (CombatLogsMonitor.cs:116-123) creates the linked CTS but passes the **outer** `cancellationToken` to both `Task.Factory.StartNew` lambdas. Fix = capture `_cancellationTokenSource.Token` into a local and pass it. Per-bug table below. |
| BUG-02 | `Stop()` before `Start()` does not NRE | Verified: `_cancellationTokenSource` (line 27) is a **non-nullable** field assigned only in `Start`; `Stop` (line 125-137) calls `.Cancel()` unconditionally. Field is null until `Start`. Fix = make nullable + null-guard. Also `_logger` is null in the parameterless ctor path — `Stop`'s catch logs through `_logger` (NRE risk if it ever catches). |
| BUG-03 | All `DateTime`/numeric parsing uses InvariantCulture; bad timestamp skips the line | Verified: `CombatLogLine.cs:9` `DateTime.Parse(Roms[0].Span)` — no culture, EAGER in ctor. SWTOR emits **time-only** stamps `HH:mm:ss` and `HH:mm:ss.fff` (confirmed from golden lines). Use `DateTime.TryParseExact(span, formats, InvariantCulture, …)`; on false → `Parse` returns null. |
| BUG-04 | `CombatLogs` static ctor tolerates filenames without `_` | Verified: `CombatLogs.cs:23` `x.Name.Split('_')[1]` — shifted from cited :23, still :23. Index `[1]` throws `IndexOutOfRangeException` → wrapped as `TypeInitializationException` at first static access. Fix = filter/guard via `Split` length check or `TryGetSecondSegment`. |
| BUG-05 | Numeric parse paths skip malformed lines instead of throwing | Verified eager/lazy map below — line numbers re-checked, all still match the cite map: `Threat.cs:14`, `Actor.cs:64,73,93,100,107`, `Value.cs:47`, `GameObject.cs:75,87,95`. Convert each to `TryParse`; return `null` from the getter/`Parse` on failure. |
| BUG-06 | Shared caches thread-safe (no `Dictionary.Add` race) | Verified: `CombatLogs.cs:8-9` two `Dictionary<int,_>`; written via `.Add` at `Action.cs:53`, `GameObject.cs:108`, `Ability.cs:19`. Convert to `ConcurrentDictionary`. **Caveat: cannot blind-`GetOrAdd` because the factories return null on failure — see Pattern 3.** |
| BUG-07 | Combat-log files opened read-only | Verified: `CombatLog.cs:24` `FileInfo.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)`. Fix = `FileAccess.Read, FileShare.Read` (or `ReadWrite` share so the live game can still write — see Open Question 2). |
</phase_requirements>

## Summary

Phase 2 turns seven catalogued defects into guarded behavior **without changing the parser's external contract** (`static T? Parse(ReadOnlyMemory<char>)`, lazy `_field ??= Get…()` getters) and without pulling forward the Phase-3 DI/cache-redesign work. Every fix is local and uses only .NET 8 BCL primitives already available in-box: `int/long/ulong.TryParse(ReadOnlySpan<char>, out …)`, `DateTime.TryParseExact(ReadOnlySpan<char>, …, CultureInfo.InvariantCulture, …)`, `System.Collections.Concurrent.ConcurrentDictionary<TKey,TValue>`, and `CancellationTokenSource.CreateLinkedTokenSource`. **No new NuGet packages** — so there is no Package Legitimacy Audit to run.

The dominant constraint is the **green-every-commit contract**. Phase 1 deliberately authored characterization tests that assert the *current* throwing behavior (`Assert.Throws<FormatException>`) at every site Phase 2 fixes. Each Phase-2 production edit must land **in the same commit** as the inversion of its matching test(s) from `Assert.Throws` (eager) / `Assert.Throws` on a property (lazy) to `Assert.Null` (eager → Parse now returns null) / `Assert.Null` on the property (lazy → getter now returns null). I re-read every test file this session and built the exact per-bug "which test flips, and to what" table below — this is the single most load-bearing artifact for the planner.

Two non-obvious correctness traps surfaced from the source that the cite-map alone does not reveal: (1) **the cache factories return `null` on parse failure**, so a naive `cache.GetOrAdd(key, _ => new GameObject(rom))` would both cache failures and store nulls — the GetOrAdd must wrap only a *successful* construction (Pattern 3); and (2) **`GameObject.Parse` and `Ability.Parse` share the same `GameObjectCache`** and both store under `Rom.GetHashCode()`, so a cache hit can return the wrong runtime subtype — a latent bug that Phase 2 must **not regress** (the existing tests use distinct literals to avoid it; keep that discipline, do not "fix" the shared key — that is RFCT-03/Phase 3).

**Primary recommendation:** Sequence the phase as 7 atomic fix+test commits (one per BUG), starting with the independent leaf fixes (BUG-07 file IO, BUG-04 static ctor, BUG-02 Stop-guard) that flip no parser tests, then BUG-05/BUG-03 (which flip the bulk of the characterization tests), then BUG-06 (cache thread-safety, which must be coordinated with the BUG-05 null-return change), and finally BUG-01 (cancellation wiring). Run `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` after every commit; it must stay green with zero skips.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Numeric/date field parsing (BUG-03, BUG-05) | Core lib `Model/*.cs` `Parse` factories + lazy getters | — | Parsing is the model layer's sole job; guards belong where the parse happens |
| Skip a now-null parsed line | Core lib `Monitor/CombatLogsMonitor.ReadAsync` | `CombatLog.GetLogLines` | Both consume `CombatLogLine.Parse`; both **already** null-check (see Don't Hand-Roll) — no new filtering needed |
| Shared cache concurrency (BUG-06) | Core lib `Monitor/CombatLogs.cs` (the dictionaries) | `Action`/`GameObject`/`Ability` `Parse` (the call sites) | The dictionaries live on `CombatLogs`; the unsafe `.Add` calls live in the factories |
| Worker cancellation (BUG-01, BUG-02) | Core lib `Monitor/CombatLogsMonitor` lifecycle | — | Task/CTS ownership is entirely inside the monitor |
| Startup settings enumeration (BUG-04) | Core lib `Monitor/CombatLogs` static ctor | — | The crash is at type-init of `CombatLogs` |
| Read-only log file open (BUG-07) | Core lib `Monitor/CombatLog.GetLogLines` | — | The file handle is opened here |

## Per-Bug Implementation Table (THE load-bearing artifact)

> Verified against source read 2026-06-11. "Test flips" = the Phase-1 characterization test that must be inverted **in the same commit** as the production edit.

| BUG | File:line (verified) | Production edit | Phase-1 test(s) that FLIP | New test(s) to ADD |
|-----|----------------------|-----------------|---------------------------|--------------------|
| **BUG-07** | `CombatLog.cs:24` | `FileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.Read)` (or keep `FileShare.ReadWrite` — Open Q2). | None (no test exercises this path; `All_Logs_Are_Not_Null` is filesystem-gated, Phase-3, untouched) | Optional: a test that opens a temp file with a writer holding a write lock and asserts `GetLogLines()` still reads (only if `FileShare.Read` chosen — verify it does not break concurrent game writes). LOW priority. |
| **BUG-04** | `CombatLogs.cs:23` | Replace `x.Name.Split('_')[1]` with a guarded projection: `.Select(x => x.Name.Split('_'))`.`Where(p => p.Length > 1)`.`Select(p => p[1])` (or a small `TryGetPlayerName` helper). | None (static-ctor crash has no direct test; it would manifest as a `TypeInitializationException` in any test that touches `CombatLogs.PlayerNames`) | ADD a focused test for the guard helper: a filename **without** `_` yields no entry / no throw. Must NOT depend on the real filesystem — extract the split logic into an `internal static` testable helper and unit-test that helper directly (the static ctor itself is filesystem-bound and stays Phase-3-untestable). |
| **BUG-02** | `CombatLogsMonitor.cs:27,125-137` | Make `_cancellationTokenSource` nullable (`CancellationTokenSource?`); in `Stop()` guard: `_cancellationTokenSource?.Cancel();` and null-guard `_logger` use, or early-return if null. | None | ADD `Stop_Before_Start_Does_Not_Throw` — construct a monitor (needs `Instance` in DEBUG build) and call `Stop()`; assert no throw. See Open Question 3 (construction without DI). |
| **BUG-01** | `CombatLogsMonitor.cs:116-123` | After creating the linked CTS, capture `var token = _cancellationTokenSource.Token;` and pass **that** to both `MonitorAsync(token)`/`ReadAsync(token)` and to `Task.Factory.StartNew(..., token)`. | None | ADD (if feasible without DI, Claude's discretion) `Start_Then_Stop_Cancels_Workers` — `Start(CancellationToken.None)`, `Stop()`, await/poll `IsRunning == false`. May defer to Phase-3 TEST-01 if construction is awkward — see Open Question 3. |
| **BUG-05 (Threat)** | `Threat.cs:14` | `Value` getter: `int.TryParse(Rom.Span, out var v) ? v : (int?)null` — **but `Value` is typed `int` and is read by `IsPositive`/`IsNegative`.** Changing to `int?` ripples. Recommended: keep `int Value` but guard via `int.TryParse(...) ? v : 0`, OR (cleaner) make `Value` an `int?` and adjust `IsPositive`/`IsNegative` to null-aware. Decide in plan; null-return is the CONTEXT policy. | `ThreatTests.Threat_NonNumeric_Value_Throws_On_Access_Today` (line 66-72): flip `Assert.Throws<FormatException>(() => _ = threat.Value)` → assert the now-graceful result (`Assert.Null(threat.Value)` if `int?`, or document the chosen sentinel). | ADD a positive golden if the type changes (e.g. `<123>` still `=> 123`). Keep `Threat_Parse_Rejects_Cleanly` green (unchanged). |
| **BUG-05 (Actor)** | `Actor.cs:64,73,93,100,107` | `GetHealth`/`GetMaxHealth` → `int.TryParse(slice, out var h) ? h : (int?)null` (already returns `int?`). `GetId` (3 sites) → `long.TryParse(slice, out var id) ? id : (long?)null` (already returns `long?`). | `ActorTests.Actor_NonNumeric_Health_Throws_On_Access_Today` (108-114): flip to `Assert.Null(a.Health)`. `ActorTests.Actor_NonNumeric_Id_Throws_On_Access_Today` (119-125): flip to `Assert.Null(a.Id)`. | Keep `Player_Is_Parsed`/`Npc_Is_Parsed`/`Companion_Is_Parsed` green (positive goldens already assert correct values). |
| **BUG-05 (Value)** | `Value.cs:47` | `Id` getter: `ulong.TryParse(slice, out var id) ? id : (ulong?)null` (already returns `ulong?`). | `ValueTests.Value_NonNumeric_Id_Throws_On_Access_Today` (155-161): flip to `Assert.Null(value.Id)`. | Keep `Value_Parse_Rejects_Cleanly` and the dozen positive goldens green. |
| **BUG-05 (GameObject — EAGER)** | `GameObject.cs:75 (GetParentId), 87/95 (GetId)` | All three → `ulong.TryParse(slice, out var id) ? id : (ulong?)null` (already `ulong?`). **Because `GameObject.Parse` reads `.Id` eagerly (line 107 `if (gameObject.Id == null) return null;`), a non-numeric id now makes `GetId` return null → `Parse` returns null** (no longer throws). | `GameObjectTests.GameObject_NonNumeric_Id_Throws_Today` (73-78): flip `Assert.Throws<FormatException>(...)` → `Assert.Null(GameObject.Parse("WidgetEager {abc}".AsMemory()))`. | Keep `GameObject_Malformed_Braces_Return_Null`, `..._Name_With_Delimiters_…`, `..._Golden_All_Fields` green. |
| **BUG-05 (Ability — LAZY, inherits GetId)** | inherits `GameObject.GetId` (no own numeric parse) | Fixed transitively by the GameObject edit. `Ability.Parse` (line 10-21) does **not** read `.Id`, so `Parse` still returns non-null; `.Id` now returns null instead of throwing. | `AbilityTests.Ability_NonNumeric_Id_Throws_On_Access_Today` (49-56): flip `Assert.Throws<FormatException>(() => _ = ability.Id)` → `Assert.Null(ability.Id)`. | Keep three positive Ability goldens green. |
| **BUG-03 (CombatLogLine — EAGER)** | `CombatLogLine.cs:9` | In ctor, replace `DateTime.Parse(Roms[0].Span)` with `DateTime.TryParseExact(Roms[0].Span, _timeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts)` and on **false**, signal failure so `Parse` returns null. **Constructor cannot return null** — refactor so `Parse` validates the timestamp **before** constructing, or have the ctor set a `bool _valid` and `Parse` checks it. See Pattern 4. Formats: `["HH:mm:ss", "HH:mm:ss.fff"]`. | `CombatLogLineTests.CombatLogLine_NonParseable_Timestamp_Throws_Today` (99-107): flip `Assert.Throws<FormatException>(() => CombatLogLine.Parse(line.AsMemory()))` → `Assert.Null(CombatLogLine.Parse(line.AsMemory()))`. | Keep `CombatLogLine_Golden_TimeOnly_Stamp_Parses` and the other golden/section tests green. |
| **BUG-06 (caches)** | `CombatLogs.cs:8-9`; call sites `Action.cs:47,53`, `GameObject.cs:103,108`, `Ability.cs:15,19` | `ConcurrentDictionary<int, Action>` / `<int, GameObject>`; replace `TryGetValue`+`Add` with the **conditional** pattern (Pattern 3) — NOT a blind `GetOrAdd` (factories return null on failure). | None directly. **Watch:** `GameObjectTests.Game_Objects_Equality_Reflects_Backing_Memory` (27-46) asserts cache-instance identity for a *shared* backing memory — must stay green (ConcurrentDictionary preserves first-writer-wins). `ActionTests.Same_Actions_Are_Equal` relies on `Rom.SequenceEqual`, not the cache — unaffected. | Optional: a concurrency smoke test (parallel `GameObject.Parse` of the same shared memory from N threads asserts a single cached instance, no exception). Discretion. |

## Standard Stack

No new packages. Everything is .NET 8 BCL, already available. `[VERIFIED: SwtorLogParser.csproj + .NET 8 BCL surface]`

### Core
| API | Where (in-box since) | Purpose | Why standard |
|-----|----------------------|---------|--------------|
| `int.TryParse(ReadOnlySpan<char>, out int)` | `System` (netcoreapp2.1+) | Allocation-free numeric guard, no try/catch | The canonical non-throwing parse; matches the existing `float.TryParse(span, …)` already used in `Actor.ExtractPosition` (`Actor.cs:147`) |
| `long.TryParse` / `ulong.TryParse` (span overloads) | `System` (netcoreapp2.1+) | Same for id fields | Same |
| `DateTime.TryParseExact(ReadOnlySpan<char>, string[], IFormatProvider, DateTimeStyles, out DateTime)` | `System` (netcoreapp2.1+) | Locale-stable timestamp parse, no throw | `InvariantCulture` + explicit `HH:mm:ss[.fff]` formats; rejects unparseable cleanly |
| `System.Collections.Concurrent.ConcurrentDictionary<TKey,TValue>` | `System.Collections.Concurrent` (always present in net8.0) | Thread-safe cache; lock-free reads | AOT-safe (no reflection); drop-in for the two `Dictionary<int,_>` |
| `CancellationTokenSource.CreateLinkedTokenSource` / `.Token` | `System.Threading` | Already used; the fix is to *pass* `.Token`, not the outer token | Standard linked-cancellation idiom |

### Supporting
| API | Purpose | When to use |
|-----|---------|-------------|
| `CultureInfo.InvariantCulture` (`System.Globalization`) | Locale-independent parse provider | Pass to every `TryParse`/`TryParseExact` that touches culture-sensitive content (already imported in `Actor.cs`) |
| `DateTimeStyles.None` | Strict timestamp parse | With `TryParseExact` |

### Alternatives Considered
| Instead of | Could use | Tradeoff |
|------------|-----------|----------|
| `ConcurrentDictionary` + conditional add | `lock` around the existing `Dictionary` | Coarser; CONTEXT explicitly chose `ConcurrentDictionary`/`GetOrAdd`. Use the conditional pattern (Pattern 3), not a literal `GetOrAdd`, because the value factory can fail. |
| `DateTime.TryParseExact` with explicit formats | `DateTime.TryParse(span, InvariantCulture, …)` | `TryParse` (non-exact) is laxer and could accept odd strings; SWTOR's format is known and fixed (`HH:mm:ss[.fff]`), so `TryParseExact` is tighter and intent-revealing. Either satisfies BUG-03; exact is recommended. |
| Make `Threat.Value` `int?` | Keep `int`, return `0` on failure | `int?` is the truthful null-policy match but ripples into `IsPositive`/`IsNegative` and the existing `Threat_NonNumeric_Value_Throws…` flip. Planner decides; document the chosen flip target. |

**Installation:** None.

```bash
# No package install. Verify after each fix commit:
dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj
```

## Package Legitimacy Audit

**Not applicable.** Phase 2 installs **no external packages** — all APIs are .NET 8 BCL already referenced transitively by the `net8.0` target. Verified by reading `SwtorLogParser.csproj` (no new `PackageReference` needed) this session. `[VERIFIED: SwtorLogParser.csproj]`

## Architecture Patterns

### System Architecture Diagram

```
  raw log line (string)
       │ .AsMemory()
       ▼
  CombatLogLine.Parse(rom)  ──(sections != 5)──►  null ──► skipped by reader
       │ ctor
       ├─ DateTime.TryParseExact(Roms[0]) ──(false)──►  Parse returns null ──► skipped   [BUG-03]
       ├─ Actor.Parse(Roms[1/2])  ─ lazy getters ─ int/long.TryParse ──► int?/long? (null on bad)  [BUG-05]
       ├─ Ability.Parse(Roms[3])  ─ inherits GameObject.GetId ─ ulong.TryParse ──► null on bad      [BUG-05]
       ├─ Action.Parse(Roms[4])   ─ ConcurrentDictionary conditional cache ─► child GameObject.Parse [BUG-06]
       │        │                                                    │ ulong.TryParse ──► null ──► Parse null ──► Action ctor `?? throw` ──► caught ──► Action.Parse null
       ├─ Value.Parse(rom)        ─ .Id getter ulong.TryParse ──► null on bad                         [BUG-05]
       └─ Threat.Parse(rom)       ─ .Value getter int.TryParse ──► null/0 on bad                      [BUG-05]
       ▼
  non-null CombatLogLine ──► CombatLogLines.OnNext  /  CombatLogChanged

  ── lifecycle ──
  Start(outerToken) ─ CreateLinkedTokenSource(outerToken) ─► token = cts.Token  [BUG-01: pass token, not outerToken]
       └─ Task.Factory.StartNew(() => ReadAsync(token), token)   ─ reads file: FileShare (BUG-07 is CombatLog.cs)
  Stop() ─ cts?.Cancel()  [BUG-02: null-guard]  ─► token cancels ─► both workers exit loops

  ── startup ──
  static CombatLogs() ─ enumerate *PlayerGUIState.ini ─ Split('_') guarded [BUG-04] ─► PlayerNames
```

### Recommended Structure (no new files; edits in place)
```
SwtorLogParser/
├── Monitor/
│   ├── CombatLogsMonitor.cs   # BUG-01 (Start token), BUG-02 (Stop null-guard)
│   ├── CombatLogs.cs          # BUG-04 (Split guard), BUG-06 (ConcurrentDictionary fields)
│   └── CombatLog.cs           # BUG-07 (FileAccess.Read)
└── Model/
    ├── CombatLogLine.cs       # BUG-03 (TryParseExact + valid gate)
    ├── Threat.cs              # BUG-05 (int.TryParse)
    ├── Actor.cs               # BUG-05 (int/long.TryParse x5)
    ├── Value.cs               # BUG-05 (ulong.TryParse)
    ├── GameObject.cs          # BUG-05 (ulong.TryParse x3), BUG-06 (cache add)
    ├── Action.cs              # BUG-06 (cache add)
    └── Ability.cs             # BUG-06 (cache add); BUG-05 fixed transitively
```
*(Optional, Claude's discretion: a tiny `internal static` TryParse-helper or the BUG-04 split helper may live in `Model/` or `Monitor/` — keep it minimal, the BCL `TryParse` is already the helper.)*

### Pattern 1: Span TryParse guard (BUG-05, the common case)
**What:** Replace an unguarded `int/long/ulong.Parse(span)` in a lazy getter with the `TryParse` ternary. The getter return type is **already nullable** at every Actor/Value/GameObject site, so this is a drop-in.
**When:** `Actor.GetHealth/GetMaxHealth/GetId`, `Value.Id`, `GameObject.GetId/GetParentId`.
**Example:**
```csharp
// Source: .NET BCL int.TryParse(ReadOnlySpan<char>, out int); mirrors existing Actor.cs:147 float.TryParse usage
private long? GetId()
{
    if (IsNpc)
    {
        var open = Roms[0].Span.IndexOf('{');
        var close = Roms[0].Span.IndexOf('}');
        if (open == -1 || close == -1) return null;
        var slice = Roms[0].Span.Slice(open + 1, close - open - 1);
        return long.TryParse(slice, out var id) ? id : (long?)null;   // was: long.Parse(slice)
    }
    // … other branches identical shape …
    return null;
}
```

### Pattern 2: EAGER-site guard cascades to a null Parse (BUG-05 GameObject, BUG-03 timestamp)
**What:** Where `Parse` reads the value eagerly, a `TryParse`-returns-null naturally makes `Parse` return null — no extra plumbing. `GameObject.Parse` already has `if (gameObject.Id == null) return null;` (line 107). The fix to `GetId` is sufficient; `Parse` already does the right thing once `GetId` stops throwing.
**Warning:** Do not also add a redundant try/catch — the whole point is to remove throwing.

### Pattern 3: ConcurrentDictionary conditional add (BUG-06) — NOT a blind GetOrAdd
**What:** The factories cache only **successful** constructions and **return null on failure**. A literal `cache.GetOrAdd(key, _ => new GameObject(rom))` would (a) run the factory under the lock and (b) cache a value even when the parse should yield null. Use TryGetValue → construct → validate → `TryAdd`, returning the cached instance if another thread won the race.
**When:** `GameObject.Parse`, `Ability.Parse`, `Action.Parse`.
**Example:**
```csharp
// Source: ConcurrentDictionary thread-safe TryAdd; preserves "null on bad parse" + first-writer-wins
public static GameObject? Parse(ReadOnlyMemory<char> rom)
{
    var key = rom.GetHashCode();                                   // Phase-2: keep existing key (RFCT-03 changes it)
    if (CombatLogs.GameObjectCache.TryGetValue(key, out var cached))
        return cached;

    var gameObject = new GameObject(rom);
    if (gameObject.Id == null) return null;                        // do NOT cache failures

    // first-writer-wins; if another thread added concurrently, return theirs
    return CombatLogs.GameObjectCache.TryAdd(gameObject.GetHashCode(), gameObject)
        ? gameObject
        : CombatLogs.GameObjectCache[gameObject.GetHashCode()];
}
```
For `Action.Parse` (which uses try/catch around `new Action(rom)`), keep the try/catch and replace only `.Add` with `TryAdd` (same first-writer-wins return).

### Pattern 4: Constructor-can't-return-null gate (BUG-03)
**What:** `DateTime.TryParseExact` returning false must make `Parse` return null, but the parse happens in the **constructor** (`CombatLogLine.cs:9`). Two clean options:
- **(a) Validate before constructing** — move the timestamp parse into `Parse` (or a static `TryParseTimestamp`) and only `new CombatLogLine(...)` when it succeeds, passing the parsed `DateTime` in. Cleanest; keeps the ctor non-throwing.
- **(b) `bool _valid` flag** — ctor sets `_valid = DateTime.TryParseExact(...)`; `Parse` returns `line._valid ? line : null`. Smaller diff but leaves a half-built object.
Recommend (a). Either keeps `Parse` returning null on a bad timestamp, flipping the characterization test.
```csharp
// Option (a) sketch
public static CombatLogLine? Parse(ReadOnlyMemory<char> rom)
{
    if (rom.IsEmpty) return null;
    var sections = GetSections(rom);
    if (sections.Count != 5) return null;
    if (!DateTime.TryParseExact(sections[0].Span, TimeFormats,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts))
        return null;                                   // BUG-03: bad timestamp -> skip line
    return new CombatLogLine(rom, sections, ts);
}
private static readonly string[] TimeFormats = { "HH:mm:ss", "HH:mm:ss.fff" };
```

### Pattern 5: Cancellation wiring (BUG-01) + Stop guard (BUG-02)
```csharp
// BUG-01: pass the LINKED token, not the outer cancellationToken
public void Start(CancellationToken cancellationToken)
{
    _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    var token = _cancellationTokenSource.Token;                       // <-- the fix
    _monitor = Task.Factory.StartNew(() => MonitorAsync(token), token);
    _reader  = Task.Factory.StartNew(() => ReadAsync(token),  token);
}

// BUG-02: field becomes CancellationTokenSource?  and Stop null-guards
public void Stop()
{
    try { _cancellationTokenSource?.Cancel(); _monitor = null; _reader = null; }
    catch (Exception e) { _logger?.LogError(e, "Cancel failed"); }     // _logger may be null too
}
```

### Anti-Patterns to Avoid
- **Blind `GetOrAdd(key, _ => new T(rom))`** for the caches — caches failures and runs the factory inside the lock; the factories legitimately return null. Use Pattern 3.
- **Changing the cache KEY** from `Rom.GetHashCode()` — that is RFCT-03/Phase 3 and would break `Game_Objects_Equality_Reflects_Backing_Memory` and the distinct-literal discipline the Phase-1 tests depend on.
- **Adding try/catch to "fix" BUG-05** — the locked policy is `TryParse` (allocation-free, no exceptions). Try/catch is the thing being removed.
- **Wrapping `DateTime.Parse` in try/catch** — use `TryParseExact`; catching `FormatException` is the anti-pattern BUG-03 replaces.
- **Touching `All_Logs_Are_Not_Null` / `Player_Is_Local_Is_True`** (filesystem-gated) — Phase 3.
- **Mutating `Threat.Value` semantics silently** — if you keep it `int` and return `0` on failure, a real threat of `0` is now indistinguishable from a parse failure. Prefer `int?` and flip the test accordingly, or document the sentinel choice explicitly.
- **Letting two fix commits leave the suite red between them** — each BUG's production edit and its test flip ship together.

## Don't Hand-Roll

| Problem | Don't build | Use instead | Why |
|---------|-------------|-------------|-----|
| Non-throwing numeric parse | try/catch around `int.Parse` | `int/long/ulong.TryParse(span, out …)` | Allocation-free, no exception cost, the locked policy |
| Locale-stable date parse | manual `HH:mm:ss` tokenizer | `DateTime.TryParseExact(span, formats, InvariantCulture, …)` | Handles `.fff` optional, validates, no throw |
| Thread-safe cache | hand-rolled `lock` + `Dictionary` | `ConcurrentDictionary` + `TryAdd` | Lock-free reads, correct first-writer-wins, AOT-safe |
| Linked cancellation | manual bool flag | `CreateLinkedTokenSource` (already present) — just pass `.Token` | Already correct except for which token is passed |
| Skip a null parsed line | new filtering layer | **existing** null-checks (`CombatLogsMonitor.cs:187` `if (item is not null)`, `CombatLog.cs:34` `if (combatLogLine is not null)`) | Both consumers ALREADY guard null — making `Parse` return null requires **no** reader change |

**Key insight:** The reader loop and `CombatLog.GetLogLines` already filter `null` results from `CombatLogLine.Parse`. The BUG-05/BUG-03 work makes more inputs return null; the consumers need **no** edit. The only place that still *throws-through* is the per-line `try/catch` in `ReadAsync` (line 183-198) — which already logs and continues, so even a residual throw is non-fatal. Do not add a redundant filter.

## Runtime State Inventory

Not a rename/refactor/migration phase — Phase 2 is in-place correctness edits to source, with no datastore keys, service config, OS-registered names, secrets, or build artifacts carrying a renamed string.
- **Stored data:** None — no schema/key changes; the cache key (`Rom.GetHashCode()`) is intentionally preserved.
- **Live service config:** None.
- **OS-registered state:** None.
- **Secrets/env vars:** None — the app reads no env/secret config (`CLAUDE.md` confirms "No environment variables or external configuration").
- **Build artifacts:** None — no package/assembly rename; no new package install.

**Nothing found in any category — verified by reading all seven source files and `CLAUDE.md` this session.**

## Common Pitfalls

### Pitfall 1: Flipping a test in a different commit than its fix
**What goes wrong:** The production edit lands, the matching `Assert.Throws` test goes red (it now gets null, not an exception) until a later commit fixes it — the suite is red mid-phase, violating the green-every-commit contract.
**Why:** Two-step "fix then update tests" habit.
**How to avoid:** Stage the production edit AND the test inversion in the **same** commit. The per-bug table lists each pair. Run `dotnet test` before committing.
**Warning signs:** `Assert.Throws<FormatException>` failing with "Expected FormatException, no exception thrown."

### Pitfall 2: `Threat.Value` type ripple
**What goes wrong:** `Threat.Value` is `int` (not `int?`) and is consumed by `IsPositive => Value >= 0` / `IsNegative => Value < 0`. Returning null requires changing the type and the two dependent properties; returning `0` silently conflates a zero threat with a parse failure.
**How to avoid:** Decide the flip target explicitly. If `int?`: `IsPositive => Value >= 0` becomes null-aware (`Value is >= 0`), and the test flips to `Assert.Null(threat.Value)`. If sentinel `0`: document it and flip the test to `Assert.Equal(0, threat.Value)` with a comment. Recommend `int?`.
**Warning signs:** Compiler errors in `IsPositive`/`IsNegative` after the edit; a "graceful" test that can't distinguish failure from a real zero.

### Pitfall 3: ConcurrentDictionary `GetOrAdd` caching failures / storing null
**What goes wrong:** `GetOrAdd(key, _ => new GameObject(rom))` returns and caches a value even when the parse should be null; and the value factory runs under contention semantics. For `GameObject.Parse` the result must be null when `Id == null`.
**How to avoid:** Pattern 3 — `TryGetValue` → construct → validate → `TryAdd`, return first-writer-wins. Never cache a null or a failed parse.
**Warning signs:** `GameObject_Malformed_Braces_Return_Null` or `Action_Malformed_Inner_Fragment_Returns_Null` going red.

### Pitfall 4: Shared `GameObjectCache` cross-type hit (GameObject vs Ability)
**What goes wrong:** `Ability.Parse` and `GameObject.Parse` both store into `CombatLogs.GameObjectCache` keyed on `Rom.GetHashCode()`. A test that parses the same backing memory as both could get the wrong subtype back. This is a **pre-existing** latent bug (RFCT-03 territory).
**How to avoid:** Do **not** attempt to separate the caches in Phase 2 (that is the key redesign = Phase 3). Preserve current behavior; keep using distinct literals in new tests (as the Phase-1 tests already do). The `(Ability?)value` / `(GameObject?)value` casts must not throw — verify any new concurrency test uses type-consistent literals.
**Warning signs:** An `InvalidCastException` in `Ability.Parse`/`GameObject.Parse` from a cross-type cached hit.

### Pitfall 5: `Action` constructor still throws after BUG-05
**What goes wrong:** `Action`'s constructor does `GameObject.Parse(...) ?? throw new Exception(...)` (lines 11-13). After BUG-05, `GameObject.Parse("{abc}")` returns **null** instead of throwing `FormatException`. The `?? throw` then fires an `Exception`, caught by `Action.Parse`'s try/catch (line 56) → returns null. **Net behavior unchanged** (`Action_Malformed_Inner_Fragment_Returns_Null` stays green) — but the exception *type* the catch sees changes from `FormatException` to the generic `Exception`. The catch is `catch (Exception e)`, so it still catches. Confirm the test asserts only `Assert.Null`, not a specific exception (it does — verified).
**How to avoid:** Leave `Action.cs`'s try/catch and `?? throw` as-is for Phase 2 (cleanup is Phase 3). Just confirm the existing null-return path still holds after BUG-05.
**Warning signs:** `Action_Malformed_Inner_Fragment_Returns_Null` red.

### Pitfall 6: BUG-04 helper must be filesystem-independent to test
**What goes wrong:** The crash is in a static ctor that enumerates a real directory; you cannot unit-test the ctor hermetically (filesystem) — that is Phase-3 abstraction work.
**How to avoid:** Extract the `Name.Split('_')[1]` logic into a pure `internal static string? SecondSegmentOrNull(string fileName)` (or inline `.Where(p => p.Length > 1)`), and unit-test **that helper** with `"abc_def.ini"` → `"def"` and `"nounderscores.ini"` → null/skip. Keep the ctor calling the helper.
**Warning signs:** A BUG-04 test that touches `CombatLogs.PlayerNames` and fails on CI because the SWTOR settings dir doesn't exist.

## Code Examples

### BUG-04 guarded enumeration (filesystem-safe via extracted helper)
```csharp
// In CombatLogs static ctor:
PlayerNames = SettingsDirectory.EnumerateFiles("*PlayerGUIState.ini")
    .Select(x => SecondSegmentOrNull(x.Name))
    .Where(n => n is not null)
    .Select(n => n!)
    .ToHashSet();

internal static string? SecondSegmentOrNull(string fileName)
{
    var parts = fileName.Split('_');
    return parts.Length > 1 ? parts[1] : null;     // was: parts[1] (IndexOutOfRange on no '_')
}
```

### BUG-07 read-only open
```csharp
// CombatLog.GetLogLines, line 24 — read-only access; share mode: see Open Question 2
using var stream = FileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
//                                              ^^^^^^^^^^^^^^^^^ was ReadWrite
//   FileShare.ReadWrite keeps the live game writer able to append while we read;
//   FileShare.Read would block the game's writes — prefer ReadWrite share, Read access.
```

## State of the Art

| Old approach | Current approach | Impact |
|--------------|------------------|--------|
| `int.Parse(span)` in getters → `FormatException` crashes the reader task | `int.TryParse(span, out …)` → graceful null/skip | Malformed lines no longer crash the pipeline |
| `DateTime.Parse(span)` culture-sensitive, throws | `DateTime.TryParseExact(span, formats, InvariantCulture, …)` | Locale-stable, no throw |
| `Dictionary.Add` from background reader (race) | `ConcurrentDictionary.TryAdd` (first-writer-wins) | No corruption / `InvalidOperationException` under concurrent parse |
| `Start` passes outer token to workers | `Start` passes the linked CTS token | `Stop()` actually cancels the workers |

**Deprecated/outdated:** None new. The preview/alpha NuGet versions are real debt but are **DEP-01/Phase 5** — do **not** bump them here.

## Assumptions Log

| # | Claim | Section | Risk if wrong |
|---|-------|---------|---------------|
| A1 | SWTOR timestamps are time-only `HH:mm:ss` / `HH:mm:ss.fff` (no date component) | BUG-03, Pattern 4 | If full dates ever appear, the format array misses them and those lines skip. Evidence: every golden test line (`[18:12:13]`, `[20:33:17.759]`, `[21:45:02.123]`) is time-only. LOW. Mitigation: make `TimeFormats` a single source-of-truth array, easy to extend. |
| A2 | `int?`/`long?`/`ulong?` return types at the Actor/Value/GameObject getters mean the TryParse change is a drop-in (no signature ripple) | Per-bug table | Verified by source read — all those getters already return nullable. Only `Threat.Value` (typed `int`) ripples (Pitfall 2). LOW. |
| A3 | `FileShare.ReadWrite` (not `FileShare.Read`) is the right share mode so the live game can keep appending while the parser reads | BUG-07, Open Q2 | If `FileShare.Read` is chosen, the running game may fail to write the log (sharing violation). CONTEXT says "`FileShare.Read`"; the running-game reality argues for `FileShare.ReadWrite`. MEDIUM — flag for the planner. |
| A4 | The monitor can be constructed for a `Stop`/`Start` unit test in the DEBUG build via the existing `Instance` (or the parameterless ctor) without the Phase-3 DI refactor | BUG-01/02 tests, Open Q3 | If construction proves awkward (private ctors, `#if`-gated `Instance`), those two lifecycle tests defer to Phase-3 TEST-01 — CONTEXT permits this at Claude's discretion. MEDIUM. |

## Open Questions

1. **BUG-07 scope: `CombatLog.cs:24` only, or also `CombatLogsMonitor.cs:174`?**
   - What we know: REQUIREMENTS BUG-07 names **only** `CombatLog.cs:24`. `ReadAsync` at `CombatLogsMonitor.cs:170-175` already uses `FileAccess.Read` but `FileShare.ReadWrite`.
   - What's unclear: Whether the planner should also touch the reader's share mode.
   - Recommendation: Fix only `CombatLog.cs:24` per the literal requirement. Leave `ReadAsync` as-is (it's already `FileAccess.Read`). Note the discrepancy for Phase 3.

2. **BUG-07 share mode: `FileShare.Read` (per CONTEXT) vs `FileShare.ReadWrite` (so the live game can append)?**
   - What we know: The parser tails a log the running game is actively writing. `FileShare.Read` denies other writers → the game could hit a sharing violation; the existing `ReadAsync` deliberately uses `FileShare.ReadWrite`.
   - Recommendation: Use `FileAccess.Read` (satisfies "read-only access") with `FileShare.ReadWrite` (don't block the writer). If the planner insists on the literal `FileShare.Read`, gate it behind a note that it may interfere with a live game session. This is a `checkpoint:human-verify` candidate.

3. **Can `Stop`/cancellation be unit-tested now without the Phase-3 DI refactor?**
   - What we know: `CombatLogsMonitor` has private ctors and a `#if RELEASE/#elif DEBUG`-gated static `Instance`; there is no public constructor.
   - Recommendation: Attempt the `Stop_Before_Start_Does_Not_Throw` and `Start→Stop` tests via the DEBUG `Instance`. If the singleton's shared state makes tests order-dependent/flaky, defer both to Phase-3 TEST-01 (CONTEXT allows). At minimum, the BUG-02 null-guard and BUG-01 token change are verifiable by inspection + the existing build.

4. **`Threat.Value` flip target: `int?` vs sentinel `0`?**
   - Recommendation: `int?` (truthful null policy), flip `Threat_NonNumeric_Value_Throws_On_Access_Today` → `Assert.Null(threat.Value)`, and null-harden `IsPositive`/`IsNegative`. Planner to confirm.

## Environment Availability

| Dependency | Required by | Available | Version | Fallback |
|------------|-------------|-----------|---------|----------|
| .NET 8 SDK | build + `dotnet test` | Assumed ✓ (targets `net8.0`) | 8.0 | — |
| xUnit + runner + Test SDK | test execution | ✓ (in `SwtorLogParser.Tests.csproj`, verified Phase 1) | xunit 2.5.0-pre.44 | — |
| `System.Collections.Concurrent` | BUG-06 | ✓ in-box (net8.0) | — | — |
| SWTOR CombatLogs / settings dir | **NOT required** by Phase-2 tests (all hermetic; BUG-04 tested via extracted helper) | n/a | — | Extract helper; test in-memory |

**Missing dependencies with no fallback:** None.
**Missing dependencies with fallback:** None — all fixes use in-box BCL; all new/flipped tests are in-memory.

## Validation Architecture

> `workflow.nyquist_validation: true` → section included. `[VERIFIED: .planning/config.json]`

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.5.0-pre.44 (+ Test SDK 17.7.0-preview, runner.visualstudio 2.5.0-pre.27) |
| Config file | none — config lives in `SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` |
| Quick run command | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj --filter "FullyQualifiedName~<Type>Tests"` |
| Full suite command | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test type | Automated command | File exists? |
|--------|----------|-----------|-------------------|-------------|
| BUG-03 | Bad timestamp → `Parse` null (was throw) | unit | `dotnet test --filter "FullyQualifiedName~CombatLogLineTests"` | ✅ flip `CombatLogLine_NonParseable_Timestamp_Throws_Today` |
| BUG-05 | GameObject non-numeric id → `Parse` null (eager) | unit | `dotnet test --filter "FullyQualifiedName~GameObjectTests"` | ✅ flip `GameObject_NonNumeric_Id_Throws_Today` |
| BUG-05 | Ability non-numeric id → `.Id` null (lazy) | unit | `dotnet test --filter "FullyQualifiedName~AbilityTests"` | ✅ flip `Ability_NonNumeric_Id_Throws_On_Access_Today` |
| BUG-05 | Actor non-numeric health/id → `.Health`/`.Id` null | unit | `dotnet test --filter "FullyQualifiedName~ActorTests"` | ✅ flip 2 tests |
| BUG-05 | Value non-numeric id → `.Id` null | unit | `dotnet test --filter "FullyQualifiedName~ValueTests"` | ✅ flip `Value_NonNumeric_Id_Throws_On_Access_Today` |
| BUG-05 | Threat non-numeric value → `.Value` graceful | unit | `dotnet test --filter "FullyQualifiedName~ThreatTests"` | ✅ flip `Threat_NonNumeric_Value_Throws_On_Access_Today` |
| BUG-06 | Concurrent parse of shared memory → single instance, no throw | unit | `dotnet test --filter "FullyQualifiedName~GameObjectTests"` | ⚠️ Wave 0 (optional concurrency smoke) |
| BUG-04 | Filename without `_` → skipped, no throw | unit | `dotnet test --filter "FullyQualifiedName~CombatLogs"` | ⚠️ Wave 0 (add helper + test) |
| BUG-02 | `Stop()` before `Start()` → no throw | unit | `dotnet test --filter "FullyQualifiedName~Monitor"` | ⚠️ Wave 0 (add; Open Q3) |
| BUG-01 | `Start`→`Stop` cancels workers | unit | `dotnet test --filter "FullyQualifiedName~Monitor"` | ⚠️ Wave 0 (add; may defer to Phase-3 TEST-01) |
| BUG-07 | Read-only file open | (manual / inspection) | n/a | ⚠️ optional |

### Sampling Rate
- **Per task commit:** `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj --filter "FullyQualifiedName~<TypeUnderEdit>Tests"` — must be green (the flipped test now passes with the new behavior).
- **Per wave merge:** `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` — full suite green, zero skips.
- **Phase gate:** full suite green before `/gsd-verify-work`.

### Wave 0 Gaps
- [ ] BUG-04: extract `internal static string? SecondSegmentOrNull(string)` (or inline guard) in `CombatLogs.cs` + add a unit test (filename without `_` → null/skip). Filesystem-independent.
- [ ] BUG-02: add `Stop_Before_Start_Does_Not_Throw` (construct via DEBUG `Instance`; Open Q3).
- [ ] BUG-01: add `Start_Then_Stop_Cancels_Workers` (or defer to Phase-3 TEST-01 if construction is awkward).
- [ ] BUG-06 (optional): concurrency smoke test — N-thread parallel `GameObject.Parse(sharedMemory)` asserts single instance + no exception.
- [ ] No framework install needed; xUnit `[Theory]`/`[Fact]`/`Assert` already present (verified Phase 1).

*(All other BUG verifications reuse existing Phase-1 tests by flipping their assertions — no new files; extend the existing `*Tests.cs`.)*

## Security Domain

> `security_enforcement: true`, ASVS level 1. `[VERIFIED: .planning/config.json]`

### Applicable ASVS Categories
| ASVS category | Applies | Standard control |
|---------------|---------|-----------------|
| V2 Authentication | no | Local log parser, no auth |
| V3 Session Management | no | No sessions |
| V4 Access Control | no | No access-control surface |
| V5 Input Validation | **yes (the entire phase)** | Phase 2 IS the input-validation remediation: `TryParse`/`TryParseExact` guards make every malformed/locale-variant/delimiter-laden log line fail gracefully (null/skip) instead of crashing the reader task. The BUG-04 static-ctor guard prevents a malformed settings filename from crashing startup. |
| V6 Cryptography | no | No crypto |

### Known Threat Patterns for span-based parsing + background reader
| Pattern | STRIDE | Standard mitigation |
|---------|--------|---------------------|
| Malformed/truncated line → `FormatException`/`IndexOutOfRange` crashes the reader task | Denial of Service | `TryParse` + `IndexOf != -1` slice guards (BUG-05); `TryParseExact` (BUG-03) |
| Locale-dependent timestamp → wrong/throwing parse | Tampering (integrity) | `CultureInfo.InvariantCulture` (BUG-03) |
| Settings filename without `_` → `TypeInitializationException` at startup | Denial of Service | guarded `Split` length check (BUG-04) |
| Concurrent `Dictionary.Add` from reader + host parse → corruption / `InvalidOperationException` | Tampering / DoS | `ConcurrentDictionary.TryAdd`, first-writer-wins (BUG-06) |
| File handle opened ReadWrite while only reading | (least-privilege) | `FileAccess.Read` (BUG-07) |
| `Stop()` fails to cancel workers → runaway background tasks | Resource exhaustion | pass linked CTS token (BUG-01); null-guard `Stop` (BUG-02) |

**Phase 2 security posture:** This phase *remediates* the input-validation threats Phase 1 only detected. No new attack surface (no network, no new IO beyond the read-only narrowing of an existing read). All fixes reduce crash/DoS exposure.

## Sources

### Primary (HIGH confidence — all read this session)
- `SwtorLogParser/Model/{CombatLogLine,Threat,Actor,Value,GameObject,Action,Ability}.cs` — verified every parse site + line number; corrected the post-formatter cite map.
- `SwtorLogParser/Monitor/{CombatLogsMonitor,CombatLogs,CombatLog}.cs` — verified `Start`/`Stop`/`ReadAsync` token wiring, the static ctor `Split`, the file-open call, the two caches.
- `SwtorLogParser/Extensions/CombatLogLineExtensions.cs` — confirmed `IsPlayerDamage`/`IsPlayerHeal` depend on `Action`/`Value` non-null (informs why null-returns must not break the DPS pipeline).
- `SwtorLogParser.Tests/{GameObject,CombatLogLine,Threat,Actor,Value,Ability,Action}Tests.cs`, `GlobalUsings.cs` — exact characterization tests to flip + the distinct-literal cache discipline.
- `SwtorLogParser/SwtorLogParser.csproj` — `net8.0`, `IsAotCompatible=true`, no new package needed.
- `.planning/phases/02-correctness-bugs/02-CONTEXT.md`, `.planning/phases/01-parser-safety-net/01-RESEARCH.md`, `.planning/REQUIREMENTS.md`, `.planning/codebase/CONCERNS.md`, `.planning/config.json`, `CLAUDE.md` — phase constraints, eager/lazy map, requirement IDs, config.

### Secondary (MEDIUM confidence)
- .NET 8 BCL API availability (`int/long/ulong.TryParse(ReadOnlySpan<char>)`, `DateTime.TryParseExact(ReadOnlySpan<char>)`, `ConcurrentDictionary`) — established in-box since netcoreapp2.1/3.0; `[ASSUMED]` from training knowledge, but these are stable, decade-old BCL surfaces.

### Tertiary (LOW confidence)
- None.

## Metadata

**Confidence breakdown:**
- Per-bug edit map + test flips: HIGH — every file/line and every test re-read this session; cite map corrected where the formatter shifted lines.
- Standard stack (BCL APIs): HIGH — no new packages; APIs are long-stable in-box.
- BUG-03 timestamp format: MEDIUM-HIGH — A1 (time-only) evidenced by all golden lines; extensible format array mitigates.
- BUG-07 share mode + BUG-01/02 testability: MEDIUM — Open Questions 2 & 3 for the planner/human.

**Research date:** 2026-06-11
**Valid until:** until the parser source changes (i.e., the moment Phase 2 execution begins editing it). Re-verify line numbers if any other chore touches these files first.
