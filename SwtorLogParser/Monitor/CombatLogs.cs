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

    // The default real-filesystem source (Plan 05). The static ctor no longer enumerates a
    // possibly-missing settings directory at type-load — the DefaultCombatLogSource guards
    // every read with Directory.Exists, so touching any CombatLogs member is CI-safe.
    private static ICombatLogSource _source = new DefaultCombatLogSource();

    /// <summary>The currently-installed filesystem source (default = real SWTOR paths).</summary>
    internal static ICombatLogSource Source => _source;

    /// <summary>Test seam: install an alternate (e.g. in-memory/temp) source.</summary>
    internal static void SetSource(ICombatLogSource source) => _source = source;

    /// <summary>Test seam: restore the real default source (avoids cross-test leakage).</summary>
    internal static void ResetSource() => _source = new DefaultCombatLogSource();

    internal static string? SecondSegmentOrNull(string fileName)
    {
        var parts = fileName.Split('_');
        return parts.Length > 1 ? parts[1] : null;
    }

    internal static DirectoryInfo CombatLogsDirectory => _source.CombatLogsDirectory;

    public static ISet<string> PlayerNames => _source.PlayerNames;

    // BUG-260612-dso: the English-word damage-TYPE/RESULT needles (energy/kinetic/internal/
    // elemental/-parry/-miss/-dodge/absorbed/deflect) were removed — type/result detection now
    // keys off the numeric {id} in Value.cs (locale-robust). These char/word needles remain because
    // they are NOT id-keyed in the log format: Critical ('*'), Charges, Tilde ('~'), HeroEngine ('he').
    internal static ReadOnlyMemory<char> Charges { get; } = "charges".AsMemory();
    internal static ReadOnlyMemory<char> Critical { get; } = "*".AsMemory();
    internal static ReadOnlyMemory<char> Tilde { get; } = "~".AsMemory();
    internal static ReadOnlyMemory<char> HeroEnginePrefix { get; } = "he".AsMemory();

    public static IEnumerable<CombatLog> EnumerateCombatLogs() => _source.EnumerateCombatLogs();

    public static CombatLog? GetLatestCombatLog() => _source.GetLatestCombatLog();

    /// <summary>
    /// Default <see cref="ICombatLogSource"/> wrapping the real SWTOR filesystem paths. Every
    /// directory read is guarded by <see cref="Directory.Exists"/> (Pitfall 6): absent folders
    /// yield empty results instead of throwing DirectoryNotFoundException at type-load.
    /// Behavior for hosts is byte-identical to before when the real folders are present.
    /// </summary>
    private sealed class DefaultCombatLogSource : ICombatLogSource
    {
        private static readonly string LogsPath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolderOption.None
            ),
            "Star Wars - The Old Republic",
            "CombatLogs"
        );

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.None
            ),
            "SWTOR",
            "swtor",
            "settings"
        );

        public DirectoryInfo CombatLogsDirectory { get; } = new(LogsPath);

        private DirectoryInfo SettingsDirectory { get; } = new(SettingsPath);

        // Lazy + Directory.Exists-guarded: PlayerNames is no longer enumerated unconditionally
        // at type-load (the old static-ctor enumeration is what threw TypeInitializationException
        // on machines without the settings folder). Absent folder -> empty set.
        //
        // WR-03: thread-safe one-time init. The prior `??=` was an unsynchronized data race —
        // two threads racing first access could both run LoadPlayerNames and produce distinct
        // sets (redundant file IO + identity divergence). Lazy<T> with the default
        // ExecutionAndPublication mode restores the CLR-serialized one-time-init guarantee the
        // base commit's static ctor had, WITHOUT reintroducing the type-load
        // TypeInitializationException risk (the Lazy lives per-source-instance and its factory
        // only runs on first PlayerNames access). The seam is unaffected: SetSource/ResetSource
        // swap to a fresh DefaultCombatLogSource (fresh Lazy), and InMemoryCombatLogSource
        // supplies PlayerNames directly without this lazy.
        private readonly Lazy<ISet<string>> _playerNames;

        public DefaultCombatLogSource()
        {
            _playerNames = new Lazy<ISet<string>>(
                LoadPlayerNames,
                LazyThreadSafetyMode.ExecutionAndPublication
            );
        }

        public ISet<string> PlayerNames => _playerNames.Value;

        private ISet<string> LoadPlayerNames()
        {
            if (!Directory.Exists(SettingsDirectory.FullName))
                return new HashSet<string>();

            return SettingsDirectory
                .EnumerateFiles("*PlayerGUIState.ini")
                .Select(x => SecondSegmentOrNull(x.Name))
                .Where(n => n is not null)
                .Select(n => n!)
                .ToHashSet();
        }

        public IEnumerable<CombatLog> EnumerateCombatLogs()
        {
            if (!Directory.Exists(CombatLogsDirectory.FullName))
                yield break;
            foreach (var fi in CombatLogsDirectory.EnumerateFiles("*.txt"))
                yield return new CombatLog(fi);
        }

        public CombatLog? GetLatestCombatLog()
        {
            if (!Directory.Exists(CombatLogsDirectory.FullName))
                return null;
            var fileInfo = CombatLogsDirectory
                .EnumerateFiles("*.txt")
                .MaxBy(x => x.LastWriteTimeUtc);
            return fileInfo is not null ? new CombatLog(fileInfo) : null;
        }
    }
}
