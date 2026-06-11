using SwtorLogParser.Model;
using SwtorLogParser.Monitor;

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

    [Fact]
    public void Player_Is_Local_Is_True()
    {
        foreach (var name in CombatLogs.PlayerNames)
        {
            var actor = Actor.Parse(
                $"@{name}#{Random.Shared.Next(1000000000)}|(4641.05,4529.71,694.02,-124.45)|(1/401177)".AsMemory());

            Assert.NotNull(actor);
            Assert.True(actor.IsPlayer);
            Assert.True(actor.IsLocalPlayer);
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
}