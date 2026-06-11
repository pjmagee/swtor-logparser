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

        // Separate AbilityCache (BoundedCache<Ability>) — no cross-type cast, no collision
        // with GameObjectCache under content keys.
        var key = rom.ToString();
        if (CombatLogs.AbilityCache.TryGetValue(key, out var cached))
            return cached;

        var ability = new Ability(rom);

        // GetOrAdd preserves the TryAdd race idiom: returns the race winner's instance.
        return CombatLogs.AbilityCache.GetOrAdd(key, ability);
    }
}
