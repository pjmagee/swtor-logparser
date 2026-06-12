using SwtorLogParser.Monitor;

namespace SwtorLogParser.Model;

public class Value
{
    private string? _text;

    private Value(ReadOnlyMemory<char> memory)
    {
        Rom = memory;
    }

    private ReadOnlyMemory<char> Rom { get; }

    // BUG-260612-dso: damage-type/result ids VERIFIED against this repo's real logs. Detection is
    // keyed off the numeric {id} (locale-robust) instead of English-word substrings. AOT-safe:
    // a plain switch over const ulong literals — NO reflection, NO Dictionary, NO attributes.
    private const ulong EnergyId = 836045448940874;
    private const ulong KineticId = 836045448940873;
    private const ulong InternalId = 836045448940876;
    private const ulong ElementalId = 836045448940875;
    private const ulong ShieldId = 836045448945509;
    private const ulong AbsorbedId = 836045448945511;
    private const ulong MissId = 836045448945502;
    private const ulong ParryId = 836045448945503;
    private const ulong DeflectId = 836045448945508;
    private const ulong DodgeId = 836045448945505;
    private const ulong ImmuneId = 836045448945506;
    private const ulong ResistId = 836045448945507;

    public bool IsCritical => Rom.Span.Contains(CombatLogs.Critical.Span, StringComparison.OrdinalIgnoreCase);
    public bool IsCharges => Rom.Span.Contains(CombatLogs.Charges.Span, StringComparison.OrdinalIgnoreCase);

    // Damage TYPE: the FIRST {id} in the outer scope is the type on damage lines.
    public bool IsEnergy => TypeId == EnergyId;
    public bool IsKinetic => TypeId == KineticId;
    public bool IsElemental => TypeId == ElementalId;
    public bool IsInternal => TypeId == InternalId;

    // RESULT (avoid): the FIRST {id} is the result on avoid lines.
    public bool IsParry => TypeId == ParryId;
    public bool IsMiss => TypeId == MissId;
    public bool IsDodge => TypeId == DodgeId;
    public bool IsDeflect => TypeId == DeflectId;

    // Absorb lives in the NESTED modifier group: true only when an AbsorbedId appears beyond
    // the first {id} token (the first id is the damage type; a later id is absorbed).
    public bool IsAbsorbed => HasNestedAbsorbed();

    // Damage and absorbed are modeled as SEPARATE fields (mirrors the dubada01 reference): on a
    // shield line the OUTER number is the damage (Total) and the NESTED (n absorbed {…}) group
    // carries the absorbed amount. null when there is no nested absorbed group.
    public int? Absorbed => ExtractAbsorbed();

    public int Total => Integer.GetValueOrDefault();

    public int? Tilde => Rom.Span.Contains(CombatLogs.Tilde.Span, StringComparison.OrdinalIgnoreCase)
        ? ExtractTildeValue(Rom)
        : null;

    public int? Integer => ExtractFirstValue(Rom);
    public string? Text => _text ??= ExtractTextValue(Rom);

    public ulong? Id
    {
        get
        {
            var start = Rom.Span.IndexOf('{');
            var end = Rom.Span.IndexOf('}');

            if (start != -1 && end != -1)
                return ulong.TryParse(Rom.Span.Slice(start + 1, end - start - 1), out var id) ? id : (ulong?)null;

            return null;
        }
    }

    // The FIRST {id} token in the outer scope: the damage TYPE on damage lines, the RESULT on
    // avoid lines. Same brace-scan shape as the Id getter; null when no numeric {id} is present.
    private ulong? TypeId
    {
        get
        {
            var span = Rom.Span;
            var start = span.IndexOf('{');
            if (start == -1) return null;
            var rel = span.Slice(start + 1).IndexOf('}');
            if (rel == -1) return null;
            return ulong.TryParse(span.Slice(start + 1, rel), out var id) ? id : (ulong?)null;
        }
    }

    // true when any {id} token AFTER the first equals AbsorbedId. On a shield line the first id is
    // the damage type and a later (nested) id is absorbed; on a plain damage line there is only
    // the first token, so this is false.
    private bool HasNestedAbsorbed()
    {
        var span = Rom.Span;
        var first = span.IndexOf('{');
        if (first == -1) return false;

        // Start scanning after the first {id} token closes.
        var rel = span.Slice(first + 1).IndexOf('}');
        if (rel == -1) return false;
        var pos = first + 1 + rel + 1;

        while (pos < span.Length)
        {
            var open = span.Slice(pos).IndexOf('{');
            if (open == -1) return false;
            open += pos;
            var closeRel = span.Slice(open + 1).IndexOf('}');
            if (closeRel == -1) return false;
            if (ulong.TryParse(span.Slice(open + 1, closeRel), out var id) && id == AbsorbedId)
                return true;
            pos = open + 1 + closeRel + 1;
        }

        return false;
    }

    // The integer inside the NESTED (n absorbed {AbsorbedId}) group, else null. The nested group
    // is the LAST '(' inside the scope; confirm it carries AbsorbedId, then read its first integer.
    private int? ExtractAbsorbed()
    {
        var nestedOpen = Rom.Span.LastIndexOf('(');
        if (nestedOpen == -1) return null;

        var nestedCloseRel = Rom.Span.Slice(nestedOpen + 1).IndexOf(')');
        if (nestedCloseRel == -1) return null;

        var nested = Rom.Slice(nestedOpen + 1, nestedCloseRel);

        // Confirm this nested group is the absorbed modifier (carries AbsorbedId).
        var braceStart = nested.Span.IndexOf('{');
        if (braceStart == -1) return null;
        var braceRel = nested.Span.Slice(braceStart + 1).IndexOf('}');
        if (braceRel == -1) return null;
        if (!ulong.TryParse(nested.Span.Slice(braceStart + 1, braceRel), out var id) || id != AbsorbedId)
            return null;

        return ExtractFirstValue(nested);
    }

    public override string ToString()
    {
        return $"{Rom}";
    }

    public static Value? Parse(ReadOnlyMemory<char> rom)
    {
        var span = rom.Span;
        var lastSection = span.LastIndexOf(']');

        // BUG-260612-dso: depth-aware OUTER-group extraction. The old LastIndexOf('(')/
        // LastIndexOf(')') sliced the INNER nested group on a shield line
        // (133 energy {…} -shield {…} (149 absorbed {…})) -> Total=149. The damage is the
        // OUTER 133; the nested (149 absorbed {…}) is a separate absorbed amount (Value.Absorbed).
        //
        // 1. Find the FIRST '(' strictly after the final ']' (the value group follows the
        //    last [action] section — preserves the old `lastSection > start` intent).
        var open = -1;
        for (var i = lastSection + 1; i < span.Length; i++)
        {
            if (span[i] == '(')
            {
                open = i;
                break;
            }
        }

        if (open == -1) return null; // no value group (preserves old start == -1 null path)

        // 2. Walk forward tracking paren depth; the index where depth returns to 0 is the
        //    BALANCING close. If the string ends before depth balances, the group is malformed.
        var depth = 0;
        var close = -1;
        for (var i = open; i < span.Length; i++)
        {
            if (span[i] == '(') depth++;
            else if (span[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    close = i;
                    break;
                }
            }
        }

        if (close == -1) return null; // unbalanced parens -> malformed

        // 3. scope = content INSIDE the outer parens (still includes any nested
        //    (149 absorbed {…}) substring, which the Absorbed property reads).
        var scope = rom.Slice(open + 1, close - open - 1);

        return !scope.Span.StartsWith(CombatLogs.HeroEnginePrefix.Span, StringComparison.OrdinalIgnoreCase)
            ? new Value(scope)
            : null;
    }

    private static string? ExtractTextValue(ReadOnlyMemory<char> rom)
    {
        var start = -1;
        var end = -1;

        for (var i = 0; i < rom.Length; i++)
        {
            var c = rom.Span[i];

            if (char.IsLetter(c))
            {
                if (start == -1) start = i;

                end = i;
            }
        }

        if (start != -1)
            return rom.Slice(start, end - start + 1).Trim().ToString();

        return null;
    }

    private static int? ExtractFirstValue(ReadOnlyMemory<char> rom)
    {
        int? value = null;
        var index = 0;

        // Ignore any leading whitespace
        while (index < rom.Length && char.IsWhiteSpace(rom.Span[index])) index++;

        // Extract the digits until a non-digit character or '~' is encountered
        while (index < rom.Length && (char.IsDigit(rom.Span[index]) || rom.Span[index] == '~'))
        {
            if (rom.Span[index] == '~') break; // Stop extracting when '~' is encountered

            value = (value ?? 0) * 10 + (rom.Span[index] - '0');
            index++;
        }

        return value;
    }

    private static int? ExtractTildeValue(ReadOnlyMemory<char> rom)
    {
        int? value = null;
        var index = 0;

        // Find the '~' character, if present
        while (index < rom.Length && rom.Span[index] != '~') index++;

        // Ignore any characters until a non-digit character is encountered
        while (index < rom.Length && !char.IsDigit(rom.Span[index])) index++;

        // Extract the digits until a non-digit character is encountered
        while (index < rom.Length && char.IsDigit(rom.Span[index]))
        {
            value = (value ?? 0) * 10 + (rom.Span[index] - '0');
            index++;
        }

        return value;
    }
}