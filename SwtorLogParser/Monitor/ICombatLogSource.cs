namespace SwtorLogParser.Monitor;

/// <summary>
/// Injectable filesystem seam behind the static <see cref="CombatLogs"/> facade. The default
/// implementation wraps the real SWTOR <c>%Documents%</c>/<c>%LocalAppData%</c> paths; tests
/// inject an in-memory/temp fixture so the deferred Phase 1 filesystem tests become hermetic.
/// <para>
/// AOT note: this is a plain interface with a constructor-injected default — no DI container,
/// no reflection — so the core library stays <c>IsAotCompatible=true</c>.
/// </para>
/// </summary>
public interface ICombatLogSource
{
    /// <summary>The combat-logs directory (used by the monitor for Refresh()/LastWriteTime).</summary>
    DirectoryInfo CombatLogsDirectory { get; }

    /// <summary>Player names sourced from the settings <c>*PlayerGUIState.ini</c> files. Empty when absent.</summary>
    ISet<string> PlayerNames { get; }

    /// <summary>Enumerate the <c>*.txt</c> combat-log files. Empty when the directory is absent.</summary>
    IEnumerable<CombatLog> EnumerateCombatLogs();

    /// <summary>The most-recently-written combat-log file, or <c>null</c> when none/absent.</summary>
    CombatLog? GetLatestCombatLog();
}
