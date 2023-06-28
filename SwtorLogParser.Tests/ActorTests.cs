namespace SwtorLogParser.Tests;

public class ActorTests
{
    [Fact]
    public void Player_Is_Parsed()
    {
        var actor = Actor.Parse("@Powerful Subscriber#688623358308676|(4641.05,4529.71,694.02,-124.45)|(1/401177)".AsMemory());
        
        Assert.True(actor.IsPlayer);
        Assert.Equal(688623358308676, actor.Id);
        Assert.Equal("Powerful Subscriber", actor.Name);
        Assert.Equal(1, actor.Health);
        Assert.Equal(401177, actor.MaxHealth);
        Assert.Equal((4641.05f,4529.71f,694.02f,-124.45f), actor.Position);
    }
    
    [Fact]
    public void Npc_Is_Parsed()
    {
        var actor = Actor.Parse("Yozusk Mauler {3158140992356352}:5577004295094|(4641.05,4529.71,694.02,-124.45)|(1/401177)".AsMemory());
        
        Assert.True(actor.IsNpc);
        Assert.Equal(3158140992356352, actor.Id);
        Assert.Equal("Yozusk Mauler", actor.Name);
        Assert.Equal(1, actor.Health);
        Assert.Equal(401177, actor.MaxHealth);
        Assert.Equal((4641.05f,4529.71f,694.02f,-124.45f), actor.Position);
    }

    [Fact]
    public void Companion_Is_Parsed()
    {
        var actor = Actor.Parse("@Powerful Subscriber#688623358308676/Shae Vizla {3916370223824896}:2488005972848|(4568.53,4550.25,694.02,0.00)|(295328/295328)".AsMemory());
        
        Assert.True(actor.IsCompanion);
        Assert.Equal(3916370223824896, actor.Id);
        Assert.Equal("Shae Vizla", actor.Name);
        Assert.Equal(295328, actor.Health);
        Assert.Equal(295328, actor.MaxHealth);
        Assert.Equal((4568.53f,4550.25f,694.02f,0.00f), actor.Position);
    }
}