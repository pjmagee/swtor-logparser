using SwtorLogParser.Monitor;

namespace SwtorLogParser.Overlay.View;

public class Entry : IComparable<Entry>, IEquatable<Entry>
{
    public CombatLogsMonitor.PlayerStats Stats { get; set; } = null!;
    
    public string Name => Stats.Player!.Name!;
    public string DPS => Stats.DPS?.ToString("N") ?? "-";
    public string DCrit => Stats.DPSCritP.HasValue ? Stats.DPSCritP.Value.ToString("N") : "-";
    public string HPS => Stats.HPS?.ToString("N") ?? "-";
    public string HCrit => Stats.HPSCritP.HasValue ? Stats.HPSCritP.Value.ToString("N") : "-";
    public DateTime Expiration { get; set; }

    public int CompareTo(Entry? other) => String.Compare(this. Name, other?.Name, StringComparison.Ordinal);

    public bool Equals(Entry? other)
    {
        return Stats.Player.Id.Equals(other?.Stats.Player.Id);
    }
}