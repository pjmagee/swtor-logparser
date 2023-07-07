using SwtorLogParser.Model;

namespace SwtorLogParser.Tests;

public class GameObjectTests
{
    [Fact]
    public void Name_And_Id_Parsed()
    {
        var name = "ApplyEffect";
        var id = 836045448945477u;
        
        var gameObject = GameObject.Parse($"{name} {{{id}}}".AsMemory());
        
        Assert.NotNull(gameObject);
        Assert.Equal(id, gameObject.Id);
        Assert.Equal(name, gameObject.Name);
    }
    
    [Fact]
    public void Game_Objects_Are_Equal()
    {
        var name = "ApplyEffect";
        var id = 836045448945477u;
        
        Assert.StrictEqual(GameObject.Parse($"{name} {{{id}}}".AsMemory()), GameObject.Parse($"{name} {{{id}}}".AsMemory()));
    }
    
    [Fact]
    public void Invalid_Id_Not_Parsed()
    {
        var name = "ApplyEffect";
        var id = 836045448945477u;
        
        Assert.Null(GameObject.Parse($"{name} {{{id}".AsMemory()));
        Assert.Null(GameObject.Parse($"{name} {id}}}".AsMemory()));
    }
    
    [Fact]
    public void Nested_Is_Parsed()
    {
        var name = "Darkness";
        var id = 2031339142381582u;
        var gameObject = GameObject.Parse($"Assassin {16141163438392504574}/{name} {{{id}}}".AsMemory());
        Assert.NotNull(gameObject);
        Assert.True(gameObject.IsNested);
        Assert.Equal(id, gameObject.Id);
        Assert.Equal(name, gameObject.Name);
    }
}