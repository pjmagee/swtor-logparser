using SwtorLogParser.Model;

namespace SwtorLogParser.Tests;

[TestClass]
public class ThreatTests
{
    [TestMethod]
    public void Zero_Is_Positive()
    {
        var threat = Threat.Parse("<0>".AsMemory());
        Assert.IsNotNull(threat);
        Assert.AreEqual(0, threat.Value);
        Assert.IsTrue(threat.IsPositive);
        Assert.IsFalse(threat.IsNegative);
    }

    [TestMethod]
    public void Positive_Is_Positive()
    {
        var threat = Threat.Parse("<123>".AsMemory());
        Assert.IsNotNull(threat);
        Assert.AreEqual(123, threat.Value);
        Assert.IsTrue(threat.IsPositive);
        Assert.IsFalse(threat.IsNegative);
    }

    [TestMethod]
    public void Negative_Is_Negative()
    {
        var threat = Threat.Parse("<-123>".AsMemory());
        Assert.IsNotNull(threat);
        Assert.AreEqual(-123, threat.Value);
        Assert.IsFalse(threat.IsPositive);
        Assert.IsTrue(threat.IsNegative);
    }

    [TestMethod]
    public void No_Threat_Is_Null()
    {
        var threat = Threat.Parse("".AsMemory());
        Assert.IsNull(threat);
    }

    [TestMethod]
    public void Invalid_Threat_Is_Null()
    {
        var threat = Threat.Parse("<>".AsMemory());
        Assert.IsNull(threat);
    }

    // Pattern C (guard-null matrix, green today): Threat.Parse rejects cleanly when empty,
    // shorter than 3 chars, or the scope starts with 'v' (Threat.cs:23-34). Consolidates and
    // extends No_Threat_Is_Null / Invalid_Threat_Is_Null into a [Theory] matrix.
    [DataTestMethod]
    [DataRow("<>")]      // length < 3 guard
    [DataRow("")]        // empty guard
    [DataRow("<vfoo>")]  // scope[0] == 'v' guard -> returns null
    public void Threat_Parse_Rejects_Cleanly(string raw)
    {
        Assert.IsNull(Threat.Parse(raw.AsMemory()));
    }

    // BUG-05 (Pattern E): scope "abc" passes the non-'v' guard so Parse returns a non-null
    // Threat; the int.TryParse (Threat.cs) reads non-numeric scope as null instead of throwing.
    // Phase 2: now graceful (BUG-05) — Value is int?.
    [TestMethod]
    public void Threat_NonNumeric_Value_Returns_Null()
    {
        var threat = Threat.Parse("<abc>".AsMemory());
        Assert.IsNotNull(threat); // Parse is LAZY — "abc" passes the non-'v' guard, returns non-null
        Assert.IsNull(threat.Value); // int.TryParse("abc") fails -> null (was FormatException)
    }
}
