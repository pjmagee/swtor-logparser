using SwtorLogParser.Model;
using SwtorLogParser.Monitor;
using SwtorLogParser.View;

namespace SwtorLogParser.Tests;

[TestClass]
public class SlidingExpirationListTests
{
    private static CombatLogsMonitor.PlayerStats MakeStats(long id, string name, double dps, double hps)
    {
        var actor = Actor.Parse($"@{name}#{id}|(0,0,0,0)|(1/2)".AsMemory());
        Assert.IsNotNull(actor);
        Assert.AreEqual(id, actor.Id);
        return new CombatLogsMonitor.PlayerStats { Player = actor, DPS = dps, HPS = hps };
    }

    [TestMethod]
    public void AddOrUpdate_Inserts_New_Player()
    {
        var list = new SlidingExpirationList(TimeSpan.FromSeconds(30));

        list.AddOrUpdate(MakeStats(101, "Alpha", 100, 0));

        Assert.AreEqual(1, list.Items.Count());
        Assert.AreEqual(100, list.Items[0].DPS);
    }

    [TestMethod]
    public void AddOrUpdate_Updates_Existing_Player_By_Id()
    {
        var list = new SlidingExpirationList(TimeSpan.FromSeconds(30));

        list.AddOrUpdate(MakeStats(202, "Beta", 100, 0));
        list.AddOrUpdate(MakeStats(202, "Beta", 250, 0));

        Assert.AreEqual(1, list.Items.Count());
        Assert.AreEqual(250, list.Items[0].DPS);
    }

    [TestMethod]
    public void AddOrUpdate_Keeps_Distinct_Players_Separate()
    {
        var list = new SlidingExpirationList(TimeSpan.FromSeconds(30));

        list.AddOrUpdate(MakeStats(303, "Gamma", 100, 0));
        list.AddOrUpdate(MakeStats(404, "Delta", 200, 0));

        Assert.AreEqual(2, list.Items.Count);
    }

    [TestMethod]
    public async Task Item_Expires_After_Expiration_Window()
    {
        // Short window: the internal timer fires at the same cadence as the expiration,
        // so after a couple of intervals the stale entry is removed.
        var list = new SlidingExpirationList(TimeSpan.FromMilliseconds(50));

        list.AddOrUpdate(MakeStats(505, "Epsilon", 100, 0));
        Assert.AreEqual(1, list.Items.Count());

        // Poll up to ~2s for the timer-driven RemoveExpiredItems to clear the entry.
        var cleared = false;
        for (var i = 0; i < 40 && !cleared; i++)
        {
            await Task.Delay(50);
            cleared = list.Items.Count == 0;
        }

        Assert.IsTrue(cleared, "Expired entry was not removed within the expiration window.");
    }
}
