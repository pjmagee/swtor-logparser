using SwtorLogParser.Caching;
using SwtorLogParser.Model;
using Action = SwtorLogParser.Model.Action;

namespace SwtorLogParser.Monitor;

public static class CombatLogs
{
    // Separate per-concrete-type content-keyed bounded caches (RFCT-03).
    // AbilityCache is NEW: Ability no longer shares GameObjectCache, eliminating the
    // latent (Ability?)value cross-type cast bug. Cap 4096 entries, FIFO eviction.
    internal static readonly BoundedCache<GameObject> GameObjectCache = new(4096);
    internal static readonly BoundedCache<Ability> AbilityCache = new(4096);
    internal static readonly BoundedCache<Action> ActionCache = new(4096);

    private static readonly string LogsPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, Environment.SpecialFolderOption.None),
            "Star Wars - The Old Republic", "CombatLogs");

    private static readonly string SettingsPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.None), "SWTOR", "swtor", "settings");

    static CombatLogs()
    {
        PlayerNames = SettingsDirectory.EnumerateFiles("*PlayerGUIState.ini")
            .Select(x => SecondSegmentOrNull(x.Name))
            .Where(n => n is not null)
            .Select(n => n!)
            .ToHashSet();
    }

    internal static string? SecondSegmentOrNull(string fileName)
    {
        var parts = fileName.Split('_');
        return parts.Length > 1 ? parts[1] : null;
    }

    internal static DirectoryInfo CombatLogsDirectory { get; } = new(LogsPath);
    internal static DirectoryInfo SettingsDirectory { get; } = new(SettingsPath);

    public static HashSet<string> PlayerNames { get; }

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
        foreach (var fi in CombatLogsDirectory.EnumerateFiles("*.txt")) yield return new CombatLog(fi);
    }

    public static CombatLog? GetLatestCombatLog()
    {
        var fileInfo = CombatLogsDirectory.EnumerateFiles("*.txt").MaxBy(x => x.LastWriteTimeUtc);
        return fileInfo is not null ? new CombatLog(fileInfo) : null;
    }
}