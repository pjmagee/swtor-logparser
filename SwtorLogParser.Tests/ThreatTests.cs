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
}