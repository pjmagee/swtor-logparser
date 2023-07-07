using SwtorLogParser.Monitor;

namespace SwtorLogParser.Model;

public class GameObject : IEquatable<GameObject>
{
    protected ReadOnlyMemory<char> Rom
    {
        get;
    }

    private bool? _isNested;
    public bool IsNested => _isNested ??= Rom.Span.IndexOf('/') > -1;


    private string? _parentName;
    public string? ParentName => _parentName ??= GetParentName();
    private string? GetParentName()
    {
        if (IsNested)
        {
            var nameEnd = Rom.Span.IndexOf('{');
            if (nameEnd == -1) return null;
            var name = Rom.Span.Slice(0, nameEnd - 1).Trim();
            return name.Length > 0 ? name.ToString() : null;
        }

        return null;
    }

    public override int GetHashCode() => Rom.GetHashCode();

    private string? _name;
    public string? Name => _name ??= GetName();
    private string? GetName()
    {
        if (IsNested)
            return Rom.Span.Slice(Rom.Span.IndexOf('/') + 1, Rom.Span.LastIndexOf('{') - Rom.Span.IndexOf('/') - 1).Trim().ToString();

        var nameEnd = Rom.Span.IndexOf('{');
        if (nameEnd == -1) return null;
        var name = Rom.Span.Slice(0, nameEnd - 1).Trim();
        return name.Length > 0 ? name.ToString() : null;
    }

    private ulong? _parentId;
    public ulong? ParentId => _parentId ??= GetParentId();
    private ulong? GetParentId()
    {
        if (!IsNested) return null;
        var startIndex = Rom.Span.IndexOf('{');
        var endIndex = Rom.Span.IndexOf('}');

        return startIndex != -1 && endIndex != -1
            ? ulong.Parse(Rom.Span.Slice(startIndex + 1, endIndex - startIndex - 1))
            : null;
    }

    private ulong? _id;
    public ulong? Id => _id ??= GetId();
    private ulong? GetId()
    {
        if (IsNested)
        {
            var startIndex = Rom.Span.LastIndexOf('{');
            var endIndex = Rom.Span.LastIndexOf('}');

            if (startIndex != -1 && endIndex != -1)
                return ulong.Parse(Rom.Span.Slice(startIndex + 1, endIndex - startIndex - 1));
        }
        else
        {
            var startIndex = Rom.Span.IndexOf('{');
            var endIndex = Rom.Span.IndexOf('}');

            if (startIndex != -1 && endIndex != -1)
                return ulong.Parse(Rom.Span.Slice(startIndex + 1, endIndex - startIndex - 1));
        }

        return null;
    }

    protected GameObject(ReadOnlyMemory<char> rom)
    {
        Rom = rom;
    }

    public static GameObject? Parse(ReadOnlyMemory<char> rom)
    {
        if (CombatLogs.GameObjectCache.TryGetValue(rom.GetHashCode(), out var value))
            return (GameObject?)value;

        var gameObject = new GameObject(rom);
        if (gameObject.Id == null) return null;
        CombatLogs.GameObjectCache.Add(gameObject.GetHashCode(), gameObject);
        return gameObject;
    }

    public override string ToString() => $"{Name} {{{Id}}}";

    public bool Equals(GameObject? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return GetHashCode().Equals(other.GetHashCode());
    }
}