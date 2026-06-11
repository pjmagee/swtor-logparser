using SwtorLogParser.Model;

namespace SwtorLogParser.Tests;

public class ValueTests
{
    [Fact]
    public void Zero_Is_Zero()
    {
        var value = Value.Parse("(0)".AsMemory());
        Assert.NotNull(value);
        Assert.Equal(0, value.Integer);
    }

    [Fact]
    public void Miss_Is_Parsed()
    {
        var value = Value.Parse("(0 -miss)".AsMemory());

        Assert.NotNull(value);
        Assert.True(value.IsMiss);
        Assert.Equal(0, value.Integer);
    }

    [Fact]
    public void Absorbed_Is_Parsed()
    {
        var value = Value.Parse("(123 absorbed)".AsMemory());

        Assert.NotNull(value);
        Assert.True(value.IsAbsorbed);
        Assert.Equal(123, value.Integer);
    }

    [Fact]
    public void Critical_Is_Parsed()
    {
        var value = Value.Parse("(123*)".AsMemory());

        Assert.NotNull(value);
        Assert.True(value.IsCritical);
        Assert.Equal(123, value.Integer);
    }

    [Fact]
    public void Parry_Is_Parsed()
    {
        var value = Value.Parse("(123 -parry)".AsMemory());

        Assert.NotNull(value);
        Assert.True(value.IsParry);
        Assert.Equal(123, value.Integer);
    }

    [Fact]
    public void Dodge_Is_Parsed()
    {
        var value = Value.Parse("(123 -dodge)".AsMemory());

        Assert.NotNull(value);
        Assert.True(value.IsDodge);
        Assert.Equal(123, value.Integer);
    }

    [Fact]
    public void Deflect_Is_Parsed()
    {
        var value = Value.Parse("(123 deflect)".AsMemory());

        Assert.NotNull(value);
        Assert.True(value.IsDeflect);
        Assert.Equal(123, value.Integer);
    }

    [Fact]
    public void Tilde_Is_Parsed()
    {
        var value = Value.Parse("(123 ~0)".AsMemory());

        Assert.NotNull(value);
        Assert.Equal(0, value.Tilde);
        Assert.Equal(123, value.Integer);
    }

    [Fact]
    public void Charges_Is_Parsed()
    {
        var value = Value.Parse("(123 charges)".AsMemory());

        Assert.NotNull(value);
        Assert.True(value.IsCharges);
        Assert.Equal(123, value.Integer);
    }

    [Fact]
    public void Energy_Is_Parsed()
    {
        var value = Value.Parse("(123 energy)".AsMemory());

        Assert.NotNull(value);
        Assert.True(value.IsEnergy);
        Assert.Equal(123, value.Integer);
    }

    [Fact]
    public void Kinetic_Is_Parsed()
    {
        var value = Value.Parse("(123 kinetic)".AsMemory());

        Assert.NotNull(value);
        Assert.True(value.IsKinetic);
        Assert.Equal(123, value.Integer);
    }

    [Fact]
    public void Elemental_Is_Parsed()
    {
        var value = Value.Parse("(123 elemental)".AsMemory());

        Assert.NotNull(value);
        Assert.True(value.IsElemental);
        Assert.Equal(123, value.Integer);
    }

    [Fact]
    public void Internal_Is_Parsed()
    {
        var value = Value.Parse("(123 internal)".AsMemory());

        Assert.NotNull(value);
        Assert.True(value.IsInternal);
        Assert.Equal(123, value.Integer);
    }

    [Fact]
    public void HeroEnginePrefix_Is_Not_Parsed()
    {
        var value = Value.Parse("(he)".AsMemory());
        Assert.Null(value);
    }

    // Pattern C (guard-null matrix, green today): Value.Parse rejects cleanly for the HeroEngine
    // prefix and for input with no '(' / ')' (Value.cs:64-70). Extends HeroEnginePrefix_Is_Not_Parsed.
    [Theory]
    [InlineData("(he)")]       // HeroEngine prefix guard
    [InlineData("no parens")]  // no '(' / ')' -> start/end == -1 guard
    public void Value_Parse_Rejects_Cleanly(string raw)
    {
        Assert.Null(Value.Parse(raw.AsMemory()));
    }

    // BUG-05 (Pattern E): Parse guards pass (parens present, not HeroEngine), so a non-null
    // Value is returned; the brace content is now read via ulong.TryParse (Value.cs), so
    // non-numeric content reads as null instead of throwing. Phase 2: now graceful (BUG-05).
    [Fact]
    public void Value_NonNumeric_Id_Returns_Null()
    {
        var value = Value.Parse("(123 {abc})".AsMemory());
        Assert.NotNull(value); // Parse is LAZY — guards pass, returns non-null
        Assert.Null(value.Id); // ulong.TryParse("abc") fails -> null (was FormatException)
    }
}