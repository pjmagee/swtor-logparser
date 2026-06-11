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

    // Golden lock (Pattern A): distinct shape — apostrophe + spaces in name with a DISTINCT
    // unsigned id from the existing goldens. Locks current correct Name + unsigned Id output.
    [Fact]
    public void Ability_Golden_Name_And_Id()
    {
        var ability = Ability.Parse("Master's Lightsaber Strike {814792340963328}".AsMemory());

        Assert.NotNull(ability);
        Assert.False(ability.IsNested);
        Assert.Equal("Master's Lightsaber Strike", ability.Name);
        Assert.Equal(814792340963328u, ability.Id);
    }

    // Phase 2: now returns null .Id (BUG-05). Ability.Parse is LAZY (unlike GameObject.Parse
    // which reads .Id eagerly): Parse does NOT touch .Id, so it returns a non-null Ability even for
    // non-numeric brace content. The inherited GetId (GameObject.cs) now uses ulong.TryParse, so
    // .Id returns null instead of throwing FormatException — fixed transitively via GameObject.GetId.
    // Cache hygiene: the GameObjectCache keys on Rom.GetHashCode and never clears between tests,
    // so use a literal UNIQUE to this test ("ZqxAbilityLazyWidget") to avoid a cached hit masking
    // the parse path.
    [Fact]
    public void Ability_NonNumeric_Id_Throws_On_Access_Today()
    {
        var ability = Ability.Parse("ZqxAbilityLazyWidget {abc}".AsMemory());

        Assert.NotNull(ability);
        Assert.Null(ability.Id);
    }
}