namespace SwtorLogParser;

public class Ability : GameObject 
{
    private Ability(ReadOnlyMemory<char> rom) : base(rom)
    {

    }

    public new static Ability? Parse(ReadOnlyMemory<char> rom)
    {
        if(rom.Length == 0 || rom.IsEmpty) return null;
        return new Ability(rom);
    }
}