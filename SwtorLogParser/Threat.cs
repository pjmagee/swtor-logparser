namespace SwtorLogParser;

public class Threat
{
    private ReadOnlyMemory<char> Rom { get; }
	
    public override string ToString() => $"{Rom}";
	
    public bool IsPositive => Value >= 0;
	
    public bool IsNegative => Value < 0;
    public int Value => int.Parse(Rom.Span);

    private Threat(ReadOnlyMemory<char> rom)
    {
        this.Rom = rom;		
    }

    public static Threat? Parse(ReadOnlyMemory<char> rom)
    {
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