namespace SwtorLogParser.Model;

public class CombatLogLine
{
    private CombatLogLine(ReadOnlyMemory<char> rom, List<ReadOnlyMemory<char>> roms)
    {
        Rom = rom;
        Roms = roms;

        TimeStamp = DateTime.Parse(Roms[0].Span);
        Source = Actor.Parse(Roms[1]);
        Target = Actor.Parse(Roms[2]);
        Ability = Ability.Parse(Roms[3]);
        Action = Action.Parse(Roms[4]);
        Value = Value.Parse(Rom);
        Threat = Threat.Parse(Rom);
    }

    public DateTime TimeStamp { get; }

    public Actor? Source { get; }

    public Actor? Target { get; }

    public Ability? Ability { get; }

    public Action? Action { get; }

    public Value? Value { get; }

    public Threat? Threat { get; }

    private ReadOnlyMemory<char> Rom { get; }

    private List<ReadOnlyMemory<char>> Roms { get; }

    public static CombatLogLine? Parse(ReadOnlyMemory<char> rom)
    {
        if (rom.IsEmpty) return null;
        var sections = GetSections(rom);
        return sections.Count != 5 ? null : new CombatLogLine(rom, sections);
    }

    public override string ToString()
    {
        var v = Value is not null ? $"({Value})" : null;
        var t = Threat is not null ? $"<{Threat}>" : null;
        return $"[{TimeStamp:T}] [{Source}] [{Target}] [{Ability}] [{Action}] {v} {t}".Trim();
    }

    private static List<ReadOnlyMemory<char>> GetSections(ReadOnlyMemory<char> rom)
    {
        var roms = new List<ReadOnlyMemory<char>>();

        const char sectionOpen = '[';
        const char sectionClose = ']';

        var start = -1;
        var end = -1;

        for (var i = 0; i < rom.Length; i++)
            if (rom.Span[i] == sectionOpen)
            {
                start = i + 1;
            }
            else if (rom.Span[i] == sectionClose)
            {
                end = i;

                if (start != -1)
                {
                    roms.Add(rom.Slice(start, end - start));
                    start = -1;
                }
            }

        return roms;
    }
}