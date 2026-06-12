# Phase 2: Correctness Bugs - Context

**Gathered:** 2026-06-11
**Status:** Ready for planning

<domain>
## Phase Boundary

Fix the seven correctness defects (BUG-01..07) in the SWTOR parser core so the monitor starts/runs/stops correctly in all conditions, no parse path throws on malformed or locale-variant input, shared caches cannot be corrupted by concurrent access, and log files are opened safely. The Phase 1 characterization tests are the regression contract — as each fix lands, the corresponding `Assert.Throws` test flips to `Assert.Null`/skip.

**In scope:** Production changes to `SwtorLogParser/Monitor/CombatLogsMonitor.cs`, `Monitor/CombatLogs.cs`, `Monitor/CombatLog.cs`, and `Model/*.cs` parse sites; updating/adding tests for the fixed behavior.

**Out of scope:** The `#if RELEASE` singleton → DI refactor (RFCT-02, Phase 3), the full content-keyed/bounded cache redesign (RFCT-03, Phase 3), monitor-lifecycle/Rx and DPS/HPS test suites (TEST-01/02, Phase 3), performance work (Phase 4). Phase 2 fixes the *correctness* of the caches (the race), not their *design*.

</domain>

<decisions>
## Implementation Decisions

### Malformed-input & parse guards (BUG-03, BUG-05)
- On a failed numeric parse at the unguarded sites, the parse path returns `null` / skips the line — it never throws. Reader loop skips a null line.
- Use `int.TryParse` / `long.TryParse` / `ulong.TryParse` at the currently-unguarded sites (`Threat.cs:14`, `Actor.cs:64,73,93,100,107`, `Value.cs:47`, `GameObject.cs:75,87,95`). Allocation-free, no try/catch.
- `DateTime` parsing (`CombatLogLine.cs:9`, BUG-03) uses `CultureInfo.InvariantCulture` with an explicit format; an unparseable timestamp causes the line to be skipped (consistent with the numeric policy), not an exception.
- **Flip the Phase 1 characterization tests:** the EAGER `Assert.Throws` tests (GameObject non-numeric id, CombatLogLine bad timestamp) and LAZY property-throw tests (Actor/Threat/Value/Ability) become `Assert.Null` / graceful-skip assertions as their production fix lands, in the SAME commit. The suite stays green every commit.

### Cache thread-safety (BUG-06)
- Fix the race NOW — it is a correctness bug. Convert the shared static parse caches to `ConcurrentDictionary` and use `GetOrAdd` (lock-free reads, eliminates the `Dictionary.Add` race from the reader task). Sites: `Action.cs:47-53`, `GameObject.cs:103-108`, `Ability.cs:15-18`, `CombatLogs.cs:8-9`.
- The FULL redesign (content-based keys instead of `ReadOnlyMemory<char>.GetHashCode()`, bounded growth) remains Phase 3 (RFCT-03). Phase 2 keeps the existing hash-code key but makes access thread-safe.

### Monitor lifecycle & file access (BUG-01, BUG-02, BUG-04, BUG-07)
- BUG-01: pass the linked `_cancellationTokenSource.Token` to `MonitorAsync` and `ReadAsync` (not the outer `cancellationToken`) so `Stop()`'s cancel actually reaches the worker tasks.
- BUG-02: null-guard `_cancellationTokenSource` in `Stop()` so calling `Stop()` before `Start()` is a safe no-op (no NRE).
- BUG-04: guard the `CombatLogs` static constructor — filenames without `_` are skipped instead of throwing `IndexOutOfRange`/`TypeInitializationException` at startup.
- BUG-07: open combat-log files `FileAccess.Read` / `FileShare.Read` in `CombatLog.GetLogLines()` (`CombatLog.cs:24`).

### Tests for the fixes
- Phase 2 adds/updates only parser-level + the flipped characterization tests + unit tests for the now-guarded parse sites, plus a focused test for the static-ctor guard and (where feasible without the Phase 3 DI refactor) the `Stop()`-before-`Start()` no-op and cancellation wiring.
- Monitor-lifecycle / Rx pipeline (TEST-01) and DPS/HPS math (TEST-02) full suites stay Phase 3.
- Each bug fix commits together with its flipped/added test; `dotnet test` stays GREEN with zero skips on every commit.

### Claude's Discretion
- Exact `DateTime` format string(s) to accept, helper/extension placement for TryParse, and whether `Stop()`/cancellation can be unit-tested now vs. deferred to Phase 3 are at Claude's discretion, guided by keeping the suite green and not pulling Phase 3 DI work forward.

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets
- Phase 1 characterization tests in `SwtorLogParser.Tests/*Tests.cs` encode the exact current throw/null behavior per model — flip these in place.
- `.planning/phases/01-parser-safety-net/01-RESEARCH.md` has the verified eager/lazy parse-site map (which sites throw eagerly from `Parse` vs lazily on property access) — drives which test flips to `Assert.Null` and which production line to guard.

### Established Patterns
- Parse factories are `static {Type}? Parse(ReadOnlyMemory<char>)`; lazy cached properties `_field ??= Get...()`. Keep this shape.
- `ImplicitUsings`/`Nullable` enabled; core library is `IsAotCompatible=true` — no reflection. `ConcurrentDictionary` is AOT-safe.

### Integration Points
- The reader task in `CombatLogsMonitor.ReadAsync` consumes `Parse` results and pushes to the `Subject` — a now-nullable/skipped line must be filtered there.
- Build/test: `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj`.

</code_context>

<specifics>
## Specific Ideas

- BUG cite map (from CONCERNS.md): BUG-01/02 `CombatLogsMonitor.cs:107-126`; BUG-03 `CombatLogLine.cs:9`; BUG-04 `CombatLogs.cs:23`; BUG-05 `Threat.cs:14`,`Actor.cs:64/73/93/100/107`,`Value.cs:47`,`GameObject.cs:75/87/95`; BUG-06 `Action.cs:53`,`GameObject.cs:108`,`Ability.cs:18`; BUG-07 `CombatLog.cs:24`.
- Keep the existing hash-code cache KEY in Phase 2 (only make it thread-safe); changing the key is Phase 3 (RFCT-03) and would otherwise invalidate the cache-unique-literal assumptions in Phase 1 tests.

</specifics>

<deferred>
## Deferred Ideas

- Singleton → DI refactor (RFCT-02), content-keyed/bounded cache redesign (RFCT-03), view dedup (RFCT-01) → Phase 3.
- Monitor lifecycle / Rx / DPS-HPS test suites (TEST-01/02) → Phase 3.

</deferred>
