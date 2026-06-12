using SwtorLogParser.Model;

namespace SwtorLogParser.Tests;

[TestClass]
public class ValueTests
{
    [TestMethod]
    public void Zero_Is_Zero()
    {
        var value = Value.Parse("(0)".AsMemory());
        Assert.IsNotNull(value);
        Assert.AreEqual(0, value.Integer);
    }

    [TestMethod]
    public void Miss_Is_Parsed()
    {
        // BUG-260612-dso: result now keyed off {id} (miss = 836045448945502), reflecting the real log format.
        var value = Value.Parse("(0 -miss {836045448945502})".AsMemory());

        Assert.IsNotNull(value);
        Assert.IsTrue(value.IsMiss);
        Assert.AreEqual(0, value.Integer);
    }

    [TestMethod]
    public void Absorbed_Is_Parsed()
    {
        // BUG-260612-dso: the old flat "(123 absorbed)" encoded BUG 1 (inner amount as Total). A real
        // shield line carries the absorbed amount in a NESTED group; Total is the OUTER damage.
        var value = Value.Parse("(133 energy {836045448940874} -shield {836045448945509} (149 absorbed {836045448945511}))".AsMemory());

        Assert.IsNotNull(value);
        Assert.IsTrue(value.IsAbsorbed);
        Assert.AreEqual(133, value.Total);
        Assert.AreEqual(149, value.Absorbed);
    }

    [TestMethod]
    public void Critical_Is_Parsed()
    {
        var value = Value.Parse("(123*)".AsMemory());

        Assert.IsNotNull(value);
        Assert.IsTrue(value.IsCritical);
        Assert.AreEqual(123, value.Integer);
    }

    [TestMethod]
    public void Parry_Is_Parsed()
    {
        // BUG-260612-dso: result now keyed off {id} (parry = 836045448945503).
        var value = Value.Parse("(123 -parry {836045448945503})".AsMemory());

        Assert.IsNotNull(value);
        Assert.IsTrue(value.IsParry);
        Assert.AreEqual(123, value.Integer);
    }

    [TestMethod]
    public void Dodge_Is_Parsed()
    {
        // BUG-260612-dso: result now keyed off {id} (dodge = 836045448945505).
        var value = Value.Parse("(123 -dodge {836045448945505})".AsMemory());

        Assert.IsNotNull(value);
        Assert.IsTrue(value.IsDodge);
        Assert.AreEqual(123, value.Integer);
    }

    [TestMethod]
    public void Deflect_Is_Parsed()
    {
        // BUG-260612-dso: result now keyed off {id} (deflect = 836045448945508).
        var value = Value.Parse("(123 -deflect {836045448945508})".AsMemory());

        Assert.IsNotNull(value);
        Assert.IsTrue(value.IsDeflect);
        Assert.AreEqual(123, value.Integer);
    }

    [TestMethod]
    public void Tilde_Is_Parsed()
    {
        var value = Value.Parse("(123 ~0)".AsMemory());

        Assert.IsNotNull(value);
        Assert.AreEqual(0, value.Tilde);
        Assert.AreEqual(123, value.Integer);
    }

    [TestMethod]
    public void Charges_Is_Parsed()
    {
        var value = Value.Parse("(123 charges)".AsMemory());

        Assert.IsNotNull(value);
        Assert.IsTrue(value.IsCharges);
        Assert.AreEqual(123, value.Integer);
    }

    [TestMethod]
    public void Energy_Is_Parsed()
    {
        // BUG-260612-dso: type now keyed off {id} (energy = 836045448940874).
        var value = Value.Parse("(123 energy {836045448940874})".AsMemory());

        Assert.IsNotNull(value);
        Assert.IsTrue(value.IsEnergy);
        Assert.AreEqual(123, value.Integer);
    }

    [TestMethod]
    public void Kinetic_Is_Parsed()
    {
        // BUG-260612-dso: type now keyed off {id} (kinetic = 836045448940873).
        var value = Value.Parse("(123 kinetic {836045448940873})".AsMemory());

        Assert.IsNotNull(value);
        Assert.IsTrue(value.IsKinetic);
        Assert.AreEqual(123, value.Integer);
    }

    [TestMethod]
    public void Elemental_Is_Parsed()
    {
        // BUG-260612-dso: type now keyed off {id} (elemental = 836045448940875).
        var value = Value.Parse("(123 elemental {836045448940875})".AsMemory());

        Assert.IsNotNull(value);
        Assert.IsTrue(value.IsElemental);
        Assert.AreEqual(123, value.Integer);
    }

    [TestMethod]
    public void Internal_Is_Parsed()
    {
        // BUG-260612-dso: type now keyed off {id} (internal = 836045448940876).
        var value = Value.Parse("(123 internal {836045448940876})".AsMemory());

        Assert.IsNotNull(value);
        Assert.IsTrue(value.IsInternal);
        Assert.AreEqual(123, value.Integer);
    }

    [TestMethod]
    public void HeroEnginePrefix_Is_Not_Parsed()
    {
        var value = Value.Parse("(he)".AsMemory());
        Assert.IsNull(value);
    }

    // Pattern C (guard-null matrix, green today): Value.Parse rejects cleanly for the HeroEngine
    // prefix and for input with no '(' / ')' (Value.cs guards). Extends HeroEnginePrefix_Is_Not_Parsed.
    [DataTestMethod]
    [DataRow("(he)")]       // HeroEngine prefix guard
    [DataRow("no parens")]  // no '(' / ')' -> no value group -> null
    public void Value_Parse_Rejects_Cleanly(string raw)
    {
        Assert.IsNull(Value.Parse(raw.AsMemory()));
    }

    // BUG-05 (Pattern E): Parse guards pass (parens present, not HeroEngine), so a non-null
    // Value is returned; the brace content is now read via ulong.TryParse (Value.cs), so
    // non-numeric content reads as null instead of throwing. Phase 2: now graceful (BUG-05).
    [TestMethod]
    public void Value_NonNumeric_Id_Returns_Null()
    {
        var value = Value.Parse("(123 {abc})".AsMemory());
        Assert.IsNotNull(value); // Parse is LAZY — guards pass, returns non-null
        Assert.IsNull(value.Id); // ulong.TryParse("abc") fails -> null (was FormatException)
    }

    // ---- BUG-260612-dso reference-verified regression tests (headline lock for both bugs) ----

    [TestMethod]
    public void NestedAbsorb_OuterDamage_Is_Total_And_Absorbed_Is_Separate()
    {
        // BUG 1 + BUG 2: full line. Outer 133 energy is the damage; nested (149 absorbed) is separate.
        var line = "[19:00:00.000] [@Player#1] [@Boss#2] [Ability {1}] [ApplyEffect {2}: Damage {3}] (133 energy {836045448940874} -shield {836045448945509} (149 absorbed {836045448945511})) <0>";
        var value = Value.Parse(line.AsMemory());

        Assert.IsNotNull(value);
        Assert.AreEqual(133, value.Total);     // outer damage, NOT 149 (BUG 1 closed)
        Assert.IsTrue(value.IsEnergy);        // type by first {id} (BUG 2 closed)
        Assert.IsTrue(value.IsAbsorbed);
        Assert.AreEqual(149, value.Absorbed);
    }

    [TestMethod]
    public void CritAbsorb_OuterCrit_Damage_Is_Total()
    {
        var line = "[19:00:00.000] [@Player#1] [@Boss#2] [Ability {1}] [ApplyEffect {2}: Damage {3}] (202* energy {836045448940874} -shield {836045448945509} (226 absorbed {836045448945511})) <0>";
        var value = Value.Parse(line.AsMemory());

        Assert.IsNotNull(value);
        Assert.AreEqual(202, value.Total);
        Assert.IsTrue(value.IsCritical);
        Assert.IsTrue(value.IsEnergy);
        Assert.AreEqual(226, value.Absorbed);
    }

    [TestMethod]
    public void SimpleDamage_NoShield_Has_No_Absorbed()
    {
        var value = Value.Parse("(133 energy {836045448940874})".AsMemory());

        Assert.IsNotNull(value);
        Assert.AreEqual(133, value.Total);
        Assert.IsTrue(value.IsEnergy);
        Assert.IsFalse(value.IsAbsorbed);
        Assert.IsNull(value.Absorbed);
    }

    [TestMethod]
    public void Avoid_Miss_Total_Is_Zero()
    {
        var value = Value.Parse("(0 -miss {836045448945502})".AsMemory());

        Assert.IsNotNull(value);
        Assert.AreEqual(0, value.Total);
        Assert.IsTrue(value.IsMiss);
    }

    [TestMethod]
    public void Heal_NoId_Total_Is_Parsed()
    {
        var value = Value.Parse("(513)".AsMemory());

        Assert.IsNotNull(value);
        Assert.AreEqual(513, value.Total);
    }

    [TestMethod]
    public void LocaleRobustness_GarbledTypeWord_With_EnergyId_Is_Energy()
    {
        // Proves type detection is id-keyed, not word-keyed: a non-English/garbled type word with the
        // energy id still reports IsEnergy.
        var value = Value.Parse("(133 xxxxx {836045448940874})".AsMemory());

        Assert.IsNotNull(value);
        Assert.IsTrue(value.IsEnergy);
    }
}
