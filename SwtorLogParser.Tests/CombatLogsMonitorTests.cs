using Microsoft.Extensions.Logging.Abstractions;
using SwtorLogParser.Monitor;

namespace SwtorLogParser.Tests;

public class CombatLogsMonitorTests
{
    // RFCT-02: the ILogger<CombatLogsMonitor> ctor is now public, so the monitor can be
    // constructed directly (for DI/tests) without going through the singleton. The
    // ConfigureObservables() chain still runs via the parameterless ctor, so DpsHps is wired.
    [Fact]
    public void Monitor_Constructs_Via_Public_Ctor()
    {
        var monitor = new CombatLogsMonitor(NullLogger<CombatLogsMonitor>.Instance);

        Assert.NotNull(monitor);
        Assert.NotNull(monitor.DpsHps);
    }

    // RFCT-02: Instance is now defined unconditionally (NullLogger-backed) in every build
    // configuration, closing the #if RELEASE/#elif DEBUG gap that left it undefined elsewhere.
    [Fact]
    public void Instance_Is_Defined()
    {
        Assert.NotNull(CombatLogsMonitor.Instance);
    }

    // BUG-02: Stop() before Start() must be a safe no-op. The _cancellationTokenSource is now
    // nullable and Stop() uses ?.Cancel() with null-safe logging, so calling Stop() on a freshly
    // obtained (never-started) monitor does not throw NullReferenceException.
    [Fact]
    public void Stop_Before_Start_Does_Not_Throw()
    {
        var ex = Record.Exception(() => CombatLogsMonitor.Instance.Stop());
        Assert.Null(ex);
    }

    // BUG-07: CombatLog.GetLogLines() opens the file with FileAccess.Read (least privilege) while
    // keeping FileShare.ReadWrite so the live SWTOR writer is not blocked. This test proves the read
    // succeeds AND tolerates a concurrent writer holding the file open with FileShare.ReadWrite.
    [Fact]
    public void GetLogLines_Opens_ReadOnly_And_Reads()
    {
        var path = Path.Combine(Path.GetTempPath(), $"swtor_combatlog_{Guid.NewGuid():N}.txt");
        var lines = new[]
        {
            "[18:12:13] [Powerful Subscriber 688623358308676 (1/401177)] [] [] [AreaEntered {836045448953664}: Imperial Fleet {137438989504}]",
            "[20:33:17.759] [@Aegrae#689921479616853|(422.51,620.88,33.46,84.27)|(300469/379924)] [=] [Progressive Scan {3394132265402368}] [ApplyEffect {836045448945477}: Heal {836045448945500}] (8622) <3880>",
        };

        try
        {
            File.WriteAllLines(path, lines);

            // Hold the file open with a concurrent writer (FileShare.ReadWrite) to prove the read
            // does not require exclusive access and the live game writer would not be blocked.
            using (var concurrentWriter = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            {
                var combatLog = new CombatLog(new FileInfo(path));
                var parsed = combatLog.GetLogLines();

                Assert.Equal(lines.Length, parsed.Count);
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
