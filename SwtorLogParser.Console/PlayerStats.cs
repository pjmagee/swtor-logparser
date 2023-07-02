public class PlayerStats
{
    public string Player { get; set; } = null!;
    public double? HPS { get; set; }
    public double? HPSCritP { get; set; }
    public double? DPS { get; set; }
    public double? DPSCritP { get; set; }
    public DateTime Expiration { get; set; }
}