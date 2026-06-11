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

    // LAZY inherited-Id throw (Pattern E, BUG-05). Ability.Parse is LAZY (unlike GameObject.Parse
    // which reads .Id eagerly): Parse does NOT touch .Id, so it returns a non-null Ability even for
    // non-numeric brace content; the throw only happens on .Id access via the inherited GetId
    // (GameObject.cs:79-99 → ulong.Parse). Phase 2 will make this graceful.
    // Cache hygiene: the GameObjectCache keys on Rom.GetHashCode and never clears between tests,
    // so use a literal UNIQUE to this test ("ZqxAbilityLazyWidget") to avoid a cached hit masking
    // the parse path.
    [Fact]
    public void Ability_NonNumeric_Id_Throws_On_Access_Today()
    {
        var ability = Ability.Parse("ZqxAbilityLazyWidget {abc}".AsMemory());

        Assert.NotNull(ability);
        Assert.Throws<FormatException>(() => _ = ability.Id);
    }
}