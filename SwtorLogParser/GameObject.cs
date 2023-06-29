namespace SwtorLogParser;

public class GameObject
{
    private ReadOnlyMemory<char> Rom { get; }
    
    public bool IsNested => Rom.Span.IndexOf('/') > -1;

    private string? _name;
    public string? Name => _name ??= GetName();
    private string? GetName()
    {
        if (IsNested) return Rom.Span.Slice(Rom.Span.IndexOf('/') + 1, Rom.Span.LastIndexOf('{') - Rom.Span.IndexOf('/') - 1).Trim().ToString();
        if (Rom.Length <= 0) return null;

        var nameEnd = Rom.Span.IndexOf('{');
        if (nameEnd == -1) return null;
        var name = Rom.Span.Slice(0, nameEnd - 1).Trim();
        return name.Length > 0 ? name.ToString() : null;
    }

    private long? _id;
    public long? Id => _id ??= GetId();
    
    private long? GetId()
    {
        if (IsNested)
        {
            var startIndex = Rom.Span.LastIndexOf('{');
            var endIndex = Rom.Span.LastIndexOf('}');
            
            if (startIndex != -1 && endIndex != -1)
                return long.Parse(Rom.Span.Slice(startIndex + 1, endIndex - startIndex - 1));
        }

        if (Rom.Length > 0)
        {
            var startIndex = Rom.Span.IndexOf('{');
            var endIndex = Rom.Span.IndexOf('}');
            
            if (startIndex != -1 && endIndex != -1)
                return long.Parse(Rom.Span.Slice(startIndex + 1, endIndex - startIndex - 1));
        }

        return null;
    }

    protected GameObject(ReadOnlyMemory<char> rom)
    {
        Rom = rom;
    }
    
    public static GameObject? Parse(ReadOnlyMemory<char> rom)
    {
        var gameObject = new GameObject(rom);
        if (gameObject.Id == null) return null;
        return gameObject;
    }

    public override string ToString() => $"{Name} {{{Id}}}";
}