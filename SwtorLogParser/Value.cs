namespace SwtorLogParser;

public class Value
{
    private ReadOnlyMemory<char> Rom { get; }

    public override string ToString() => $"{Rom}";

    private Value(ReadOnlyMemory<char> memory)
    {
        Rom = memory;
    }
	
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
	
    public int? Tilde
    {
        get 
        {
            if(Rom.Span.Contains(CombatLogs.Tilde.Span, StringComparison.OrdinalIgnoreCase))
                return ExtractTildeValue(Rom);
            return null;
        }
    }	

    public int? Integer => ExtractFirstValue(Rom);
	
    public ReadOnlyMemory<char>? Text => ExtractTextValue(Rom);

    public long? Id
    {
        get
        {
            var start = Rom.Span.IndexOf('{');
            var end = Rom.Span.IndexOf('}');

            if (start != -1)
                return long.Parse(Rom.Span.Slice(start + 1, end - start - 1));

            return null;
        }
    }

    public static Value? Parse(ReadOnlyMemory<char> rom)
    {
        var start = rom.Span.LastIndexOf('(');
        var end = rom.Span.LastIndexOf(')');

        var scope = rom.Slice(start + 1, end - start - 1);
			
        if (!scope.Span.StartsWith(CombatLogs.HeroEnginePrefix.Span, StringComparison.OrdinalIgnoreCase))
            return new Value(scope);
			
        return null;
    }

    private static ReadOnlyMemory<char>? ExtractTextValue(ReadOnlyMemory<char> rom)
    {
        int start = -1;
        int end = -1;

        for (int i = 0; i < rom.Length; i++)
        {
            char c = rom.Span[i];

            if (char.IsLetter(c))
            {
                if (start == -1)
                {
                    start = i;
                }

                end = i;
            }
        }

        if (start != -1)
            return rom.Slice(start, end - start + 1);

        return null;
    }

    private static int? ExtractFirstValue(ReadOnlyMemory<char> rom)
    {
        int? value = null;
        int index = 0;

        // Ignore any leading whitespace
        while (index < rom.Length && char.IsWhiteSpace(rom.Span[index]))
        {
            index++;
        }

        // Extract the digits until a non-digit character or '~' is encountered
        while (index < rom.Length && (char.IsDigit(rom.Span[index]) || rom.Span[index] == '~'))
        {
            if (rom.Span[index] == '~')
            {
                break; // Stop extracting when '~' is encountered
            }

            value = ((value ?? 0) * 10) + (rom.Span[index] - '0');
            index++;
        }

        return value;
    }

    private static int? ExtractTildeValue(ReadOnlyMemory<char> rom)
    {
        int? value = null;
        int index = 0;

        // Find the '~' character, if present
        while (index < rom.Length && rom.Span[index] != '~')
        {
            index++;
        }

        // Ignore any characters until a non-digit character is encountered
        while (index < rom.Length && !char.IsDigit(rom.Span[index]))
        {
            index++;
        }

        // Extract the digits until a non-digit character is encountered
        while (index < rom.Length && char.IsDigit(rom.Span[index]))
        {
            value = ((value ?? 0) * 10) + (rom.Span[index] - '0');
            index++;
        }

        return value;
    }
}