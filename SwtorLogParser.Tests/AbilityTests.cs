using SwtorLogParser.Model;

namespace SwtorLogParser.Tests;

public class AbilityTests
{
    [Fact]
    public void Ability_With_Name_And_Id_Parsed()
    {
        var ability = Ability.Parse("Overlord's Command Throne {3039943492370432}".AsMemory());

        Assert.NotNull(ability);
        Assert.False(ability.IsNested);
        Assert.Equal("Overlord's Command Throne", ability.Name);
        Assert.Equal(3039943492370432u, ability.Id);
    }

    [Fact]
    public void Ability_With_No_Name_And_Id_And_Nested_Parsed()
    {
        var ability = Ability.Parse(" {3039943492370432}".AsMemory());

        Assert.NotNull(ability);
        Assert.False(ability.IsNested);
        Assert.Null(ability.Name);
        Assert.Equal(3039943492370432u, ability.Id);
    }
}