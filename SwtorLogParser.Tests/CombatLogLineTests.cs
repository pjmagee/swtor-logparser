namespace SwtorLogParser.Tests;

public class CombatLogLineTests
{
    [Fact]
    public void Empty_Line_Is_Null()
    {
        var line = CombatLogLine.Parse("".AsMemory());
        Assert.Null(line);
    }
    
    [Fact]
    public void Line_With_No_Timestamp_Is_Null()
    {
        var line = CombatLogLine.Parse("foo".AsMemory());
        Assert.Null(line);
    }
    
    [Fact]
    public void Line_With_Valid_Sections_Is_Parsed()
    {
        var line = CombatLogLine.Parse("[18:12:13] [Powerful Subscriber 688623358308676 (1/401177)] [] [] [AreaEntered {836045448953664}: Imperial Fleet {137438989504}]".AsMemory());
        
        Assert.NotNull(line);
        
        Assert.NotNull(line.Source);
        Assert.NotNull(line.Action);
        
        Assert.Null(line.Target);
        Assert.Null(line.Ability);
        Assert.Null(line.Value);
        Assert.Null(line.Threat);
    }
    
    [Fact]
    public void Line_With_Valid_Sections_And_Threat_Is_Parsed()
    {
        var line = CombatLogLine.Parse("[20:33:17.759] [@Aegrae#689921479616853|(422.51,620.88,33.46,84.27)|(300469/379924)] [=] [Progressive Scan {3394132265402368}] [ApplyEffect {836045448945477}: Heal {836045448945500}] (8622) <3880>".AsMemory());
        
        Assert.NotNull(line);
        
        Assert.NotNull(line.Source);
        Assert.NotNull(line.Action);
        
        Assert.Null(line.Target);
        
        Assert.NotNull(line.Ability);
        Assert.NotNull(line.Value);
        Assert.NotNull(line.Threat);
    }
}