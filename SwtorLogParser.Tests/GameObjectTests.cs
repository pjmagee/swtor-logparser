using SwtorLogParser.Model;

namespace SwtorLogParser.Tests;

[TestClass]
public class GameObjectTests
{
    [TestMethod]
    public void Name_And_Id_Parsed()
    {
        var name = "ApplyEffect";
        var id = 836045448945477u;

        var gameObject = GameObject.Parse($"{name} {{{id}}}".AsMemory());

        Assert.IsNotNull(gameObject);
        Assert.AreEqual(id, gameObject.Id);
        Assert.AreEqual(name, gameObject.Name);
    }

    // RFCT-03 (Phase 3): the parse caches are now keyed by string CONTENT (rom.ToString()), not by
    // identity-based ReadOnlyMemory<char>.GetHashCode. The Phase-1 version of this test locked the
    // OLD contract (distinct backing memory => NOT equal). RFCT-03 intentionally inverts that: two
    // roms with identical CONTENT but different backing memory now resolve to the SAME cached
    // instance (the ME-02 dedup fix). This test is re-characterized to lock the new content-keyed
    // contract — same content => same cached instance (ReferenceEquals) regardless of backing memory.
    [TestMethod]
    public void Game_Objects_Equality_Reflects_Content_Key()
    {
        var name = "ApplyEffect";
        var id = 836045448945477u;

        // Two DISTINCT backing strings with identical content -> same content key -> SAME instance.
        var a = GameObject.Parse($"{name} {{{id}}}".AsMemory());
        var b = GameObject.Parse($"{name} {{{id}}}".AsMemory());
        Assert.IsNotNull(a);
        Assert.IsNotNull(b);
        Assert.AreSame(a, b);
        Assert.AreEqual(a, b);

        // Same backing memory reused -> still the same cached instance.
        var shared = $"{name} {{{id}}}".AsMemory();
        var c = GameObject.Parse(shared);
        var d = GameObject.Parse(shared);
        Assert.IsNotNull(c);
        Assert.AreSame(c, d);
    }

    [TestMethod]
    public void Invalid_Id_Not_Parsed()
    {
        var name = "ApplyEffect";
        var id = 836045448945477u;

        Assert.IsNull(GameObject.Parse($"{name} {{{id}".AsMemory()));
        Assert.IsNull(GameObject.Parse($"{name} {id}}}".AsMemory()));
    }

    [TestMethod]
    public void Nested_Is_Parsed()
    {
        var name = "Darkness";
        var id = 2031339142381582u;
        var gameObject = GameObject.Parse($"Assassin {16141163438392504574}/{name} {{{id}}}".AsMemory());
        Assert.IsNotNull(gameObject);
        Assert.IsTrue(gameObject.IsNested);
        Assert.AreEqual(id, gameObject.Id);
        Assert.AreEqual(name, gameObject.Name);
    }

    // Phase 2: now returns null (BUG-05). GameObject.Parse reads .Id eagerly
    // (GameObject.cs:107 -> GetId), and GetId now uses ulong.TryParse, so non-numeric
    // brace content yields a null Id and Parse returns null instead of throwing. (TEST-03)
    [TestMethod]
    public void GameObject_NonNumeric_Id_Throws_Today()
    {
        // Unique literal so the static GameObjectCache cannot serve a cached object.
        Assert.IsNull(GameObject.Parse("WidgetEager {abc}".AsMemory()));
    }

    // Brace-edge inputs the IndexOf('{')/IndexOf('}') != -1 guard catches -> Parse returns null
    // cleanly TODAY (no throw). Distinct literal per row so the cache does not mask the parse path.
    [DataTestMethod]
    [DataRow("BraceWidgetA {123")]   // no closing brace
    [DataRow("BraceWidgetB 123}")]   // no opening brace
    [DataRow("BraceWidgetC")]        // no braces at all
    public void GameObject_Malformed_Braces_Return_Null(string raw)
    {
        Assert.IsNull(GameObject.Parse(raw.AsMemory()));
    }

    // Names containing [ ] @ : before the numeric brace are sliced (GetName: 0..IndexOf('{')-1, Trim)
    // without an IndexOutOfRange escape. Each row uses a UNIQUE id so the cache cannot collide. (TEST-03)
    [DataTestMethod]
    [DataRow("Name [bracket] {836045448945401}", "Name [bracket]")]
    [DataRow("Name @at {836045448945402}", "Name @at")]
    [DataRow("Name :colon {836045448945403}", "Name :colon")]
    public void GameObject_Name_With_Delimiters_Is_Parsed(string raw, string expectedName)
    {
        var go = GameObject.Parse(raw.AsMemory());
        Assert.IsNotNull(go);
        Assert.AreEqual(expectedName, go.Name);
    }

    // Golden-line regression lock: a known-good non-nested object, all fields asserted.
    // Unique id not used by any other test in this file.
    [TestMethod]
    public void GameObject_Golden_All_Fields()
    {
        var go = GameObject.Parse("Imperial Fleet {137438989504}".AsMemory());

        Assert.IsNotNull(go);
        Assert.IsFalse(go.IsNested);
        Assert.AreEqual("Imperial Fleet", go.Name);
        Assert.AreEqual(137438989504u, go.Id);
    }

    // BUG-06 concurrency smoke test: N threads concurrently calling GameObject.Parse on the SAME
    // shared backing memory must never throw (no InvalidOperationException / torn ConcurrentDictionary)
    // and must converge on a SINGLE cached instance (first-writer-wins). Type-consistent literal only
    // (GameObject, never mixed with Ability on the same backing memory — RESEARCH Pitfall 4).
    [TestMethod]
    public void GameObject_Concurrent_Parse_Same_Memory_Single_Instance()
    {
        // Literal UNIQUE to this test so the static GameObjectCache cannot pre-serve it.
        var shared = "ZqxConcurrencyWidget {137438989777}".AsMemory();

        var results = new GameObject?[256];
        Exception? ex = null;
        try
        {
            Parallel.For(0, results.Length, i => results[i] = GameObject.Parse(shared));
        }
        catch (Exception e)
        {
            ex = e;
        }

        Assert.IsNull(ex);
        foreach (var r in results) Assert.IsNotNull(r);

        // First-writer-wins: every reference must be the same cached instance.
        var first = results[0];
        foreach (var r in results) Assert.AreSame(first, r);
    }
}
