using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SwtorLogParser.Extensions;
using SwtorLogParser.Model;
using SwtorLogParser.Monitor;

namespace SwtorLogParser.Tests;

public class CombatLogsMonitorTests
{
    // TEST-01: build a player-damage line with a NOW-RELATIVE timestamp so the
    // DpsHps pipeline's `.Where(x => x.TimeStamp > DateTime.Now.AddSeconds(-10))` window
    // (CombatLogsMonitor.ConfigureObservables) does NOT silently drop it. A fixed literal
    // timestamp like [20:33:17.759] would be dropped by that filter.
    private static CombatLogLine NowRelativePlayerDamageLine()
    {
        var ts = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var raw =
            $"[{ts}] [@Aegrae#689921479616853|(422.51,620.88,33.46,84.27)|(300469/379924)] [=] "
            + "[Progressive Scan {3394132265402368}] "
            + "[ApplyEffect {836045448945477}: Damage {836045448945501}] (8622) <3880>";

        var line = CombatLogLine.Parse(raw.AsMemory());
        Assert.NotNull(line);
        Assert.True(line!.IsPlayerDamage(), "test line must satisfy IsPlayerDamage()");
        return line;
    }

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

    // TEST-01 (BUG-01 deferred from Phase 2): after Start the Rx pipeline must deliver
    // PlayerStats for a pushed player-damage line. Constructed via the public ctor (not the
    // singleton) to avoid leaking state across tests. Subject<T>.OnNext is synchronous, so
    // delivery lands on the calling thread — no scheduler/sleep needed for this assertion.
    [Fact]
    public void Start_Then_Push_Delivers()
    {
        var monitor = new CombatLogsMonitor(NullLogger<CombatLogsMonitor>.Instance);
        var received = new List<CombatLogsMonitor.PlayerStats>();
        using var _ = monitor.DpsHps.Subscribe(received.Add);

        monitor.Start(CancellationToken.None);
        monitor.PublishForTest(NowRelativePlayerDamageLine());

        Assert.NotEmpty(received);
        // Assert delivery + player identity only — exact DPS numbers are time-dependent (TEST-02).
        Assert.All(received, s => Assert.NotNull(s.Player));

        monitor.Stop();
    }

    // TEST-01: Stop must halt the running monitor so the file-tailing feed (the real source that
    // pushes parsed lines into the Subject) stops. We assert via IsRunning that the started reader/
    // monitor tasks are torn down by Stop().
    //
    // NOTE: Stop() cancels the file-reading tasks (Phase 2 cancellation wiring) but, by design, does
    // NOT complete/dispose the Rx Subject — the DpsHps pipeline is intentionally independent of
    // Start/Stop and the locked constraint forbids changing the Rx semantics here. The PublishForTest
    // seam injects directly into that Subject, so it bypasses the reader the way the live game writer
    // never could; therefore we verify "Stop halts the feed" through IsRunning (the reader that feeds
    // the Subject), not by pushing through the bypass seam after Stop.
    [Fact]
    public void Stop_Halts_Delivery()
    {
        var monitor = new CombatLogsMonitor(NullLogger<CombatLogsMonitor>.Instance);
        var received = new List<CombatLogsMonitor.PlayerStats>();
        using var _ = monitor.DpsHps.Subscribe(received.Add);

        monitor.Start(CancellationToken.None);
        monitor.PublishForTest(NowRelativePlayerDamageLine());
        Assert.NotEmpty(received); // pipeline delivers while running

        monitor.Stop();
        Thread.Sleep(50); // let cooperative cancellation settle

        // The reader/monitor tasks that feed the Subject are torn down — the live feed has halted.
        Assert.False(monitor.IsRunning);
    }

    // TEST-01: a second Start (after Stop) must not throw — exercises the linked-CTS
    // dispose/recreate path in Start().
    [Fact]
    public void Second_Start_Does_Not_Throw()
    {
        var monitor = new CombatLogsMonitor(NullLogger<CombatLogsMonitor>.Instance);

        var ex = Record.Exception(() =>
        {
            monitor.Start(CancellationToken.None);
            monitor.Stop();
            monitor.Start(CancellationToken.None);
        });

        Assert.Null(ex);
        monitor.Stop();
    }
}
