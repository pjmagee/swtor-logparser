using SwtorLogParser.Model;
using SwtorLogParser.Monitor;

namespace SwtorLogParser.Tests;

// PERF-01 Wave-0 contract tests. These are written BEFORE the CombatLog.cs refactor and
// MUST pass against the CURRENT (unrefactored) code — they lock the pre-refactor behavior so
// the optimization cannot drift the GetLogLines() output set, the CRLF parity, or the
// ToString() count semantics.
[TestClass]
public class CombatLogReadTests
{
    // A real, parseable player line shape (mirrors CombatLogsMonitorTests / DpsHpsMathTests).
    private const string ValidLineA =
        "[18:12:13] [Powerful Subscriber 688623358308676 (1/401177)] [] [] [AreaEntered {836045448953664}: Imperial Fleet {137438989504}]";

    private const string ValidLineB =
        "[20:33:17.759] [@Aegrae#689921479616853|(422.51,620.88,33.46,84.27)|(300469/379924)] [=] [Progressive Scan {3394132265402368}] [ApplyEffect {836045448945477}: Heal {836045448945500}] (8622) <3880>";

    // Fewer than 5 bracket sections => CombatLogLine.Parse rejects it (returns null).
    private const string MalformedLine = "[18:00:00] [only] [three] [sections]";

    // TEST: GetLogLines() yields the same set of parsed CombatLogLine objects. The fixture mixes
    // CRLF and LF terminators, contains a blank line in the middle, and a malformed (unparseable)
    // line. The returned list must contain exactly the valid lines, parsed identically.
    [TestMethod]
    public void GetLogLines_Yields_Same_Parsed_Lines()
    {
        var path = Path.Combine(Path.GetTempPath(), $"swtor_combatlog_{Guid.NewGuid():N}.txt");

        // Hand-build mixed line endings: ValidLineA terminated by CRLF, blank line (CRLF),
        // ValidLineB terminated by LF, malformed line terminated by LF (no final terminator).
        var content = ValidLineA + "\r\n" + "\r\n" + ValidLineB + "\n" + MalformedLine + "\n";

        try
        {
            File.WriteAllText(path, content);

            var combatLog = new CombatLog(new FileInfo(path));
            var parsed = combatLog.GetLogLines();

            // Exactly the two valid lines parse; blank + malformed are dropped.
            Assert.AreEqual(2, parsed.Count);

            // Content parity: the parsed lines reconstruct the same ToString() as parsing the raw
            // lines directly (locks the slice content fed to Parse).
            var expectedA = CombatLogLine.Parse(ValidLineA.AsMemory());
            var expectedB = CombatLogLine.Parse(ValidLineB.AsMemory());
            Assert.IsNotNull(expectedA);
            Assert.IsNotNull(expectedB);

            Assert.AreEqual(expectedA!.ToString(), parsed[0].ToString());
            Assert.AreEqual(expectedB!.ToString(), parsed[1].ToString());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // TEST: the splitter/slice path must match MemoryExtensions.EnumerateLines — specifically, a
    // CRLF-terminated value-bearing line must NOT carry a trailing '\r' into Parse. This is the
    // actual landmine from 04-RESEARCH (CRLF parity). We drive a CRLF-terminated line through the
    // production GetLogLines() path and assert the parsed result is byte-identical to parsing the
    // same line WITHOUT the '\r' — i.e. the terminator was excluded from the slice.
    [TestMethod]
    public void Splitter_Matches_EnumerateLines()
    {
        // Cross-check the expected line set against EnumerateLines over a mixed fixture: collect
        // every non-empty line (mirroring the old `if (line.IsEmpty) continue;`).
        const string text = "first\r\nsecond\nthird\rfourth\r\n\r\nfifth";
        var expectedLines = new List<string>();
        foreach (var line in text.AsSpan().EnumerateLines())
        {
            if (line.IsEmpty) continue;
            expectedLines.Add(line.ToString());
        }
        // No emitted line retains a trailing '\r' (EnumerateLines excludes the terminator).
        Assert.IsFalse(expectedLines.Any(l => l.EndsWith('\r')));
        CollectionAssert.AreEqual(new[] { "first", "second", "third", "fourth", "fifth" }, expectedLines);

        // Now prove the production read path strips CRLF identically: write a CRLF-terminated
        // value-bearing line and confirm GetLogLines() parses it the same as the \r-free form.
        var path = Path.Combine(Path.GetTempPath(), $"swtor_combatlog_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, ValidLineB + "\r\n");

            var combatLog = new CombatLog(new FileInfo(path));
            var parsed = combatLog.GetLogLines();

            Assert.AreEqual(1, parsed.Count());

            // Parsing ValidLineB WITH a trailing '\r' would change the last section's slice; parsing
            // WITHOUT it is the reference. The production path must equal the reference (no \r).
            var reference = CombatLogLine.Parse(ValidLineB.AsMemory());
            Assert.IsNotNull(reference);
            Assert.AreEqual(reference!.ToString(), parsed[0].ToString());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // TEST: ToString() reports the file name and a line count. Per 04-RESEARCH Pitfall 1 / A1, the
    // exact integer is diagnostic-only (no test pins it elsewhere). Locked PERF-01 count semantics:
    // "non-empty lines" — so over a fixture with 2 valid lines + 1 blank line the count is 2.
    [TestMethod]
    public void ToString_Reports_Line_Count()
    {
        var path = Path.Combine(Path.GetTempPath(), $"swtor_combatlog_{Guid.NewGuid():N}.txt");
        // Two valid (non-empty, parseable) lines plus a blank line in the middle.
        var content = ValidLineA + "\r\n" + "\r\n" + ValidLineB + "\n";

        try
        {
            File.WriteAllText(path, content);

            var fileInfo = new FileInfo(path);
            var combatLog = new CombatLog(fileInfo);
            var rendered = combatLog.ToString();

            // Shape: "{FileInfo.Name}: {count}" where count is the number of non-empty lines (2).
            Assert.AreEqual($"{fileInfo.Name}: 2", rendered);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
