using SwtorLogParser.Monitor;
using SwtorLogParser.Tests.Fixtures;

namespace SwtorLogParser.Tests;

// TEST-01/TEST-02 (Phase 3 Plan 05): the filesystem-hermeticity seam.
// CombatLogs's filesystem access (logs enumeration + PlayerNames) now lives behind an
// injectable ICombatLogSource guarded by Directory.Exists, so touching any CombatLogs member
// no longer throws TypeInitializationException/DirectoryNotFoundException when the SWTOR
// folders are absent (Pitfall 6). The static facade is preserved for hosts.
public class CombatLogSourceTests
{
    // Static_Ctor_Does_Not_Throw_When_Settings_Absent: touching CombatLogs.PlayerNames must
    // not throw even when the real settings/logs directories are absent. The default source
    // guards every directory read with Directory.Exists and yields an empty set instead.
    [Fact]
    public void Static_Ctor_Does_Not_Throw_When_Settings_Absent()
    {
        var ex = Record.Exception(() =>
        {
            _ = CombatLogs.PlayerNames;
            _ = CombatLogs.EnumerateCombatLogs().ToList();
            _ = CombatLogs.GetLatestCombatLog();
        });

        Assert.Null(ex);
    }

    // Default_Seam_Wraps_Real_Paths: the default source still resolves the real %Documents%
    // SWTOR path (behavior unchanged for hosts) — the default CombatLogsDirectory path still
    // ends with the expected SWTOR subpath.
    [Fact]
    public void Default_Seam_Wraps_Real_Paths()
    {
        CombatLogs.ResetSource();

        var expectedSuffix = Path.Combine("Star Wars - The Old Republic", "CombatLogs");
        Assert.EndsWith(expectedSuffix, CombatLogs.CombatLogsDirectory.FullName);
    }

    // Test_Source_Is_Injectable: a test can install an in-memory source supplying a known
    // PlayerNames set and a known set of combat-log files, then read them back through the
    // static facade. Restore the default source afterward to avoid cross-test leakage.
    [Fact]
    public void Test_Source_Is_Injectable()
    {
        using var fixture = new InMemoryCombatLogSource(new HashSet<string> { "Aegrae" });
        fixture.AddLogFile("combat_2024-01-01.txt", "line one", "line two");

        try
        {
            CombatLogs.SetSource(fixture);

            Assert.Contains("Aegrae", CombatLogs.PlayerNames);
            Assert.Single(CombatLogs.EnumerateCombatLogs());
            Assert.NotNull(CombatLogs.GetLatestCombatLog());
        }
        finally
        {
            CombatLogs.ResetSource();
        }
    }

    // ResetSource restores the real default source after a test swap.
    [Fact]
    public void ResetSource_Restores_Default()
    {
        using var fixture = new InMemoryCombatLogSource(new HashSet<string> { "TempName" });
        CombatLogs.SetSource(fixture);
        Assert.Contains("TempName", CombatLogs.PlayerNames);

        CombatLogs.ResetSource();

        Assert.DoesNotContain("TempName", CombatLogs.PlayerNames);
    }
}
