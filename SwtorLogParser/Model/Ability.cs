using SwtorLogParser.Monitor;

namespace SwtorLogParser.Model;

public class Ability : GameObject
{
    private Ability(ReadOnlyMemory<char> rom) : base(rom)
    {

    }

    public new static Ability? Parse(ReadOnlyMemory<char> rom)
    {
        if (rom.Length == 0 || rom.IsEmpty) return null;

        if (CombatLogs.GameObjectCache.TryGetValue(rom.GetHashCode(), out var value))
        {
            return (Ability?)value;
        }

        var ability = new Ability(rom);
        CombatLogs.GameObjectCache.Add(ability.GetHashCode(), ability);
        return ability;
    }
}