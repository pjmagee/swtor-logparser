namespace SwtorLogParser.Tests;

public class StaticTests
{
    [Fact]
    public void Folder_Contains_Logs()
    {
        Assert.True(CombatLogs.CombatLogsDirectory.Exists);
        Assert.True(CombatLogs.CombatLogsDirectory.EnumerateFiles().Any());
    }
}