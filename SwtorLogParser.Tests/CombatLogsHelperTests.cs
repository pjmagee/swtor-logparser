using SwtorLogParser.Monitor;

namespace SwtorLogParser.Tests;

[TestClass]
public class CombatLogsHelperTests
{
    // BUG-04: the static ctor used Name.Split('_')[1], which IndexOutOfRange-d (surfaced as
    // TypeInitializationException) on a settings filename without '_'. The indexing logic is now
    // extracted into the pure SecondSegmentOrNull helper, unit-tested in isolation (no filesystem,
    // no CombatLogs.PlayerNames access — RESEARCH Pitfall 6).
    // Helper returns the verbatim second '_'-delimited segment (preserving the original
    // Split('_')[1] production semantics, extension included), or null when there is no '_'.
    [DataTestMethod]
    [DataRow("abc_def", "def")]
    [DataRow("nounderscores.ini", null)]
    [DataRow("a_b_c.ini", "b")]
    public void SecondSegmentOrNull_Returns_Expected(string fileName, string? expected)
    {
        Assert.AreEqual(expected, CombatLogs.SecondSegmentOrNull(fileName));
    }
}
