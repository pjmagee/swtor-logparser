using SwtorLogParser.Model;

namespace SwtorLogParser.Tests;

public class ThreatTests
{
    [Fact]
    public void Zero_Is_Positive()
    {
        var threat = Threat.Parse("<0>".AsMemory());
        Assert.NotNull(threat);
        Assert.Equal(0, threat.Value);
        Assert.True(threat.IsPositive);
        Assert.False(threat.IsNegative);
    }

    [Fact]
    public void Positive_Is_Positive()
    {
        var threat = Threat.Parse("<123>".AsMemory());
        Assert.NotNull(threat);
        Assert.Equal(123, threat.Value);
        Assert.True(threat.IsPositive);
        Assert.False(threat.IsNegative);
    }

    [Fact]
    public void Negative_Is_Negative()
    {
        var threat = Threat.Parse("<-123>".AsMemory());
        Assert.NotNull(threat);
        Assert.Equal(-123, threat.Value);
        Assert.False(threat.IsPositive);
        Assert.True(threat.IsNegative);
    }

    [Fact]
    public void No_Threat_Is_Null()
    {
        var threat = Threat.Parse("".AsMemory());
        Assert.Null(threat);
    }

    [Fact]
    public void Invalid_Threat_Is_Null()
    {
        var threat = Threat.Parse("<>".AsMemory());
        Assert.Null(threat);
    }

    // Pattern C (guard-null matrix, green today): Threat.Parse rejects cleanly when empty,
    // shorter than 3 chars, or the scope starts with 'v' (Threat.cs:23-34). Consolidates and
    // extends No_Threat_Is_Null / Invalid_Threat_Is_Null into a [Theory] matrix.
    [Theory]
    [InlineData("<>")]      // length < 3 guard
    [InlineData("")]        // empty guard
    [InlineData("<vfoo>")]  // scope[0] == 'v' guard -> returns null
    public void Threat_Parse_Rejects_Cleanly(string raw)
    {
        Assert.Null(Threat.Parse(raw.AsMemory()));
    }

    // BUG-05 (Pattern E, LAZY throw): scope "abc" passes the non-'v' guard so Parse returns a
    // non-null Threat; the int.Parse (Threat.cs:14) is deferred to .Value access and throws on
    // non-numeric scope. Phase 2 (TryParse) inverts this to a graceful value.
    [Fact]
    public void Threat_NonNumeric_Value_Throws_On_Access_Today()
    {
        var threat = Threat.Parse("<abc>".AsMemory());
        Assert.NotNull(threat); // Parse is LAZY — "abc" passes the non-'v' guard, returns non-null
        Assert.Throws<FormatException>(() => _ = threat.Value); // int.Parse("abc") at Threat.cs:14
    }
}