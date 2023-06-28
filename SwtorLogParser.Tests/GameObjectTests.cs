namespace SwtorLogParser.Tests;

public class GameObjectTests
{
    [Fact]
    public void Name_And_Id_Parsed()
    {
        var name = "ApplyEffect";
        var id = 836045448945477;
        
        var gameObject = new GameObject($"{name} {{{id}}}".AsMemory());
        Assert.Equal(id, gameObject.Id);
        Assert.Equal(name, gameObject.Name);
    }
    
    [Fact]
    public void Nested_Is_Parsed()
    {
        var name = "Darkness";
        var id = 2031339142381582;
        var gameObject = new GameObject($"Assassin {16141163438392504574}/{name} {{{id}}}".AsMemory());
        Assert.True(gameObject.IsNested);
        Assert.Equal(id, gameObject.Id);
        Assert.Equal(name, gameObject.Name);
    }
}