---
phase: quick-260612-dso
plan: 01
subsystem: core-parser
tags: [parser, value, absorb, dps-correctness, aot, locale]
requires: []
provides:
  - "Value.Parse OUTER-paren depth-aware scope (absorb/shield Total correctness)"
  - "id-based (numeric {id}) damage-type/result detection (locale-robust)"
  - "Value.Absorbed int? field (nested absorbed amount, separate from damage)"
affects:
  - "CombatLogsMonitor.CalculateDpsHpsStats (Total/IsEnergy now correct on absorb hits)"
tech-stack:
  added: []
  patterns: ["plain switch over const ulong id literals (AOT-safe, no reflection)"]
key-files:
  created: []
  modified:
    - SwtorLogParser/Model/Value.cs
    - SwtorLogParser/Monitor/CombatLogs.cs
    - SwtorLogParser.Tests/ValueTests.cs
decisions:
  - "APPROVED freeze exception: fix Value.Parse absorb/scope + id-based type detection now"
  - "~ effective-HPS deferred (OUT OF SCOPE); raw Total HPS behavior unchanged"
metrics:
  duration: ~15min
  completed: 2026-06-12
---

# Phase quick-260612-dso Plan 01: Value.Parse outer-paren absorb bug fix Summary

Fixed two confirmed core-parser correctness bugs: `Value.Parse` now selects the OUTER paren group via a depth-aware balancing scan (Total = outer damage 133, not inner absorbed 149), and damage-type/result classification is now keyed off the numeric `{id}` token instead of English-word substrings (locale-robust). Added a new `Value.Absorbed int?` field exposing the nested absorbed amount. Core library stays `IsAotCompatible=true` (plain `switch` over `const ulong` literals, no reflection).

## Tasks Completed

| Task | Name | Commit |
|------|------|--------|
| 1 | Depth-aware outer-paren scope in Value.Parse | `9a33d6c` |
| 2 | id-based damage-type/result detection + Absorbed field | `7bdee60` |
| 3 | Reference-verified regression tests + benchmark + STATE decision | `093381d` |

## What Changed

**BUG 1 (DPS correctness):** `Value.Parse` replaced `LastIndexOf('(')`/`LastIndexOf(')')` with a depth-aware scan: find the first `(` after the final `]`, walk paren depth to the balancing `)`. On a nested shield line the OUTER group is now the value scope, so `Total` is the outer damage. Unbalanced/malformed groups and the HeroEngine prefix and no-value-group cases still return null.

**BUG 2 (locale robustness):** `IsEnergy/IsKinetic/IsInternal/IsElemental` and `IsMiss/IsParry/IsDodge/IsDeflect` now derive from `TypeId` (the first `{id}` token) via a plain `switch` over `const ulong` reference ids. `IsAbsorbed` is true only when a `{id}` beyond the first equals `AbsorbedId`. New `int? Absorbed` reads the nested `(n absorbed {id})` amount (null when no nested absorbed group). `IsCritical`/`IsCharges`/`Tilde` remain char/word-keyed (not id-keyed in the log format).

**CombatLogs.cs:** Removed the now-dead English type/result needles (`Energy`, `Kinetic`, `Internal`, `Elemental`, `Parry`, `Miss`, `Dodge`, `Absorbed`, `Deflect`); kept `Critical`, `Charges`, `Tilde`, `HeroEnginePrefix`. Grep-confirmed zero remaining references to the removed needles.

## Tests

**Corrected to carry real `{id}` tokens** (mechanical, `// BUG-260612-dso` comment on each): `Energy_Is_Parsed`, `Kinetic_Is_Parsed`, `Internal_Is_Parsed`, `Elemental_Is_Parsed`, `Miss_Is_Parsed`, `Parry_Is_Parsed`, `Dodge_Is_Parsed`, `Deflect_Is_Parsed`. `Absorbed_Is_Parsed` rewritten to a real nested shield line asserting the outer/inner split.

**Unchanged (and still green):** `Zero_Is_Zero`, `Critical_Is_Parsed`, `Tilde_Is_Parsed`, `Charges_Is_Parsed`, `HeroEnginePrefix_Is_Not_Parsed`, `Value_Parse_Rejects_Cleanly`, `Value_NonNumeric_Id_Returns_Null`.

**Added regression tests:**
- `NestedAbsorb_OuterDamage_Is_Total_And_Absorbed_Is_Separate` → Total==133, IsEnergy, IsAbsorbed, Absorbed==149
- `CritAbsorb_OuterCrit_Damage_Is_Total` → Total==202, IsCritical, IsEnergy, Absorbed==226
- `SimpleDamage_NoShield_Has_No_Absorbed` → Total==133, IsEnergy, IsAbsorbed==false, Absorbed==null
- `Avoid_Miss_Total_Is_Zero` → Total==0, IsMiss
- `Heal_NoId_Total_Is_Parsed` → Total==513
- `LocaleRobustness_GarbledTypeWord_With_EnergyId_Is_Energy` → IsEnergy (id-keyed, not word-keyed)

**Results:** ValueTests 23/23 pass; full suite 116/116 pass (Release).

## Build / AOT

- `dotnet build SwtorLogParser -c Release` → 0 warnings, 0 errors.
- Native AOT CLI (`dotnet publish SwtorLogParser.Native.Cli -c Release`): IL/trim analysis phase emitted **0 IL2xxx/IL3xxx warnings** — AOT compatibility intact (the id map is a literal switch; no reflection added). The native *link* step fails in this Git-Bash shell because `link.exe`/`vswhere.exe` are not on PATH — an environment limitation unrelated to the change; the IL analysis (the AOT-correctness gate) passed clean.

## Benchmark Re-check

ShortRun harness (BenchmarkDotNet v0.15.8, .NET 10.0.9):

| Method | Mean | Allocated |
|--------|------|-----------|
| ParseAllLines (pure parse) | 1.450 ms | 954.69 KB |
| ParseAllLines_TouchAll | 2.016 ms | 1559.88 KB |
| ParseAllLines_HotCache | 1.752 ms | 1621.54 KB |

Allocation delta from the depth-aware scan ≈ 0: the new scope selection and id helpers are pure `ReadOnlySpan<char>` index walking (zero heap allocation), and the benchmark fixture contains no absorb lines, so the nested-absorbed extraction never fires on the hot path. Consistent with the prior 260612-czd baseline.

## Deviations from Plan

None of substance — plan executed as written. Note: the Native AOT *publish* could not complete the native link step in this shell environment (`link.exe` not resolvable); the AOT-correctness signal (0 IL2xxx/IL3xxx) was verified from the IL-analysis phase, which is the relevant gate.

## Out-of-scope working-tree note (not part of this task)

`SwtorLogParser/Model/CombatLogLine.cs` and `GameObject.cs` carry pre-existing IDE auto-format (whitespace/Allman-brace) reflows that were present in the working tree before this task and are unrelated to the absorb fix. Per the scope boundary they were NOT staged into any of this task's commits and remain uncommitted working-tree changes.

## Known Stubs

None.

## Self-Check: PASSED
- SwtorLogParser/Model/Value.cs — FOUND (contains `Absorbed`)
- SwtorLogParser.Tests/ValueTests.cs — FOUND (contains `836045448940874`)
- Commit 9a33d6c — FOUND
- Commit 7bdee60 — FOUND
- Commit 093381d — FOUND
