---
phase: 02-correctness-bugs
verified: 2026-06-11T00:00:00Z
status: human_needed
score: 7/7 must-haves verified
overrides_applied: 0
human_verification:
  - test: "BUG-07 live-tailing: with the SWTOR client running and actively writing a combat log, start the monitor and confirm new lines still appear AND the game is not blocked from writing"
    expected: "Monitor reads new lines incrementally; the game writer is never blocked (validates FileAccess.Read + FileShare.ReadWrite kept together)"
    why_human: "Requires a running SWTOR client actively appending to a live log file — cannot be reproduced in a hermetic unit test"
  - test: "BUG-01 Stop() live cancellation: start the monitor, call Stop(), confirm background CPU/file activity ceases"
    expected: "After Stop(), the MonitorAsync/ReadAsync worker loops observe the linked CTS cancellation and halt"
    why_human: "Full Start/Stop lifecycle assertion was intentionally deferred to Phase-3 TEST-01 (the singleton touches the real combat-logs directory, making a polled IsRunning assertion flaky); live confirmation requires the running client. BUG-01 is verified by source inspection + the BUG-02 no-op test."
---

# Phase 2: Correctness Bugs Verification Report

**Phase Goal:** The monitor starts, runs, and stops correctly in all conditions; no parse path throws on malformed or locale-variant input; shared caches cannot be corrupted; log files are opened safely
**Verified:** 2026-06-11
**Status:** human_needed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
| --- | --- | --- | --- |
| 1 | No numeric parse path throws (Threat/Actor/Value/GameObject) — BUG-05 | ✓ VERIFIED | `Threat.cs:14` `int.TryParse`; `Actor.cs:67,76,96,103,110` `int/long.TryParse`; `Value.cs:47` `ulong.TryParse`; `GameObject.cs:75,87,95` `ulong.TryParse`. Grep for bare `int/long/ulong.Parse(` → 0 hits. |
| 2 | Bad timestamp returns null, locale-independent — BUG-03 | ✓ VERIFIED | `CombatLogLine.cs:47` `DateTime.TryParseExact(sections[0].Span, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts)` → `return null` on failure; `TimeFormats` (line 7) = `HH:mm:ss` / `HH:mm:ss.fff`. No `DateTime.Parse(` anywhere. |
| 3 | Shared caches cannot be corrupted — BUG-06 | ✓ VERIFIED | `CombatLogs.cs:9-10` both caches are `ConcurrentDictionary<int,_>`; `GameObject.cs:110`, `Ability.cs:21`, `Action.cs:55` all use `TryAdd` first-writer-wins. No `Dictionary.Add` / blind `GetOrAdd` on caches (grep `.Add(` in Model → only local List adds). |
| 4 | Stop() actually cancels workers — BUG-01 | ✓ VERIFIED | `CombatLogsMonitor.cs:121-123` captures `var token = _cancellationTokenSource.Token` and passes it to BOTH worker lambdas `MonitorAsync(token)`/`ReadAsync(token)` and the StartNew token arg. Live runtime cancellation routed to human (see below). |
| 5 | Stop() before Start() is a safe no-op — BUG-02 | ✓ VERIFIED | `CombatLogsMonitor.cs:27` field is `CancellationTokenSource?`; `Stop()` uses `_cancellationTokenSource?.Cancel()` (line 130) and `_logger?.LogError` (line 136). Test `Stop_Before_Start_Does_Not_Throw` passes. |
| 6 | Settings filename without '_' does not crash startup — BUG-04 | ✓ VERIFIED | `CombatLogs.cs:31-35` `SecondSegmentOrNull` length-guards `Split('_')` (`parts.Length > 1 ? parts[1] : null`); static ctor (lines 24-28) projects through it and filters nulls. Helper test `CombatLogsHelperTests.cs` passes. |
| 7 | Log files opened safely (read-only, writer not blocked) — BUG-07 | ✓ VERIFIED | `CombatLog.cs:24` `FileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite)`. Grep for standalone `FileShare.Read` → 0 hits (ReadWrite preserved). Test `GetLogLines_Opens_ReadOnly_And_Reads` passes. |

**Score:** 7/7 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
| --- | --- | --- | --- |
| `Model/Threat.cs` | int.TryParse-guarded Value (int?) | ✓ VERIFIED | `Value` is `int?` via `int.TryParse`; `IsPositive`/`IsNegative` null-aware relational patterns (lines 12-14). |
| `Model/Actor.cs` | int/long.TryParse GetHealth/GetMaxHealth/GetId | ✓ VERIFIED | 5 TryParse sites (67,76,96,103,110); slash-index guard added (line 66). |
| `Model/Value.cs` | ulong.TryParse Id | ✓ VERIFIED | Line 47 ternary; surrounding `if (start != -1 && end != -1)` guard preserved. |
| `Model/GameObject.cs` | ulong.TryParse GetId/GetParentId + conditional cache add | ✓ VERIFIED | 3 TryParse sites (75,87,95); `Parse` keeps `if (Id == null) return null;` (107) then TryAdd (110). |
| `Model/Ability.cs` | TryAdd first-writer-wins | ✓ VERIFIED | Line 21 TryAdd, returns cached instance on race (line 25). |
| `Model/Action.cs` | TryAdd inside existing try/catch | ✓ VERIFIED | Line 55 TryAdd inside try; catch body + trailing `return null` preserved. |
| `Model/CombatLogLine.cs` | TryParseExact(InvariantCulture) gate; ctor takes DateTime | ✓ VERIFIED | Line 47 gate in static `Parse`; private ctor takes `DateTime timeStamp` (line 9). |
| `Monitor/CombatLogsMonitor.cs` | nullable CTS, null-guarded Stop, linked-token Start | ✓ VERIFIED | `CancellationTokenSource?` (27); `?.Cancel()` (130); linked token to workers (121-123). |
| `Monitor/CombatLog.cs` | FileAccess.Read + FileShare.ReadWrite | ✓ VERIFIED | Line 24. |
| `Monitor/CombatLogs.cs` | SecondSegmentOrNull helper; ConcurrentDictionary fields | ✓ VERIFIED | Helper lines 31-35; caches lines 9-10; `using System.Collections.Concurrent;` (1). |
| `Tests/CombatLogsHelperTests.cs` | filesystem-independent BUG-04 test | ✓ VERIFIED | File exists; tests `SecondSegmentOrNull` in isolation. |

### Key Link Verification

| From | To | Via | Status | Details |
| --- | --- | --- | --- | --- |
| Start() | MonitorAsync/ReadAsync | linked `_cancellationTokenSource.Token` to lambdas | ✓ WIRED | `var token = _cancellationTokenSource.Token` passed to both (CombatLogsMonitor.cs:121-123). |
| CombatLogs static ctor | SecondSegmentOrNull | guarded projection skips names without '_' | ✓ WIRED | CombatLogs.cs:25 `.Select(x => SecondSegmentOrNull(x.Name))`. |
| GameObject/Ability/Action.Parse | GameObjectCache/ActionCache | ConcurrentDictionary.TryAdd first-writer-wins | ✓ WIRED | TryAdd at GameObject.cs:110, Ability.cs:21, Action.cs:55. |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
| --- | --- | --- | --- |
| Full test suite green, zero skips | `dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj` | Failed: 0, Passed: 72, Skipped: 0, Total: 72 | ✓ PASS |
| No bare numeric `.Parse` in source | grep `(int\|long\|ulong)\.Parse(` / `DateTime.Parse(` | 0 matches | ✓ PASS |
| No `Dictionary.Add` on caches | grep `.Add(` in Model | only local List adds | ✓ PASS |
| No standalone `FileShare.Read` | grep `FileShare.Read\b` | 0 matches | ✓ PASS |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
| --- | --- | --- | --- | --- |
| BUG-01 | 02-03 | Stop() cancels workers (observe linked token) | ✓ SATISFIED | Truth 4 |
| BUG-02 | 02-03 | Stop() before Start() does not throw | ✓ SATISFIED | Truth 5 |
| BUG-03 | 02-03 | DateTime/numeric parse uses InvariantCulture | ✓ SATISFIED | Truth 2 |
| BUG-04 | 02-03 | Static ctor tolerates filenames without '_' | ✓ SATISFIED | Truth 6 |
| BUG-05 | 02-01, 02-02 | Numeric parse paths skip malformed lines | ✓ SATISFIED | Truth 1 |
| BUG-06 | 02-02 | Shared caches thread-safe | ✓ SATISFIED | Truth 3 |
| BUG-07 | 02-03 | Combat-log files opened read-only | ✓ SATISFIED | Truth 7 |

All 7 phase requirement IDs claimed by plans match REQUIREMENTS.md Phase-2 mapping. No orphaned requirements.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
| --- | --- | --- | --- | --- |
| (none) | — | No TBD/FIXME/XXX/TODO debt markers in modified source; no stub returns flowing to render | — | — |

The `Action` ctor uses `?? throw new Exception(...)` (lines 11-13), but this is intentional and caught by the surrounding `try/catch` in `Action.Parse` (lines 50-64) which returns null — documented behavior (RESEARCH Pitfall 5), not a stub.

### Human Verification Required

#### 1. BUG-07 live-tailing (running SWTOR client)

**Test:** With the game running and actively writing a combat log, start the monitor.
**Expected:** New lines appear in the monitor AND the game is not blocked from writing (validates `FileAccess.Read` + `FileShare.ReadWrite` kept together).
**Why human:** Requires a live SWTOR client actively appending to the file; not reproducible hermetically.

#### 2. BUG-01 Stop() live cancellation

**Test:** Start the monitor, call Stop(), confirm background CPU/file activity ceases.
**Expected:** Worker loops observe the linked CTS cancellation and halt.
**Why human:** The full Start/Stop lifecycle assertion was intentionally deferred to Phase-3 TEST-01 (the singleton touches the real combat-logs directory, making a polled IsRunning assertion flaky/order-dependent). BUG-01 is verified by source inspection + the BUG-02 no-op test; live confirmation needs the running client.

### Gaps Summary

No blocking gaps. All 7 observable truths are VERIFIED in source, all required artifacts exist and are substantive and wired, and the full test suite passes 72/72 with zero skips and zero failures.

Two non-blocking human-verify items remain (BUG-01 and BUG-07 live behavior against the running SWTOR client). These are documented in 02-03-SUMMARY.md as deliberately deferred / manual-only, and the BUG-01 full lifecycle test is explicitly carried to Phase-3 TEST-01. Per the phase contract, these are the only outstanding items, so the phase goal is achieved in code and status is human_needed (not gaps_found).

---

_Verified: 2026-06-11_
_Verifier: Claude (gsd-verifier)_
