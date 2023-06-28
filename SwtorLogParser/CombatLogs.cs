namespace SwtorLogParser;

public static class CombatLogs
{
    private static readonly string Settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, System.Environment.SpecialFolderOption.None), "Star Wars - The Old Republic", "CombatLogs");
    public static DirectoryInfo CombatLogsDirectory { get; } = new(Settings);
    public static ReadOnlyMemory<char> Energy { get; } = "energy".AsMemory();
    public static ReadOnlyMemory<char> Kinetic { get; } = "kinetic".AsMemory();
    public static ReadOnlyMemory<char> Internal { get; } = "internal".AsMemory();
    public static ReadOnlyMemory<char> Elemental { get; } = "elemental".AsMemory();
    public static ReadOnlyMemory<char> Charges { get; } = "charges".AsMemory();
    public static ReadOnlyMemory<char> Critical { get; } = "*".AsMemory();
    public static ReadOnlyMemory<char> Parry { get; } = "-parry".AsMemory();
    public static ReadOnlyMemory<char> Miss { get; } = "-miss".AsMemory();
    public static ReadOnlyMemory<char> Dodge { get; } = "-dodge".AsMemory();
    public static ReadOnlyMemory<char> Absorbed { get; } = "absorbed".AsMemory();
    public static ReadOnlyMemory<char> Deflect { get; } = "deflect".AsMemory();
    public static ReadOnlyMemory<char> Tilde { get; } = "~".AsMemory();
    public static ReadOnlyMemory<char> HeroEnginePrefix { get; } = "he".AsMemory();

    public static IEnumerable<CombatLog> EnumerateCombatLogs()
    {
        foreach (FileInfo fi in CombatLogsDirectory.EnumerateFiles("*.txt"))
        {
            yield return new CombatLog(fi);
        }
    }
}