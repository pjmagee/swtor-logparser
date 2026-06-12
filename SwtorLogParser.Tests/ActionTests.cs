using Action = SwtorLogParser.Model.Action;

namespace SwtorLogParser.Tests;

[TestClass]
public class ActionTests
{
    [TestMethod]
    public void Event_And_Effect_Parsed()
    {
        var action = Action.Parse("AreaEntered {836045448953664}: Imperial Fleet {137438989504}".AsMemory());

        Assert.IsNotNull(action);
        Assert.IsFalse(action.Effect.IsNested);

        Assert.AreEqual("AreaEntered", action.Event.Name);
        Assert.AreEqual(836045448953664u, action.Event.Id);

        Assert.AreEqual("Imperial Fleet", action.Effect.Name);
        Assert.AreEqual(137438989504u, action.Effect.Id);
    }

    [TestMethod]
    public void Event_And_Effect_Nested_Parsed()
    {
        var action =
            Action.Parse(
                "DisciplineChanged {836045448953665}: Mercenary {16141111589108060476}/Bodyguard {2031339142381600}"
                    .AsMemory());

        Assert.IsNotNull(action);
        Assert.IsTrue(action.Effect.IsNested);

        Assert.AreEqual("DisciplineChanged", action.Event.Name);
        Assert.AreEqual(836045448953665u, action.Event.Id);

        Assert.AreEqual("Bodyguard", action.Effect.Name);
        Assert.AreEqual(2031339142381600u, action.Effect.Id);

        Assert.AreEqual("Mercenary", action.Effect.ParentName);
        Assert.AreEqual(16141111589108060476u, action.Effect.ParentId);
    }

    [TestMethod]
    public void Same_Actions_Are_Equal()
    {
        var action1 = Action.Parse("[ApplyEffect {836045448945477}: Damage {836045448945501}]".AsMemory());
        var action2 = Action.Parse("[ApplyEffect {836045448945477}: Damage {836045448945501}]".AsMemory());

        Assert.AreEqual(action1, action2);
    }

    // Graceful-null (Pattern C, green TODAY). Action.Parse wraps construction in try/catch: when a
    // child GameObject.Parse hits a non-numeric id brace it throws EAGERLY (GameObject.Parse reads
    // .Id), the exception is caught (Console.Error), and Action.Parse returns null. Distinct literal
    // per row — Action/GameObject caches key on Rom.GetHashCode and never clear between tests.
    [DataTestMethod]
    [DataRow("ZqxBadEvent {abc}: Imperial Fleet {137438989504}")]
    [DataRow("AreaEntered {836045448953664}: ZqxBadEffect {xyz}")]
    public void Action_Malformed_Inner_Fragment_Returns_Null(string raw)
    {
        Assert.IsNull(Action.Parse(raw.AsMemory()));
    }
}
