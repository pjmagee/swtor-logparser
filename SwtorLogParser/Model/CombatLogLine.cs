using System.Globalization;

namespace SwtorLogParser.Model;

public class CombatLogLine : IEquatable<CombatLogLine>
{
    private static readonly string[] TimeFormats = { "HH:mm:ss", "HH:mm:ss.fff" };

    // PERF-LAZY-01: backing fields + per-member parsed flags so each sub-object is parsed AT MOST
    // ONCE and the legitimate "parsed to null" result is memoized too (a plain `??=` would re-parse
    // forever on a null). Mirrors the existing lazy-property convention (GameObject `_name ??= ...`);
    // no lock — CombatLogLine instances flow single-threaded per line through the Rx pipeline, and a
    // benign re-compute would yield the identical value from the same retained slice.
    private Actor? _source;
    private bool _sourceParsed;
    private Actor? _target;
    private bool _targetParsed;
    private Ability? _ability;
    private bool _abilityParsed;
    private Action? _action;
    private bool _actionParsed;
    private Value? _value;
    private bool _valueParsed;
    private Threat? _threat;
    private bool _threatParsed;

    private CombatLogLine(
        ReadOnlyMemory<char> rom,
        List<ReadOnlyMemory<char>> roms,
        DateTime timeStamp
    )
    {
        Rom = rom;
        Roms = roms;
        TimeStamp = timeStamp;
    }

    public override int GetHashCode() => Rom.GetHashCode();

    public DateTime TimeStamp { get; }

    public Actor? Source
    {
        get
        {
            if (!_sourceParsed)
            {
                _source = Actor.Parse(Roms[1]);
                _sourceParsed = true;
            }
            return _source;
        }
    }

    public Actor? Target
    {
        get
        {
            if (!_targetParsed)
            {
                _target = Actor.Parse(Roms[2]);
                _targetParsed = true;
            }
            return _target;
        }
    }

    public Ability? Ability
    {
        get
        {
            if (!_abilityParsed)
            {
                _ability = Ability.Parse(Roms[3]);
                _abilityParsed = true;
            }
            return _ability;
        }
    }

    public Action? Action
    {
        get
        {
            if (!_actionParsed)
            {
                _action = Action.Parse(Roms[4]);
                _actionParsed = true;
            }
            return _action;
        }
    }

    public Value? Value
    {
        get
        {
            if (!_valueParsed)
            {
                _value = Value.Parse(Rom);
                _valueParsed = true;
            }
            return _value;
        }
    }

    public Threat? Threat
    {
        get
        {
            if (!_threatParsed)
            {
                _threat = Threat.Parse(Rom);
                _threatParsed = true;
            }
            return _threat;
        }
    }

    private ReadOnlyMemory<char> Rom { get; }

    private List<ReadOnlyMemory<char>> Roms { get; }

    public static CombatLogLine? Parse(ReadOnlyMemory<char> rom)
    {
        if (rom.IsEmpty)
            return null;
        var sections = GetSections(rom);
        if (sections.Count != 5)
            return null;
        if (
            !DateTime.TryParseExact(
                sections[0].Span,
                TimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var ts
            )
        )
            return null;
        return new CombatLogLine(rom, sections, ts);
    }

    public bool Equals(CombatLogLine? other) => Rom.Equals(other?.Rom);

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
        {
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
        }

        return roms;
    }
}
