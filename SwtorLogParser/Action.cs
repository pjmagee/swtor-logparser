namespace SwtorLogParser;

public class Action
{
    private ReadOnlyMemory<char> Rom { get; }
    private int Splitter { get; }
    
    public GameObject Event { get; }
    public GameObject Effect { get; }

    public override string ToString() => $"{Event}: {Effect}";

    private Action(ReadOnlyMemory<char> rom)
    {
        Rom = rom;
        Splitter = Rom.Span.IndexOf(':');
        Event = GameObject.Parse(Rom.Slice(0, Splitter)) ?? throw new Exception("Event is null");
        Effect = GameObject.Parse(Rom.Slice(Splitter + 1, Rom.Span.Length - Splitter - 1)) ?? throw new Exception("Effect is null");
    }
	
    public static Action? Parse(ReadOnlyMemory<char> memory)
    {
        if (memory.Span.IndexOf(':') != -1)
        {
            try
            {
                return new Action(memory);
            }
            catch (Exception)
            {
                return null;
            }
        }
        
        return null;
    }
}