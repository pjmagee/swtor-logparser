namespace SwtorLogParser.Model;

public class Threat
{
    private Threat(ReadOnlyMemory<char> rom)
    {
        Rom = rom;
    }

    private ReadOnlyMemory<char> Rom { get; }

    public bool IsPositive => Value is >= 0;
    public bool IsNegative => Value is < 0;
    public int? Value => int.TryParse(Rom.Span, out var v) ? v : (int?)null;

    public override string ToString()
    {
        return $"{Rom}";
    }

    public static Threat? Parse(ReadOnlyMemory<char> rom)
    {
        if (rom.IsEmpty) return null;
        if (rom.Length < 3) return null;

        var start = rom.Span.LastIndexOf('<');
        var end = rom.Span.LastIndexOf('>');
        var exists = start > rom.Span.LastIndexOf(']');

        if (exists)
        {
            var scope = rom.Slice(start + 1, end - start - 1);

            if (scope.Span[0] != 'v')
                return new Threat(scope);
        }

        return null;
    }
}