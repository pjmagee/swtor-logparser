using SwtorLogParser.Monitor;

namespace SwtorLogParser.Cli.View;

public class Entry
{
    public CombatLogsMonitor.PlayerStats Stats { get; set; }
    public DateTime Expiration { get; set; }
}