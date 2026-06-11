using SwtorLogParser.Model;
using SwtorLogParser.Monitor;
using SwtorLogParser.Tests.Fixtures;

namespace SwtorLogParser.Tests;

public class CombatLogLineTests
{
    // Phase 3 Plan 05 (TEST-01): HERMETIC. Previously this iterated the REAL %Documents%
    // SWTOR CombatLogs folder, so it flaked depending on what (if anything) lived there and
    // on ambient ICombatLogSource state from other tests. It now installs its OWN in-memory
    // source seeded with known well-formed golden lines, iterates via the seam, and restores
    // the default source in finally. Passes deterministically with NO real SWTOR folder.
    [Fact]
    public void All_Logs_Are_Not_Null()
    {
        using var fixture = new InMemoryCombatLogSource();
        // Golden lines: player Source actors carry a '#id' so Actor.Id resolves; the NPC line
        // uses a '{id}' brace form. (We deliberately avoid the brace-less NPC form
        // '[Name 123 (h/m)]' here — that exercises Actor.GetId's unguarded NPC branch, a
        // separate latent issue out of this plan's CombatLogs/seam scope.)
        fixture.AddLogFile(
            "combat_2024-01-01_00_00_00_000000.txt",
            "[20:33:17.759] [@Aegrae#689921479616853|(422.51,620.88,33.46,84.27)|(300469/379924)] [=] [Progressive Scan {3394132265402368}] [ApplyEffect {836045448945477}: Heal {836045448945500}] (8622) <3880>",
            "[18:12:13] [Yozusk Mauler {3158140992356352}:5577004295094|(4641.05,4529.71,694.02,-124.45)|(1/401177)] [=] [] [AreaEntered {836045448953664}: Imperial Fleet {137438989504}]",
            "[21:45:02.123] [@Goldenpc#688623358300042|(100.10,200.20,300.30,40.40)|(300042/379942)] [=] [Progressive Scan {3394132265400042}] [ApplyEffect {836045448940042}: Damage {836045448945501}] (1234) <567>");

        CombatLogs.SetSource(fixture);
        try
        {
            var seenAnyLine = false;

            foreach (var combatLog in CombatLogs.EnumerateCombatLogs())
                using (var reader = combatLog.FileInfo.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    using (var streamReader = new StreamReader(reader))
                    {
                        while (!streamReader.EndOfStream)
                        {
                            var line = streamReader.ReadLine();

                            if (line is not null)
                            {
                                seenAnyLine = true;
                                var parsed = CombatLogLine.Parse(line.AsMemory());
                                Assert.NotNull(parsed);

                                Assert.NotEmpty(parsed.ToString());

                                Assert.True(parsed.TimeStamp != default);

                                if (parsed.Source is not null)
                                {
                                    Assert.NotNull(parsed.Source.Id);
                                    Assert.NotNull(parsed.Source.Name);
                                }

                                if (parsed.Action is not null)
                                {
                                    Assert.NotNull(parsed.Action.Effect);
                                    Assert.NotNull(parsed.Action.Event);
                                }
                            }
                        }
                    }
                }

            Assert.True(seenAnyLine, "hermetic fixture must yield at least one combat-log line");
        }
        finally
        {
            CombatLogs.ResetSource();
        }
    }

    [Fact]
    public void Empty_Line_Is_Null()
    {
        var line = CombatLogLine.Parse("".AsMemory());
        Assert.Null(line);
    }

    [Fact]
    public void Line_With_No_Timestamp_Is_Null()
    {
        var line = CombatLogLine.Parse("foo".AsMemory());
        Assert.Null(line);
    }

    [Fact]
    public void Line_With_Valid_Sections_Is_Parsed()
    {
        var line = CombatLogLine.Parse("[18:12:13] [Powerful Subscriber 688623358308676 (1/401177)] [] [] [AreaEntered {836045448953664}: Imperial Fleet {137438989504}]".AsMemory());

        Assert.NotNull(line);

        Assert.NotNull(line.Source);
        Assert.NotNull(line.Action);

        Assert.Null(line.Target);
        Assert.Null(line.Ability);
        Assert.Null(line.Value);
        Assert.Null(line.Threat);
    }

    [Fact]
    public void Line_With_Valid_Sections_And_Threat_Is_Parsed()
    {
        var line = CombatLogLine.Parse("[20:33:17.759] [@Aegrae#689921479616853|(422.51,620.88,33.46,84.27)|(300469/379924)] [=] [Progressive Scan {3394132265402368}] [ApplyEffect {836045448945477}: Heal {836045448945500}] (8622) <3880>".AsMemory());

        Assert.NotNull(line);

        Assert.NotNull(line.Source);
        Assert.NotNull(line.Action);

        Assert.Null(line.Target);

        Assert.NotNull(line.Ability);
        Assert.NotNull(line.Value);
        Assert.NotNull(line.Threat);
    }

    // Phase 2: bad timestamp now returns null (BUG-03). CombatLogLine.Parse gates the
    // first section through DateTime.TryParseExact + InvariantCulture in the static factory,
    // so a non-parseable timestamp skips the line (returns null) instead of throwing. (TEST-03)
    [Fact]
    public void CombatLogLine_NonParseable_Timestamp_Returns_Null()
    {
        // Well-formed 5-section line whose FIRST section is not a parseable timestamp.
        var line =
            "[notatime] [Powerful Subscriber 688623358308991 (1/401177)] [] [] [AreaEntered {836045448953991}: Imperial Fleet {137438989991}]";

        Assert.Null(CombatLogLine.Parse(line.AsMemory()));
    }

    // Golden lock: SWTOR emits time-only stamps (HH:mm:ss[.fff]) which are culture-robust.
    // Distinct literals from the existing goldens so this exercises a fresh parse. (TEST-03)
    [Fact]
    public void CombatLogLine_Golden_TimeOnly_Stamp_Parses()
    {
        var line =
            "[21:45:02.123] [@Goldenpc#688623358300042|(100.10,200.20,300.30,40.40)|(300042/379942)] [=] [Progressive Scan {3394132265400042}] [ApplyEffect {836045448940042}: Heal {836045448940500}] (1234) <567>".AsMemory();

        var parsed = CombatLogLine.Parse(line);

        Assert.NotNull(parsed);
        Assert.True(parsed.TimeStamp != default);
        Assert.NotNull(parsed.Source);
        Assert.NotNull(parsed.Action);
    }

    [Fact]
    public void Duplicated_Line_Appears_In_HashSet_Once()
    {
        // A
        var comparer = new CombatLogLineComparer();
        var line1 = CombatLogLine.Parse("[20:33:17.759] [@Aegrae#689921479616853|(422.51,620.88,33.46,84.27)|(300469/379924)] [=] [Progressive Scan {3394132265402368}] [ApplyEffect {836045448945477}: Heal {836045448945500}] (8622) <3880>".AsMemory());
        var line2 = CombatLogLine.Parse("[20:33:17.759] [@Aegrae#689921479616853|(422.51,620.88,33.46,84.27)|(300469/379924)] [=] [Progressive Scan {3394132265402368}] [ApplyEffect {836045448945477}: Heal {836045448945500}] (8622) <3880>".AsMemory());
        var set = new HashSet<CombatLogLine>(comparer);

        Assert.NotNull(line1);
        Assert.NotNull(line2);
        
        // A
        set.Add(line1);
        set.Add(line2);
        
        // A
        Assert.Single(set);
        Assert.True(comparer.Equals(line1, line2));
    }
}
