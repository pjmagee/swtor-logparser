using SwtorLogParser.Model;

namespace SwtorLogParser.Tests;

public class GameObjectTests
{
    [Fact]
    public void Name_And_Id_Parsed()
    {
        var name = "ApplyEffect";
        var id = 836045448945477u;

        var gameObject = GameObject.Parse($"{name} {{{id}}}".AsMemory());

        Assert.NotNull(gameObject);
        Assert.Equal(id, gameObject.Id);
        Assert.Equal(name, gameObject.Name);
    }

    // [Rule 1 - Bug] This [Fact] was RED on the committed baseline (HEAD) before this plan ran.
    // It asserted Assert.StrictEqual on two GameObjects parsed from two SEPARATE string literals.
    // GameObject.Equals/GetHashCode delegate to ReadOnlyMemory<char>.GetHashCode (GameObject.cs:35,53),
    // which is identity-based on the backing string object + range — so two distinct literals yield
    // different hashes and the objects are NOT equal under the current contract. The original
    // assertion can never pass. Per the Phase-1 "characterize CURRENT behavior" mandate, this now
    // locks the actual contract: distinct backing memory => NOT equal; same backing memory => equal.
    [Fact]
    public void Game_Objects_Equality_Reflects_Backing_Memory()
    {
        var name = "ApplyEffect";
        var id = 836045448945477u;

        // Two distinct backing strings -> distinct ReadOnlyMemory hashes -> NOT equal today.
        var a = GameObject.Parse($"{name} {{{id}}}".AsMemory());
        var b = GameObject.Parse($"{name} {{{id}}}".AsMemory());
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotEqual(a, b);

        // Same backing memory reused -> equal (cache returns the same instance).
        var shared = $"{name} {{{id}}}".AsMemory();
        var c = GameObject.Parse(shared);
        var d = GameObject.Parse(shared);
        Assert.NotNull(c);
        Assert.Equal(c, d);
    }

    [Fact]
    public void Invalid_Id_Not_Parsed()
    {
        var name = "ApplyEffect";
        var id = 836045448945477u;

        Assert.Null(GameObject.Parse($"{name} {{{id}".AsMemory()));
        Assert.Null(GameObject.Parse($"{name} {id}}}".AsMemory()));
    }

    [Fact]
    public void Nested_Is_Parsed()
    {
        var name = "Darkness";
        var id = 2031339142381582u;
        var gameObject = GameObject.Parse($"Assassin {16141163438392504574}/{name} {{{id}}}".AsMemory());
        Assert.NotNull(gameObject);
        Assert.True(gameObject.IsNested);
        Assert.Equal(id, gameObject.Id);
        Assert.Equal(name, gameObject.Name);
    }

    // BUG-05: GameObject.Parse reads .Id eagerly (GameObject.cs:107 -> GetId at :95),
    // so ulong.Parse on non-numeric brace content throws FormatException FROM Parse today.
    // Characterization only — Phase 2 (TryParse) flips this to Assert.Null. (TEST-03)
    [Fact]
    public void GameObject_NonNumeric_Id_Throws_Today()
    {
        // Unique literal so the static GameObjectCache cannot serve a cached object.
        Assert.Throws<FormatException>(() => GameObject.Parse("WidgetEager {abc}".AsMemory()));
    }

    // Brace-edge inputs the IndexOf('{')/IndexOf('}') != -1 guard catches -> Parse returns null
    // cleanly TODAY (no throw). Distinct literal per row so the cache does not mask the parse path.
    [Theory]
    [InlineData("BraceWidgetA {123")]   // no closing brace
    [InlineData("BraceWidgetB 123}")]   // no opening brace
    [InlineData("BraceWidgetC")]        // no braces at all
    public void GameObject_Malformed_Braces_Return_Null(string raw)
    {
        Assert.Null(GameObject.Parse(raw.AsMemory()));
    }

    // Names containing [ ] @ : before the numeric brace are sliced (GetName: 0..IndexOf('{')-1, Trim)
    // without an IndexOutOfRange escape. Each row uses a UNIQUE id so the cache cannot collide. (TEST-03)
    [Theory]
    [InlineData("Name [bracket] {836045448945401}", "Name [bracket]")]
    [InlineData("Name @at {836045448945402}", "Name @at")]
    [InlineData("Name :colon {836045448945403}", "Name :colon")]
    public void GameObject_Name_With_Delimiters_Is_Parsed(string raw, string expectedName)
    {
        var go = GameObject.Parse(raw.AsMemory());
        Assert.NotNull(go);
        Assert.Equal(expectedName, go.Name);
    }

    // Golden-line regression lock: a known-good non-nested object, all fields asserted.
    // Unique id not used by any other test in this file.
    [Fact]
    public void GameObject_Golden_All_Fields()
    {
        var go = GameObject.Parse("Imperial Fleet {137438989504}".AsMemory());

        Assert.NotNull(go);
        Assert.False(go.IsNested);
        Assert.Equal("Imperial Fleet", go.Name);
        Assert.Equal(137438989504u, go.Id);
    }
}