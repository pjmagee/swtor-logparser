using SwtorLogParser.Monitor;

namespace SwtorLogParser.Tests.Fixtures;

/// <summary>
/// Hermetic in-memory <see cref="ICombatLogSource"/> for tests: supplies a known
/// <see cref="PlayerNames"/> set and a known set of combat-log files written to a
/// per-instance temp directory. No real SWTOR folder is touched. Disposing removes
/// the temp directory (temp-file hygiene matching CombatLogsMonitorTests).
/// </summary>
public sealed class InMemoryCombatLogSource : ICombatLogSource, IDisposable
{
    private readonly string _tempDir;

    public InMemoryCombatLogSource(ISet<string>? playerNames = null)
    {
        PlayerNames = playerNames ?? new HashSet<string>();
        _tempDir = Path.Combine(Path.GetTempPath(), "swtorlp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        CombatLogsDirectory = new DirectoryInfo(_tempDir);
    }

    public ISet<string> PlayerNames { get; }

    public DirectoryInfo CombatLogsDirectory { get; }

    /// <summary>Write a *.txt combat-log file with the given lines into the temp logs directory.</summary>
    public void AddLogFile(string fileName, params string[] lines)
    {
        File.WriteAllLines(Path.Combine(_tempDir, fileName), lines);
        CombatLogsDirectory.Refresh();
    }

    public IEnumerable<CombatLog> EnumerateCombatLogs()
    {
        CombatLogsDirectory.Refresh();
        if (!CombatLogsDirectory.Exists) yield break;
        foreach (var fi in CombatLogsDirectory.EnumerateFiles("*.txt")) yield return new CombatLog(fi);
    }

    public CombatLog? GetLatestCombatLog()
    {
        CombatLogsDirectory.Refresh();
        if (!CombatLogsDirectory.Exists) return null;
        var fileInfo = CombatLogsDirectory.EnumerateFiles("*.txt").MaxBy(x => x.LastWriteTimeUtc);
        return fileInfo is not null ? new CombatLog(fileInfo) : null;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
