using SwtorLogParser.Model;
using SwtorLogParser.Monitor;
using SwtorLogParser.Tests.Fixtures;

namespace SwtorLogParser.Tests;

public class ActorTests
{
    [Fact]
    public void Player_Is_Parsed()
    {
        var actor = Actor.Parse("@Powerful Subscriber#688623358308676|(4641.05,4529.71,694.02,-124.45)|(1/401177)"
            .AsMemory());

        Assert.NotNull(actor);
        Assert.True(actor.IsPlayer);
        Assert.False(actor.IsCompanion);
        Assert.False(actor.IsNpc);
        Assert.Equal(688623358308676, actor.Id);
        Assert.Equal("Powerful Subscriber", actor.Name);
        Assert.Equal(1, actor.Health);
        Assert.Equal(401177, actor.MaxHealth);
        Assert.Equal((4641.05f, 4529.71f, 694.02f, -124.45f), actor.Position);
    }

    // Phase 3 Plan 05 (TEST-02): HERMETIC. Previously this iterated CombatLogs.PlayerNames
    // sourced from the REAL %LocalAppData% settings folder (empty / threw without it). It now
    // installs an in-memory source with a KNOWN PlayerNames set, asserts IsLocalPlayer against
    // it, and restores the default source in finally. Passes deterministically with NO real
    // settings folder present.
    [Fact]
    public void Player_Is_Local_Is_True()
    {
        using var fixture = new InMemoryCombatLogSource(new HashSet<string> { "Aegrae" });
        CombatLogs.SetSource(fixture);
        try
        {
            Assert.NotEmpty(CombatLogs.PlayerNames);

            foreach (var name in CombatLogs.PlayerNames)
            {
                var actor = Actor.Parse(
                    $"@{name}#{Random.Shared.Next(1000000000)}|(4641.05,4529.71,694.02,-124.45)|(1/401177)".AsMemory());

                Assert.NotNull(actor);
                Assert.True(actor.IsPlayer);
                Assert.True(actor.IsLocalPlayer);
            }
        }
        finally
        {
            CombatLogs.ResetSource();
        }
    }

    [Fact]
    public void Npc_Is_Parsed()
    {
        var actor = Actor.Parse(
            "Yozusk Mauler {3158140992356352}:5577004295094|(4641.05,4529.71,694.02,-124.45)|(1/401177)".AsMemory());

        Assert.NotNull(actor);
        Assert.True(actor.IsNpc);
        Assert.False(actor.IsCompanion);
        Assert.False(actor.IsPlayer);
        Assert.Equal(3158140992356352, actor.Id);
        Assert.Equal("Yozusk Mauler", actor.Name);
        Assert.Equal(1, actor.Health);
        Assert.Equal(401177, actor.MaxHealth);
        Assert.Equal((4641.05f, 4529.71f, 694.02f, -124.45f), actor.Position);
    }

    [Fact]
    public void Companion_Is_Parsed()
    {
        var actor = Actor.Parse(
            "@Powerful Subscriber#688623358308676/Shae Vizla {3916370223824896}:2488005972848|(4568.53,4550.25,694.02,0.00)|(295328/295328)"
                .AsMemory());

        Assert.NotNull(actor);
        Assert.True(actor.IsCompanion);
        Assert.False(actor.IsNpc);
        Assert.False(actor.IsPlayer);
        Assert.Equal(3916370223824896, actor.Id);
        Assert.Equal("Shae Vizla", actor.Name);
        Assert.Equal(295328, actor.Health);
        Assert.Equal(295328, actor.MaxHealth);
        Assert.Equal((4568.53f, 4550.25f, 694.02f, 0.00f), actor.Position);
    }

    // TEST-03 criterion 3 (Pattern B): names containing the structural delimiters
    // [ ] { } @ : must NOT escape an exception FROM Parse, and Actor.Name (GetName,
    // Actor.cs:43-60) is wrapped in try/catch returning string-or-null. Green today.
    // Distinct ids/names per row so cache hits (Actor does not cache, but keep unambiguous).
    [Theory]
    [InlineData("@Name[bracket]#11|(0,0,0,0)|(1/2)")]
    [InlineData("@Name{brace}#12|(0,0,0,0)|(1/2)")]
    [InlineData("@Name@at#13|(0,0,0,0)|(1/2)")]
    [InlineData("NpcName:colon {3158140992356352}:5577004295094|(0,0,0,0)|(1/2)")]
    public void Actor_Name_With_Delimiters_Does_Not_Throw_From_Parse(string raw)
    {
        var a = Actor.Parse(raw.AsMemory());
        Assert.NotNull(a); // Parse never throws (LAZY) and never returns null for non-empty input
        _ = a.Name;        // GetName try/catch (Actor.cs:43-60) -> string or null, never escapes
    }

    // TEST-03 criterion 2 (Pattern F): ExtractPosition uses CultureInfo.InvariantCulture
    // (Actor.cs:147), so ',' delimits the four fields and '.' is the decimal point. This
    // LOCKS the invariant-culture behavior — BUG-03 already-correct site. Distinct id.
    [Fact]
    public void Actor_Position_Comma_Is_Field_Separator_Invariant()
    {
        var actor = Actor.Parse(
            "@Locale Lock#700000000000001|(4641.05,4529.71,694.02,-124.45)|(1/401177)".AsMemory());

        Assert.NotNull(actor);
        Assert.Equal((4641.05f, 4529.71f, 694.02f, -124.45f), actor.Position);
    }

    // BUG-05 (Pattern E): Actor.Parse succeeds on a structurally-present but non-numeric
    // health section. GetHealth (Actor.cs) now uses int.TryParse, so non-numeric health
    // "(x/y)" reads as null instead of throwing. Phase 2: now graceful (BUG-05).
    [Fact]
    public void Actor_NonNumeric_Health_Returns_Null()
    {
        var a = Actor.Parse("@Name#123|(0,0,0,0)|(x/y)".AsMemory());
        Assert.NotNull(a); // Parse is LAZY — succeeds
        Assert.Null(a.Health); // int.TryParse("x") fails -> null (was FormatException)
    }

    // BUG-05 (Pattern E): parallel .Id characterization. For an NPC line the id is read
    // between '{' and '}' via long.TryParse (GetId, Actor.cs). Non-numeric brace content
    // reads as null instead of throwing. Phase 2: now graceful (BUG-05).
    [Fact]
    public void Actor_NonNumeric_Id_Returns_Null()
    {
        var a = Actor.Parse("NpcWithBadId {abc}:5577004295094|(0,0,0,0)|(1/2)".AsMemory());
        Assert.NotNull(a); // Parse is LAZY — succeeds
        Assert.Null(a.Id); // long.TryParse("abc") fails -> null (was FormatException)
    }

    // HI-01 (completes BUG-05): a non-empty actor with fewer than 3 '|'-delimited sections
    // is NOT IsEmpty, so the old GetMaxHealth (guarded only on IsEmpty) indexed Roms[2] and
    // threw IndexOutOfRangeException. GetMaxHealth now bounds-checks (Roms.Count != 3 -> null)
    // matching GetHealth, so .MaxHealth returns null instead of throwing.
    [Theory]
    [InlineData("@Name#123")]                       // 1 section
    [InlineData("@Name#123|(0,0,0,0)")]             // 2 sections
    [InlineData("NpcShort {3158140992356352}:55")]  // 1 non-'=' section (not IsEmpty)
    public void Actor_Short_Sections_MaxHealth_Returns_Null_No_Throw(string raw)
    {
        var a = Actor.Parse(raw.AsMemory());
        Assert.NotNull(a); // Parse is LAZY for non-empty input
        var ex = Record.Exception(() => a.MaxHealth);
        Assert.Null(ex);        // no IndexOutOfRangeException escapes
        Assert.Null(a.MaxHealth); // Roms.Count != 3 -> null
    }

    // CR-01: a brace-less NPC actor (no '{id}' section) must NOT throw from Actor.Id.
    // GetId's NPC branch found openIndex == closeIndex == -1 and called Slice(0, -1)
    // (negative length) -> ArgumentOutOfRangeException, reachable via the public Actor.Id
    // surface (e.g. CombatLogLine.Target.Id on a player-damages-NPC line). Guarded to
    // return null on missing/inverted braces, matching the null-on-malformed-parse policy.
    [Fact]
    public void Actor_BraceLess_Npc_Id_Returns_Null_No_Throw()
    {
        var a = Actor.Parse("Yozusk Mauler:5577004295094|(0,0,0,0)|(1/2)".AsMemory());
        Assert.NotNull(a);   // Parse is LAZY — succeeds
        Assert.True(a.IsNpc);
        var ex = Record.Exception(() => a.Id);
        Assert.Null(ex);     // no ArgumentOutOfRangeException escapes (CR-01)
        Assert.Null(a.Id);   // no '{' / '}' -> null
    }

    // CR-01 (counterpart): a brace-bearing NPC still parses its id correctly — the guard
    // must not regress valid NPC actors.
    [Fact]
    public void Actor_BraceBearing_Npc_Id_Still_Parses()
    {
        var a = Actor.Parse("Yozusk Mauler {3158140992356352}:5577004295094|(0,0,0,0)|(1/2)".AsMemory());
        Assert.NotNull(a);
        Assert.True(a.IsNpc);
        Assert.Equal(3158140992356352, a.Id);
    }
}