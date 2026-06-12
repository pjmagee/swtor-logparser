---
phase: quick-260612-dso
verified: 2026-06-12T00:00:00Z
status: passed
score: 5/5 must-haves verified
overrides_applied: 0
---

# Quick Task 260612-dso: Value.Parse Outer-Paren Absorb Bug Fix — Verification Report

**Task Goal:** Fix two confirmed correctness bugs in the core parser — (1) `Value.Parse` must take the OUTER paren group on nested absorb/shield lines so `Value.Total` is the real damage (133), not the inner absorbed amount (149); (2) damage type determined by numeric `{id}`, not English substring. Add `Value.Absorbed` field. Keep core lib `IsAotCompatible`. All tests green.
**Verified:** 2026-06-12
**Status:** passed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
| --- | --- | --- | --- |
| 1 | `Value.Total` returns the OUTER damage (133) on nested absorb lines, not inner absorbed (149) | ✓ VERIFIED | `Value.Parse` (Value.cs:151-202) is depth-aware: finds first `(` after last `]` (lines 163-171), walks paren depth to the balancing `)` (lines 177-191), slices the outer scope (line 197). NO `LastIndexOf('(')`/`LastIndexOf(')')` for scope selection. Test `NestedAbsorb_OuterDamage_Is_Total_And_Absorbed_Is_Separate` asserts `Total==133` (ValueTests.cs:184). PASSED. |
| 2 | Type/result determined by numeric `{id}`, not English substring | ✓ VERIFIED | `TypeId` (Value.cs:81-92) reads the first `{id}`; all type/result bools compare it to `const ulong` literals (lines 36-45). `LocaleRobustness_GarbledTypeWord_With_EnergyId_Is_Energy` proves `(133 xxxxx {836045448940874})` → `IsEnergy==true` (ValueTests.cs:235-243). PASSED. |
| 3 | Nested shield absorbed amount exposed as separate `int? Absorbed` field | ✓ VERIFIED | `public int? Absorbed => ExtractAbsorbed()` (Value.cs:54, 125-144). Test asserts `Total==133` AND `Absorbed==149` as distinct fields (ValueTests.cs:184-187); simple-damage line → `Absorbed==null` (ValueTests.cs:212). PASSED. |
| 4 | Core lib stays `IsAotCompatible` — id→type mapping is a plain switch, NO reflection | ✓ VERIFIED | `IsAotCompatible=true` in csproj (line 6). Value.cs contains NO `Dictionary`/`Reflection`/`typeof`/`Activator`/`Attribute` (only a comment match). Mapping is `==` over `private const ulong` literals (lines 19-30, 36-45). Test build under net10.0 Release succeeded. |
| 5 | Non-absorb existing tests pass; classification + absorb expectations reflect fixed semantics | ✓ VERIFIED | `dotnet test -c Release` → 116/116 passed. Classification tests carry real `{id}` tokens (ValueTests.cs:103-144); unchanged tests (`Zero_Is_Zero`, `Critical_Is_Parsed`, `Tilde_Is_Parsed`, `Charges_Is_Parsed`, `HeroEnginePrefix_Is_Not_Parsed`, etc.) remain and pass. |

**Score:** 5/5 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
| --- | --- | --- | --- |
| `SwtorLogParser/Model/Value.cs` | Depth-aware outer-paren scope; id-keyed properties; `Absorbed` field | ✓ VERIFIED | Contains `Absorbed` (lines 49, 54, 125). Depth-aware `Parse`, `TypeId`, `HasNestedAbsorbed`, `ExtractAbsorbed` all present and substantive. |
| `SwtorLogParser.Tests/ValueTests.cs` | Reference-verified regression tests locking corrected semantics | ✓ VERIFIED | Contains `836045448940874` and all required ids; 6 new regression tests (nested absorb, crit absorb, simple damage, avoid, heal, locale-robustness) present and passing. |
| `SwtorLogParser/Monitor/CombatLogs.cs` | Dead English needles removed; Critical/Charges/Tilde/HeroEnginePrefix kept | ✓ VERIFIED | `Energy`/`Kinetic`/`Internal`/`Elemental`/`Parry`/`Miss`/`Dodge`/`Absorbed`/`Deflect` needles removed (grep for `<Name> { get` → no matches). `Charges`, `Critical`, `Tilde`, `HeroEnginePrefix` retained (lines 44-47). |

### Id Constant Verification

| Meaning | Required Id | Value.cs Constant | Match |
| --- | --- | --- | --- |
| energy | 836045448940874 | EnergyId (line 19) | ✓ |
| kinetic | 836045448940873 | KineticId (line 20) | ✓ |
| internal | 836045448940876 | InternalId (line 21) | ✓ |
| elemental | 836045448940875 | ElementalId (line 22) | ✓ |
| absorbed | 836045448945511 | AbsorbedId (line 24) | ✓ |
| miss | 836045448945502 | MissId (line 25) | ✓ |
| parry | 836045448945503 | ParryId (line 26) | ✓ |
| deflect | 836045448945508 | DeflectId (line 27) | ✓ |
| dodge | 836045448945505 | DodgeId (line 28) | ✓ |

All 9 ids match exactly.

### Key Link Verification

| From | To | Via | Status | Details |
| --- | --- | --- | --- | --- |
| `CombatLogLine.cs` | `Value.cs` | `Value.Parse(Rom)` in lazy `Value` property | ✓ WIRED | `_value = Value.Parse(Rom)` at CombatLogLine.cs:100 |
| `CombatLogsMonitor.cs` | `Value.cs` | `line.Value!.Total` / `line.Value!.IsCritical` in `CalculateDpsHpsStats` | ✓ WIRED | Total at lines 132,140; IsCritical at lines 133,141 — unchanged public surface; corrected `Total` flows through on absorb lines (intended). |

### Consumer Sanity Check

Grep across `**/*.cs` for `IsEnergy`/`IsAbsorbed`/`.Absorbed`/`.Total` usage: no production host (CLI, Native CLI, Overlay) consumes the changed type/absorb semantics directly — they reach DPS only through `Total`/`IsCritical` in `CombatLogsMonitor`, exactly as the plan intends. `IsEnergy`/`Absorbed`/etc. are referenced only by `Value.cs` and `ValueTests.cs`. No consumer missed.

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
| --- | --- | --- | --- |
| Full test suite green | `dotnet test -c Release` | Failed: 0, Passed: 116, Total: 116 | ✓ PASS |

### Anti-Patterns Found

None. No `TODO`/`FIXME`/`TBD`/`XXX` debt markers in the modified files. No reflection, Dictionary, or attribute-based dispatch introduced. `ExtractAbsorbed` legitimately uses `LastIndexOf('(')` to locate the NESTED group (not the outer scope) — this is correct per plan Task 2 step 5 and is covered by passing tests.

### Gaps Summary

No gaps. Both bugs are closed: BUG 1 (outer-paren scope → `Total==133`) and BUG 2 (id-keyed type/result detection). The new `int? Absorbed` field exposes the nested absorbed amount as a distinct field. Core library remains `IsAotCompatible` with a plain `const ulong`/`==` mapping. All 116 tests pass in Release. Key links to `CombatLogLine` and `CombatLogsMonitor` are intact, and no consumer of the changed semantics was missed.

---

_Verified: 2026-06-12_
_Verifier: Claude (gsd-verifier)_
