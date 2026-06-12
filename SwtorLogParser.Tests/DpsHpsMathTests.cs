using Microsoft.Extensions.Logging.Abstractions;
using SwtorLogParser.Extensions;
using SwtorLogParser.Model;
using SwtorLogParser.Monitor;

namespace SwtorLogParser.Tests;

// TEST-02: Unit-test the DPS/HPS math (Accumulator + CalculateDpsHpsStats) DIRECTLY against
// known CombatLogLine inputs. Calling these internal methods directly (via the existing
// InternalsVisibleTo(SwtorLogParser.Tests) grant) BYPASSES the DpsHps pipeline's
// `.Where(x => x.TimeStamp > DateTime.Now.AddSeconds(-10))` filter, so fixed/relative
// timestamps are deterministic here and exact numbers can be asserted.
//
// These tests LOCK the CURRENT product behavior (PERF-03 accumulator rewrite is Phase 4) —
// they must not assert any "improved" rounding/averaging.
[TestClass]
public class DpsHpsMathTests
{
    // Build a player-damage line whose timestamp is a controlled time-of-day so deltas between
    // lines are exact. Distinct text per line keeps each CombatLogLine distinct in the HashSet
    // (CombatLogLineComparer hashes on the raw Rom content).
    // Crit is encoded as a `*` inside the value parentheses (CombatLogs.Critical = "*"), e.g.
    // `(1000*)`. A distinct trailing threat keeps each raw line distinct in the HashSet.
    private static CombatLogLine DamageLine(string time, int value, bool critical)
    {
        var crit = critical ? "*" : string.Empty;
        var raw =
            $"[{time}] [@Aegrae#689921479616853|(422.51,620.88,33.46,84.27)|(300469/379924)] [=] "
            + "[Progressive Scan {3394132265402368}] "
            + $"[ApplyEffect {{836045448945477}}: Damage {{836045448945501}}] ({value}{crit})";

        var line = CombatLogLine.Parse(raw.AsMemory());
        Assert.IsNotNull(line);
        Assert.IsTrue(line!.IsPlayerDamage(), "test line must satisfy IsPlayerDamage()");
        Assert.AreEqual(critical, line.Value!.IsCritical);
        return line;
    }

    private static CombatLogLine HealLine(string time, int value, bool critical)
    {
        var crit = critical ? "*" : string.Empty;
        var raw =
            $"[{time}] [@Aegrae#689921479616853|(422.51,620.88,33.46,84.27)|(300469/379924)] [=] "
            + "[Progressive Scan {3394132265402368}] "
            + $"[ApplyEffect {{836045448945477}}: Heal {{836045448945500}}] ({value}{crit})";

        var line = CombatLogLine.Parse(raw.AsMemory());
        Assert.IsNotNull(line);
        Assert.IsTrue(line!.IsPlayerHeal(), "test line must satisfy IsPlayerHeal()");
        Assert.AreEqual(critical, line.Value!.IsCritical);
        return line;
    }

    private static CombatLogsMonitor NewMonitor() =>
        new(NullLogger<CombatLogsMonitor>.Instance);

    // DPS = damageTotal / (last - first).TotalSeconds, computed directly (bypasses DateTime.Now).
    // Two damage lines 1.000s apart: totals 1000 + 2000 = 3000 over 1.0s => DPS = 3000.
    [TestMethod]
    public void Dps_Computed_From_Known_Damage()
    {
        var monitor = NewMonitor();
        var state = new HashSet<CombatLogLine>(new CombatLogLineComparer());

        state = monitor.Accumulator(state, DamageLine("20:00:00.000", 1000, critical: false));
        state = monitor.Accumulator(state, DamageLine("20:00:01.000", 2000, critical: false));

        var stats = monitor.CalculateDpsHpsStats(state);

        Assert.IsNotNull(stats.DPS);
        Assert.AreEqual(3000d, stats.DPS!.Value, 1e-3); // 3000 over 1.0s
        Assert.IsNull(stats.HPS); // no heals => null
    }

    // HPS = healTotal / (last - first).TotalSeconds. Two heals 1.0s apart: 500 + 1500 over 1.0s => 2000.
    [TestMethod]
    public void Hps_Computed_From_Known_Heals()
    {
        var monitor = NewMonitor();
        var state = new HashSet<CombatLogLine>(new CombatLogLineComparer());

        state = monitor.Accumulator(state, HealLine("20:00:00.000", 500, critical: false));
        state = monitor.Accumulator(state, HealLine("20:00:01.000", 1500, critical: false));

        var stats = monitor.CalculateDpsHpsStats(state);

        Assert.IsNotNull(stats.HPS);
        Assert.AreEqual(2000d, stats.HPS!.Value, 1e-3); // 2000 over 1.0s
        Assert.IsNull(stats.DPS); // no damage => null
    }

    // Crit% = count(IsCritical) / state.Count * 100. One of two damage lines is critical => 50%.
    // The current code maps exactly-zero/infinity crit to null, so a non-crit-only stream yields null.
    [TestMethod]
    public void Crit_Percent_Computed()
    {
        var monitor = NewMonitor();
        var state = new HashSet<CombatLogLine>(new CombatLogLineComparer());

        state = monitor.Accumulator(state, DamageLine("20:00:00.000", 1000, critical: true));
        state = monitor.Accumulator(state, DamageLine("20:00:01.000", 1000, critical: false));

        var stats = monitor.CalculateDpsHpsStats(state);

        // 1 critical of 2 total lines => 1/2*100 = 50.
        Assert.IsNotNull(stats.DPSCritP);
        Assert.AreEqual(50d, stats.DPSCritP!.Value, 1e-3);
        // No heals => hps crit numerator is 0 => current code maps 0.0 to null.
        Assert.IsNull(stats.HPSCritP);
    }

    // Zero crit maps to null (locks the `dpsCrit == 0.0d ? null` branch of the current code).
    [TestMethod]
    public void Zero_Crit_Maps_To_Null()
    {
        var monitor = NewMonitor();
        var state = new HashSet<CombatLogLine>(new CombatLogLineComparer());

        state = monitor.Accumulator(state, DamageLine("20:00:00.000", 1000, critical: false));
        state = monitor.Accumulator(state, DamageLine("20:00:01.000", 1000, critical: false));

        var stats = monitor.CalculateDpsHpsStats(state);

        Assert.IsNull(stats.DPSCritP);
        Assert.IsNull(stats.HPSCritP);
    }

    // The 10s sliding window in Accumulator removes any line older than newLine.TimeStamp - 10s.
    // Seed a line at t0, then Accumulator a new line 11s later: the old line is removed, new kept.
    [TestMethod]
    public void Window_Expiry_Removes_Old_Lines()
    {
        var monitor = NewMonitor();
        var state = new HashSet<CombatLogLine>(new CombatLogLineComparer());

        var oldLine = DamageLine("20:00:00.000", 1000, critical: false);
        var newLine = DamageLine("20:00:11.000", 2000, critical: false); // 11s later (> 10s)

        state = monitor.Accumulator(state, oldLine);
        Assert.IsTrue(state.Contains(oldLine)); // present before expiry

        state = monitor.Accumulator(state, newLine);

        Assert.IsFalse(state.Contains(oldLine)); // 10s RemoveWhere evicted the old line
        Assert.IsTrue(state.Contains(newLine)); // the new line is kept
        Assert.AreEqual(1, state.Count());
    }

    // A line exactly within the 10s window survives (boundary: 9s < 10s delta is kept).
    [TestMethod]
    public void Window_Keeps_Recent_Lines()
    {
        var monitor = NewMonitor();
        var state = new HashSet<CombatLogLine>(new CombatLogLineComparer());

        var recentLine = DamageLine("20:00:00.000", 1000, critical: false);
        var newLine = DamageLine("20:00:09.000", 2000, critical: false); // 9s later (< 10s)

        state = monitor.Accumulator(state, recentLine);
        state = monitor.Accumulator(state, newLine);

        Assert.IsTrue(state.Contains(recentLine)); // within 10s => kept
        Assert.IsTrue(state.Contains(newLine));
        Assert.AreEqual(2, state.Count);
    }

    // PERF-03 Wave-0: lock the `state.Count <= 1 ⇒ timeSpan == TimeSpan.FromSeconds(1)` branch
    // explicitly (previously only covered transitively). With a SINGLE line, the divisor is 1.0s,
    // so DPS == the single line's value. The single-pass rewrite must preserve this exactly.
    [TestMethod]
    public void Single_Line_Uses_OneSecond_Window()
    {
        var monitor = NewMonitor();
        var state = new HashSet<CombatLogLine>(new CombatLogLineComparer());

        state = monitor.Accumulator(state, DamageLine("20:00:00.000", 1500, critical: false));

        var stats = monitor.CalculateDpsHpsStats(state);

        Assert.AreEqual(1, state.Count());
        Assert.IsNotNull(stats.DPS);
        Assert.AreEqual(1500d, stats.DPS!.Value, 1e-3); // one line => divisor is 1.0s => DPS == value
        Assert.IsNull(stats.HPS); // no heals => null
    }

    // Regression: two (or more) events sharing the EXACT same timestamp make maxStamp == minStamp, so the
    // window is zero-length. Without the guard, total / 0 = Infinity HPS/DPS (the reported bug). The guard
    // floors a zero-length window to 1s, so the result is finite.
    [TestMethod]
    public void Same_Timestamp_Events_Do_Not_Produce_Infinity()
    {
        var monitor = NewMonitor();
        var state = new HashSet<CombatLogLine>(new CombatLogLineComparer());

        // Distinct values keep the two raw lines distinct in the HashSet, but the timestamp is identical.
        state = monitor.Accumulator(state, HealLine("20:00:00.000", 500, critical: false));
        state = monitor.Accumulator(state, HealLine("20:00:00.000", 1500, critical: false));

        Assert.AreEqual(2, state.Count); // both kept (> 1) => a zero-length window

        var stats = monitor.CalculateDpsHpsStats(state);

        Assert.IsNotNull(stats.HPS);
        Assert.IsFalse(double.IsInfinity(stats.HPS!.Value)); // the bug produced Infinity here
        Assert.AreEqual(2000d, stats.HPS!.Value, 1e-3); // (500 + 1500) over the 1s floor
    }
}
