namespace SwtorLogParser.Tests;

public class ActionTests
{
    [Fact]
    public void Event_And_Effect_Parsed()
    {
        var action = Action.Parse("AreaEntered {836045448953664}: Imperial Fleet {137438989504}".AsMemory());
        
        Assert.NotNull(action);
        Assert.False(action.Effect.IsNested);
        
        Assert.Equal("AreaEntered", action.Event.Name);
        Assert.Equal(836045448953664u, action.Event.Id);
        
        Assert.Equal("Imperial Fleet", action.Effect.Name);
        Assert.Equal(137438989504u, action.Effect.Id);
    }

    [Fact]
    public void Event_And_Effect_Nested_Parsed()
    {
        var action = Action.Parse("DisciplineChanged {836045448953665}: Mercenary {16141111589108060476}/Bodyguard {2031339142381600}".AsMemory());
        
        Assert.NotNull(action);
        Assert.True(action.Effect.IsNested);
        
        Assert.Equal("DisciplineChanged", action.Event.Name);
        Assert.Equal(836045448953665u, action.Event.Id);
        
        Assert.Equal("Bodyguard", action.Effect.Name);
        Assert.Equal(2031339142381600u, action.Effect.Id);
        
        Assert.Equal("Mercenary", action.Effect.ParentName);
        Assert.Equal(16141111589108060476u, action.Effect.ParentId);
    }

    [Fact]
    public void Same_Actions_Are_Equal()
    {
        var action1 = Action.Parse("[ApplyEffect {836045448945477}: Damage {836045448945501}]".AsMemory());
        var action2 = Action.Parse("[ApplyEffect {836045448945477}: Damage {836045448945501}]".AsMemory());
        
        Assert.StrictEqual(action1, action2);
    }
}