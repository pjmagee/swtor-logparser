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

    public bool IsCritical => Rom.Span.Contains(CombatLogs.Critical.Span, StringComparison.OrdinalIgnoreCase);
    public bool IsCharges => Rom.Span.Contains(CombatLogs.Charges.Span, StringComparison.OrdinalIgnoreCase);

    public bool IsEnergy => Rom.Span.Contains(CombatLogs.Energy.Span, StringComparison.OrdinalIgnoreCase);
    public bool IsKinetic => Rom.Span.Contains(CombatLogs.Kinetic.Span, StringComparison.OrdinalIgnoreCase);
    public bool IsElemental => Rom.Span.Contains(CombatLogs.Elemental.Span, StringComparison.OrdinalIgnoreCase);
    public bool IsInternal => Rom.Span.Contains(CombatLogs.Internal.Span, StringComparison.OrdinalIgnoreCase);

    public bool IsAbsorbed => Rom.Span.Contains(CombatLogs.Absorbed.Span, StringComparison.OrdinalIgnoreCase);
    public bool IsParry => Rom.Span.Contains(CombatLogs.Parry.Span, StringComparison.OrdinalIgnoreCase);
    public bool IsMiss => Rom.Span.Contains(CombatLogs.Miss.Span, StringComparison.OrdinalIgnoreCase);
    public bool IsDodge => Rom.Span.Contains(CombatLogs.Dodge.Span, StringComparison.OrdinalIgnoreCase);
    public bool IsDeflect => Rom.Span.Contains(CombatLogs.Deflect.Span, StringComparison.OrdinalIgnoreCase);

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