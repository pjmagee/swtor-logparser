namespace SwtorLogParser;

public static class CombatLogs
{
    private static readonly string Settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, System.Environment.SpecialFolderOption.None), "Star Wars - The Old Republic", "CombatLogs");
    internal static DirectoryInfo CombatLogsDirectory { get; } = new(Settings);
    
    internal static ReadOnlyMemory<char> Energy { get; } = "energy".AsMemory();
    internal static ReadOnlyMemory<char> Kinetic { get; } = "kinetic".AsMemory();
    internal static ReadOnlyMemory<char> Internal { get; } = "internal".AsMemory();
    internal static ReadOnlyMemory<char> Elemental { get; } = "elemental".AsMemory();
    internal static ReadOnlyMemory<char> Charges { get; } = "charges".AsMemory();
    internal static ReadOnlyMemory<char> Critical { get; } = "*".AsMemory();
    internal static ReadOnlyMemory<char> Parry { get; } = "-parry".AsMemory();
    internal static ReadOnlyMemory<char> Miss { get; } = "-miss".AsMemory();
    internal static ReadOnlyMemory<char> Dodge { get; } = "-dodge".AsMemory();
    internal static ReadOnlyMemory<char> Absorbed { get; } = "absorbed".AsMemory();
    internal static ReadOnlyMemory<char> Deflect { get; } = "deflect".AsMemory();
    internal static ReadOnlyMemory<char> Tilde { get; } = "~".AsMemory();
    internal static ReadOnlyMemory<char> HeroEnginePrefix { get; } = "he".AsMemory();

    public static IEnumerable<CombatLog> EnumerateCombatLogs()
    {
        foreach (FileInfo fi in CombatLogsDirectory.EnumerateFiles("*.txt"))
        {
            yield return new CombatLog(fi);
        }
    }
    
    public static CombatLog? GetLatestCombatLog()
    {
        FileInfo? fileInfo = CombatLogsDirectory.EnumerateFiles("*.txt").MaxBy(x => x.LastWriteTimeUtc);
        return fileInfo is not null ? new CombatLog(fileInfo) : null;
    }
}