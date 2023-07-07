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
}