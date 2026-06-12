---
phase: quick-260612-dso
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - SwtorLogParser/Model/Value.cs
  - SwtorLogParser/Monitor/CombatLogs.cs
  - SwtorLogParser.Tests/ValueTests.cs
  - .planning/STATE.md
autonomous: true
requirements: [QUICK-260612-dso]

must_haves:
  truths:
    - "Value.Total returns the OUTER damage number (133) on nested absorb/shield lines, not the inner absorbed amount (149)"
    - "Damage type (IsEnergy/IsKinetic/IsInternal/IsElemental) and result (IsMiss/IsParry/IsDodge/IsDeflect/IsAbsorbed) are determined by the numeric {id} token, not an English-word substring match"
    - "The absorbed amount on a nested shield line is exposed as a separate Value.Absorbed int? field (133 damage + 149 absorbed are distinct fields)"
    - "Core library SwtorLogParser stays IsAotCompatible=true — the id->type mapping is a plain switch with NO reflection"
    - "All non-absorb existing ValueTests pass (corrected only to carry real {id} tokens); absorb/type expectations reflect the fixed semantics"
  artifacts:
    - path: "SwtorLogParser/Model/Value.cs"
      provides: "Depth-aware outer-paren scope in Parse; id-keyed type/result properties; Absorbed field"
      contains: "Absorbed"
    - path: "SwtorLogParser.Tests/ValueTests.cs"
      provides: "Reference-verified regression tests locking the corrected absorb + id-based semantics"
      contains: "836045448940874"
  key_links:
    - from: "SwtorLogParser/Model/CombatLogLine.cs"
      to: "SwtorLogParser/Model/Value.cs"
      via: "Value.Parse(Rom) in the lazy Value property"
      pattern: "Value\\.Parse\\(Rom\\)"
    - from: "SwtorLogParser/Monitor/CombatLogsMonitor.cs"
      to: "SwtorLogParser/Model/Value.cs"
      via: "line.Value!.Total / line.Value!.IsCritical in CalculateDpsHpsStats"
      pattern: "Value!\\.(Total|IsCritical)"
---

<objective>
Fix two confirmed correctness bugs in the FROZEN core parser's `Value.Parse`, validated against the dubada01/SWTORCombatParser reference. This is an APPROVED, scoped exception to the v1.1 "core parser FROZEN" decision — the user explicitly chose to fix now. It intentionally CHANGES live DPS output for absorb/shield hits; that is the correction.

BUG 1 — `Value.Parse` uses `LastIndexOf('(')` / `LastIndexOf(')')`, so on a nested absorb/shield line it slices the INNER `(149 absorbed {…})` group → Total=149, IsEnergy=false. WRONG. The damage is the OUTER `133 energy`; 149 is a separate absorbed amount.

BUG 2 — Damage type/result are detected by English-word substring (`Contains("energy")`, the `CombatLogs` needle table). Breaks on non-English clients. The stable signal is the numeric `{id}`.

Purpose: Keep the live DPS/HPS pipeline correct on absorb/shield hits and make type detection locale-robust, without breaking AOT compatibility or the rest of the parser.
Output: Fixed `Value.cs` (depth-aware outer scope + id-keyed properties + `Absorbed` field), pruned dead needles in `CombatLogs.cs`, reference-verified regression tests, a benchmark re-check note, and a STATE.md freeze-exception decision line.
</objective>

<execution_context>
@D:/Projects/pjmagee/swtor-logparser/.claude/gsd-core/workflows/execute-plan.md
@D:/Projects/pjmagee/swtor-logparser/.claude/gsd-core/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@CLAUDE.md
@SwtorLogParser/Model/Value.cs
@SwtorLogParser/Model/CombatLogLine.cs
@SwtorLogParser/Monitor/CombatLogs.cs
@SwtorLogParser/Monitor/CombatLogsMonitor.cs
@SwtorLogParser/Extensions/CombatLogLineExtensions.cs
@SwtorLogParser.Tests/ValueTests.cs
</context>

<constraints>
- Target net10.0 (CLAUDE.md note overrides the older net8.0 text). Core library SwtorLogParser MUST stay IsAotCompatible=true — the id->type mapping is a plain `switch`/`if` over `ulong`, NO reflection, NO dictionaries-of-delegates, NO attributes.
- The "behave identically" hardening rule is DELIBERATELY LIFTED for the absorb/type semantics ONLY. Every other parser behavior (Total on simple lines, Tilde, Charges, Critical, Threat, Id, HeroEngine rejection, null-on-empty) stays identical.
- OUT OF SCOPE (do NOT touch, note as deferred follow-up): the `~` effective-value (raw-vs-effective HPS) question. Keep current raw `Total` behavior in `CalculateDpsHpsStats`.
- No ROADMAP changes.
- Preserve the public signatures of every existing `Value` bool property (IsEnergy/IsKinetic/IsInternal/IsElemental/IsAbsorbed/IsParry/IsMiss/IsDodge/IsDeflect/IsCritical/IsCharges) — UI/tests may bind to them. `Absorbed` is the only new public member.
</constraints>

<reference_ids>
Damage-type and result ids VERIFIED against this repo's real logs (use exactly these `ulong` constants in the switch):

| Meaning   | Id (ulong)        | Property          |
|-----------|-------------------|-------------------|
| energy    | 836045448940874   | IsEnergy          |
| kinetic   | 836045448940873   | IsKinetic         |
| internal  | 836045448940876   | IsInternal        |
| elemental | 836045448940875   | IsElemental       |
| shield    | 836045448945509   | (modifier marker) |
| absorbed  | 836045448945511   | IsAbsorbed/Absorbed |
| miss      | 836045448945502   | IsMiss            |
| parry     | 836045448945503   | IsParry           |
| deflect   | 836045448945508   | IsDeflect         |
| dodge     | 836045448945505   | IsDodge           |
| immune    | 836045448945506   | (no property)     |
| resist    | 836045448945507   | (no property)     |

Token layout in the OUTER scope:
- Damage: `133 energy {836045448940874}` → first `{id}` = the damage TYPE id.
- Avoid:  `0 -miss {836045448945502}` → first `{id}` = the RESULT id.
- Shielded: `133 energy {…940874} -shield {…945509} (149 absorbed {…945511})` → first id = type (energy); absorbed id + amount live in the NESTED group beyond the first token.
</reference_ids>

<tasks>

<task type="auto">
  <name>Task 1: Depth-aware outer-paren scope in Value.Parse (the DPS-correctness fix)</name>
  <files>SwtorLogParser/Model/Value.cs</files>
  <action>
Replace the `LastIndexOf`-based scope selection in `Value.Parse` (currently `Value.cs:58-71`) with a DEPTH-AWARE outer-group extraction so the value scope is the OUTER `(…)` group, which may itself CONTAIN a nested `(…)`.

Algorithm:
1. Compute `lastSection = rom.Span.LastIndexOf(']')` (end of the final `[action]` section). Preserve the existing intent of the old `lastSection > start` guard: the value group must come AFTER the last `]`.
2. Find the FIRST `'('` at an index strictly greater than `lastSection` — call it `open`. If none, return null (no value group — preserves the old `start == -1` null path).
3. Walk forward from `open` tracking paren depth (`+1` on `'('`, `-1` on `')'`). The index where depth returns to 0 is the BALANCING `close`. If the string ends before depth returns to 0 (malformed), return null.
4. `scope = rom.Slice(open + 1, close - open - 1)` — the content INSIDE the outer parens (this still includes any nested `(149 absorbed {…})` substring, which Task 2 reads for Absorbed).
5. Preserve the HeroEngine rejection exactly: if `scope.Span.StartsWith(CombatLogs.HeroEnginePrefix.Span, OrdinalIgnoreCase)` return null (covers `(he4000)` / `(he)`).
6. Return `new Value(scope)`.

WHY depth-aware (not naive first-'(' to last-')'): the outer `')'` is the balancing one. For the single-level nesting seen in real logs a first-'(' / last-')' span would coincide, but depth-aware is robust to any future deeper nesting and reads as obviously correct. Use depth-aware.

Do NOT change `ExtractFirstValue`, `ExtractTildeValue`, `ExtractTextValue`, `Integer`, `Total`, `Tilde`, `Id`, or `ToString`. `Total` still = `Integer.GetValueOrDefault()` and `Integer` still = `ExtractFirstValue(Rom)` over the (now OUTER) scope → first integer, stops at space/`*`/`~`, yielding 133 not 149. `IsCritical` still = `Contains('*')` over the outer scope (the `*` attaches to the outer damage number, e.g. `202* energy … (226 absorbed)`).
  </action>
  <verify>
    <automated>dotnet build SwtorLogParser/SwtorLogParser.csproj -c Release</automated>
  </verify>
  <done>Value.Parse selects the OUTER paren group via depth-aware balancing scan; HeroEngine prefix and no-value-group cases still return null; Integer/Total/Tilde/IsCritical helpers unchanged; SwtorLogParser builds clean in Release (AOT-compatible, no reflection introduced).</done>
</task>

<task type="auto">
  <name>Task 2: id-based damage-type/result detection + Absorbed field (locale-robust)</name>
  <files>SwtorLogParser/Model/Value.cs, SwtorLogParser/Monitor/CombatLogs.cs</files>
  <action>
Reimplement the type/result detection in `Value.cs` off the numeric `{id}` instead of English substrings, keeping every public property signature.

1. Add a private helper that returns the FIRST `{id}` token in the outer scope as a `ulong?` (the type/result id). Reuse the same brace-scan shape as the existing `Id` getter (`IndexOf('{')` / matching `'}'` from that point, `ulong.TryParse`). Name it e.g. `TypeId`. This is the id of the FIRST `{…}` — the damage TYPE on damage lines, the RESULT on avoid lines.
2. Add a private static AOT-safe mapper: a plain `switch` (or `if`-chain) over the `ulong` id returning an internal enum or set of bools. NO reflection, NO `Dictionary`, NO delegates — a literal `switch (id) { case 836045448940874: … }` keyed on the constants in `<reference_ids>`. Define the ids as `private const ulong` fields with descriptive names (EnergyId, KineticId, InternalId, ElementalId, ShieldId, AbsorbedId, MissId, ParryId, DeflectId, DodgeId, ImmuneId, ResistId).
3. Reimplement the bool properties off `TypeId`:
   - `IsEnergy => TypeId == EnergyId`, `IsKinetic => TypeId == KineticId`, `IsInternal => TypeId == InternalId`, `IsElemental => TypeId == ElementalId`.
   - `IsMiss => TypeId == MissId`, `IsParry => TypeId == ParryId`, `IsDodge => TypeId == DodgeId`, `IsDeflect => TypeId == DeflectId`.
   Keep their public `bool` signatures unchanged.
4. `IsAbsorbed`: true when the scope contains the AbsorbedId (836045448945511) in the NESTED modifier group — i.e. anywhere beyond the first `{id}` token. Simplest robust check: scan all `{id}` tokens in the scope and return true if any equals AbsorbedId AND it is not the first token. (On a real shield line the first id is energy and a later id is absorbed, so this is true; on a plain `(133 energy {…940874})` line there is only the first token → false.)
5. Add a new public `int? Absorbed` property: the integer inside the NESTED `(<n> absorbed {836045448945511})` group, else null. Locate the nested group as the LAST `'('` inside the scope (depth ≥ 1 region); confirm its content contains AbsorbedId; extract the first integer in that nested group via the existing `ExtractFirstValue` over the nested slice. If there is no nested absorbed group, return null. Document with a brief comment that damage and absorbed are modeled as separate fields (mirrors the reference).
6. Keep `IsCritical` (`Contains('*')`), `IsCharges` (`Contains("charges")`), `Tilde` (`~`) exactly as-is — these are NOT id-keyed in the log format and stay word/char-keyed.

In `CombatLogs.cs`: the English needle ROMs for damage TYPE and RESULT are now dead. REMOVE these now-unused fields: `Energy`, `Kinetic`, `Internal`, `Elemental`, `Parry`, `Miss`, `Dodge`, `Absorbed`, `Deflect`. KEEP these (still referenced by Value): `Critical` (`*`), `Charges` (`charges`), `Tilde` (`~`), `HeroEnginePrefix` (`he`). After removal, grep-confirm no remaining references to the removed needles anywhere in the solution before relying on the build.
  </action>
  <verify>
    <automated>dotnet build SwtorLogParser/SwtorLogParser.csproj -c Release 2>&1 | grep -v '^#' | grep -ci 'error' | grep -qx 0 && echo TYPE_FIX_BUILDS</automated>
  </verify>
  <done>IsEnergy/IsKinetic/IsInternal/IsElemental and IsMiss/IsParry/IsDodge/IsDeflect derive from the first {id} via a plain switch; IsAbsorbed is true only when the nested group carries AbsorbedId; new int? Absorbed returns the nested absorbed amount (else null); dead type/result needles removed from CombatLogs.cs while Critical/Charges/Tilde/HeroEnginePrefix remain; SwtorLogParser builds clean in Release with no reflection added.</done>
</task>

<task type="auto">
  <name>Task 3: Reference-verified regression tests + benchmark re-check + STATE decision line</name>
  <files>SwtorLogParser.Tests/ValueTests.cs, .planning/STATE.md</files>
  <action>
Update `ValueTests.cs` to lock the CORRECTED semantics. The existing type/result tests use bare strings like `(123 energy)` / `(0 -miss)` with NO `{id}` — under id-based detection those would no longer be classified. This is the expected consequence of BUG 2's fix: CORRECT each such test to carry the real `{id}` token (mechanical update, add a one-line comment `// BUG-260612-dso: type now keyed off {id}`), do NOT delete them and do NOT preserve the id-less form. Tests that do not depend on type/result classification (`Zero_Is_Zero`, `Critical_Is_Parsed`, `Tilde_Is_Parsed`, `Charges_Is_Parsed`, `HeroEnginePrefix_Is_Not_Parsed`, `Value_Parse_Rejects_Cleanly`, `Value_NonNumeric_Id_Returns_Null`) MUST remain unchanged and pass.

Concretely, rewrite the classification tests to include ids and assert the corrected fields:
- `Energy_Is_Parsed`: `(123 energy {836045448940874})` → Integer==123, IsEnergy==true.
- `Kinetic_Is_Parsed`: `(123 kinetic {836045448940873})` → IsKinetic==true.
- `Internal_Is_Parsed`: `(123 internal {836045448940876})` → IsInternal==true.
- `Elemental_Is_Parsed`: `(123 elemental {836045448940875})` → IsElemental==true.
- `Miss_Is_Parsed`: `(0 -miss {836045448945502})` → Integer==0, IsMiss==true.
- `Parry_Is_Parsed`: `(123 -parry {836045448945503})` → IsParry==true.
- `Dodge_Is_Parsed`: `(123 -dodge {836045448945505})` → IsDodge==true.
- `Deflect_Is_Parsed`: `(123 -deflect {836045448945508})` → IsDeflect==true.
- `Absorbed_Is_Parsed`: REPLACE the old `(123 absorbed)` shape with a real nested shield line and assert the FIXED outer/inner split (see new tests below); the old "Integer==123 && IsAbsorbed" on a flat string encoded the bug.

ADD these reference-verified regression tests (these are the headline lock for both bugs). Use the full outer-scope tails as they appear after the `[action]` section:
- Nested absorb (BUG 1 + BUG 2): scope tail `(133 energy {836045448940874} -shield {836045448945509} (149 absorbed {836045448945511}))` → Total==133, IsEnergy==true, IsAbsorbed==true, Absorbed==149.
- Crit absorb: `(202* energy {836045448940874} -shield {836045448945509} (226 absorbed {836045448945511}))` → Total==202, IsCritical==true, IsEnergy==true, Absorbed==226.
- Simple damage (no shield): `(133 energy {836045448940874})` → Total==133, IsEnergy==true, IsAbsorbed==false, Absorbed==null.
- Avoid: `(0 -miss {836045448945502})` → Total==0, IsMiss==true.
- Heal (no id): `(513)` → Total==513.
- Locale-robustness (proves id-keyed not word-keyed): a synthetic line with the energy id but a garbled/non-English type word, e.g. `(133 xxxxx {836045448940874})` → IsEnergy==true.

You may pass either the full `[...] [...] [...] [...] [action]] (scope) <threat>` line to `Value.Parse`, or the parens-bearing tail directly, as long as `Value.Parse`'s `lastSection`/depth logic resolves the same outer scope; prefer realistic full lines for the nested cases so the `lastSection` guard is exercised, and verify the chosen form selects the OUTER group.

Then:
- Run the full test suite: every non-classification test unchanged + green, classification tests corrected + green, new regression tests green.
- Re-run the existing benchmark harness (`dotnet run --project SwtorLogParser.Benchmarks -c Release`, ShortRunJob) to confirm the depth-aware scan adds no meaningful allocation/timing regression; record a one-line note (bytes/op delta ≈ 0) in the task SUMMARY. The fixture has no absorb lines, so this measures the scan-shape change on the hot path only — expected negligible.
- Append a decision line to `.planning/STATE.md` under `### Decisions` documenting the APPROVED freeze exception, e.g.: "[quick-260612-dso] APPROVED exception to the FROZEN-core-parser decision: fixed Value.Parse outer-paren scope (absorb/shield Total bug) + switched damage-type detection to numeric {id}; this intentionally changes live DPS for absorb/shield hits. `~` effective-HPS remains OUT OF SCOPE (deferred)." (The orchestrator commits STATE.md.)
  </action>
  <verify>
    <automated>dotnet test SwtorLogParser.Tests/SwtorLogParser.Tests.csproj -c Release --filter "FullyQualifiedName~ValueTests"</automated>
  </verify>
  <done>ValueTests: classification tests corrected to carry real {id} tokens and pass; new nested-absorb, crit-absorb, simple-damage, avoid, heal, and locale-robustness tests assert the corrected semantics and pass; all unrelated tests still pass unchanged; benchmark re-run shows negligible allocation delta (noted in SUMMARY); STATE.md carries the freeze-exception decision line.</done>
</task>

</tasks>

<verification>
- `dotnet build -c Release` over the solution succeeds; SwtorLogParser stays IsAotCompatible (no reflection added — id mapping is a literal `switch`).
- `dotnet test -c Release` is fully green: corrected classification tests + new regression tests + all previously-passing tests.
- Manual scan: `Value.Parse` no longer calls `LastIndexOf('(')`/`LastIndexOf(')')`; it walks paren depth. `CombatLogs.cs` no longer defines the dead type/result needles; `Critical`/`Charges`/`Tilde`/`HeroEnginePrefix` remain.
- DPS consumers (`CombatLogsMonitor.CalculateDpsHpsStats`, `CombatLogLineExtensions`) are unmodified and compile against the unchanged public surface (`Total`, `IsCritical`); the only behavioral change reaches them through corrected `Total`/`IsEnergy` values on absorb lines (intended).
</verification>

<success_criteria>
- Nested absorb/shield line yields Total==133 (outer damage), not 149 (inner absorbed). [BUG 1 closed]
- Damage type/result classified by `{id}` switch; a garbled type word with the energy id still reports IsEnergy. [BUG 2 closed]
- `Value.Absorbed` exposes the nested absorbed amount (149/226) as a separate int? field; null when no nested absorbed group.
- Core library remains AOT-compatible (plain switch, no reflection).
- All non-absorb existing tests pass unchanged; classification tests corrected to id form; new reference tests green.
- `~` effective-HPS explicitly deferred; raw `Total` HPS behavior unchanged.
</success_criteria>

<output>
Create `.planning/quick/260612-dso-fix-value-parse-outer-paren-absorb-bug-i/260612-dso-SUMMARY.md` when done.
</output>
