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
        Assert.Equal(836045448953664, action.Event.Id);
        
        Assert.Equal("Imperial Fleet", action.Effect.Name);
        Assert.Equal(137438989504, action.Effect.Id);
    }

    [Fact]
    public void Event_And_Effect_Nested_Parsed()
    {
        var action = Action.Parse("DisciplineChanged {836045448953665}: Mercenary {16141111589108060476}/Bodyguard {2031339142381600}".AsMemory());
        
        Assert.NotNull(action);
        Assert.True(action.Effect.IsNested);
        
        Assert.Equal("DisciplineChanged", action.Event.Name);
        Assert.Equal(836045448953665, action.Event.Id);
        
        Assert.Equal("Bodyguard", action.Effect.Name);
        Assert.Equal(2031339142381600, action.Effect.Id);
    }
}