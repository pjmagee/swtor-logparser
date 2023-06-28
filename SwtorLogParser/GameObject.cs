namespace SwtorLogParser;

public class GameObject
{
    private ReadOnlyMemory<char> Rom { get; }
    public bool IsNested => Rom.Span.IndexOf('/') > -1;

    public virtual string? Name
    {
        get
        {
            if (IsNested) return Rom.Span.Slice(Rom.Span.IndexOf('/') + 1, Rom.Span.LastIndexOf('{') - Rom.Span.IndexOf('/') - 1).Trim().ToString();
            if (Rom.Length > 0)
            {
                var name = Rom.Span.Slice(0, Rom.Span.IndexOf('{') - 1).Trim();
                if (name.Length > 0) return name.ToString();
            }
            return null;
        }
    }

    public virtual long? Id
    {
        get
        {
            if (IsNested)
            {
                var startIndex = Rom.Span.LastIndexOf('{');
                var endIndex = Rom.Span.LastIndexOf('}');
                return long.Parse(Rom.Span.Slice(startIndex + 1, endIndex - startIndex - 1));
            }

            if (Rom.Length > 0)
            {
                var startIndex = Rom.Span.IndexOf('{');
                var endIndex = Rom.Span.IndexOf('}');
                return long.Parse(Rom.Span.Slice(startIndex + 1, endIndex - startIndex - 1));
            }

            return null;
        }
    }

    public GameObject(ReadOnlyMemory<char> rom)
    {
        Rom = rom;
    }

    public override string ToString() => string.Format("{0} {{{1}}}", Name, Id);
}