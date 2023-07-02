namespace SwtorLogParser;

public class Action : IEquatable<Action>
{
    public static Action ApplyEffectDamage { get; } = Parse("ApplyEffect {836045448945477}: Damage {836045448945501}".AsMemory())!;
    public static Action ApplyEffectHeal { get; } = Parse("ApplyEffect {836045448945477}: Heal {836045448945500}".AsMemory())!;
    public static Action EventAbilityActivate { get; } = Parse("Event {836045448945472}: AbilityActivate {836045448945479}".AsMemory())!;
    
    public GameObject Event { get; }
    public GameObject Effect { get; }
    public override string ToString() => $"{Event}: {Effect}";
    private ReadOnlyMemory<char> Rom { get; }
    private int Splitter { get; }
    
    private Action(ReadOnlyMemory<char> rom)
    {
        Rom = rom;
        Splitter = Rom.Span.IndexOf(':');
        Event = GameObject.Parse(Rom.Slice(0, Splitter).Trim()) ?? throw new Exception("Event is null");
        Effect = GameObject.Parse(Rom.Slice(Splitter + 1, Rom.Span.Length - Splitter - 1).Trim()) ?? throw new Exception("Effect is null");
    }
	
    public static Action? Parse(ReadOnlyMemory<char> rom)
    {
        if (CombatLogs.ActionCache.TryGetValue(rom.GetHashCode(), out var value)) return (Action?)value;

        if (rom.Span.IndexOf(':') != -1)
        {
            try
            {
                var action = new Action(rom);
                CombatLogs.ActionCache.Add(action.GetHashCode(), action);
                return action;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
            }
        }

        return null;
    }

    public override int GetHashCode() => Rom.GetHashCode();

    public bool Equals(Action? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Rom.Span.SequenceEqual(other.Rom.Span);
    }
}