using SwtorLogParser.Monitor;

namespace SwtorLogParser.Model;

public class Ability : GameObject
{
    private Ability(ReadOnlyMemory<char> rom)
        : base(rom) { }

    public static new Ability? Parse(ReadOnlyMemory<char> rom)
    {
        if (rom.Length == 0 || rom.IsEmpty)
            return null;

        if (CombatLogs.GameObjectCache.TryGetValue(rom.GetHashCode(), out var value))
            return (Ability?)value;

        var ability = new Ability(rom);

        var key = ability.GetHashCode();
        if (CombatLogs.GameObjectCache.TryAdd(key, ability))
            return ability;

        // Another thread won the race for this key — return the cached instance.
        return CombatLogs.GameObjectCache.TryGetValue(key, out var existing) ? (Ability?)existing : ability;
    }
}
