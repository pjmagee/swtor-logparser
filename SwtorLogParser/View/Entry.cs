using SwtorLogParser.Monitor;

namespace SwtorLogParser.View;

public class Entry
{
    public CombatLogsMonitor.PlayerStats Stats { get; set; } = null!;
    public DateTime Expiration { get; set; }
}
