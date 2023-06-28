namespace SwtorLogParser;

public class Action
{
    public GameObject Event => new(Rom.Slice(0, Splitter));
    public GameObject Effect => new(Rom.Slice(Splitter + 1, Rom.Span.Length - Splitter - 1));

    private ReadOnlyMemory<char> Rom { get; }
    private int Splitter { get; }

    public override string ToString() => $"{Event}: {Effect}";

    private Action(ReadOnlyMemory<char> rom)
    {
        Rom = rom;
        Splitter = Rom.Span.IndexOf(':');
    }
	
    public static Action? Parse(ReadOnlyMemory<char> memory)
    {
        if (memory.Span.IndexOf(':') != -1) return new Action(memory);
        return null;
    }
}