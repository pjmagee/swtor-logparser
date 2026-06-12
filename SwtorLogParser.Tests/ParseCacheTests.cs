using SwtorLogParser.Caching;
using SwtorLogParser.Model;

namespace SwtorLogParser.Tests;

// RFCT-03 cache contract: content-keyed (rom.ToString), per-type-separate, thread-safe, bounded.
// The static parse caches persist across tests within a run, so every test uses a UNIQUE literal
// content to avoid a leftover cached entry from another test masking the path under test.
[TestClass]
public class ParseCacheTests
{
    // Content-key dedup: two DISTINCT backing memories with identical content resolve to the SAME
    // cached instance — proving the cache keys on content, not ReadOnlyMemory.GetHashCode identity.
    [TestMethod]
    public void Content_Key_Dedups_Identical_Content()
    {
        const string content = "ZqxCacheDedupWidget {3039943492370432}";

        // Two genuinely distinct backing arrays: slice off a prepended char vs. the plain string.
        var a = ("X" + content).AsMemory().Slice(1);
        var b = content.AsMemory();

        var first = GameObject.Parse(a);
        var second = GameObject.Parse(b);

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreSame(first, second);
    }

    // Cross-type no-collision: parsing the same content as a GameObject then an Ability (and the
    // reverse for a second content) must NOT throw InvalidCastException, and each returns its own
    // concrete type. Separate per-type caches make this impossible to collide.
    [TestMethod]
    public void Ability_And_GameObject_No_Cross_Type_Collision()
    {
        const string contentA = "ZqxCrossTypeAlpha {3039943492370001}";
        var go = GameObject.Parse(contentA.AsMemory());
        var ab = Ability.Parse(contentA.AsMemory());

        Assert.IsNotNull(go);
        Assert.IsNotNull(ab);
        Assert.IsInstanceOfType(go, typeof(GameObject));
        Assert.IsInstanceOfType(ab, typeof(Ability));

        // Reverse order with a second unique content.
        const string contentB = "ZqxCrossTypeBeta {3039943492370002}";
        var ab2 = Ability.Parse(contentB.AsMemory());
        var go2 = GameObject.Parse(contentB.AsMemory());

        Assert.IsNotNull(ab2);
        Assert.IsNotNull(go2);
        Assert.IsInstanceOfType(ab2, typeof(Ability));
        Assert.IsInstanceOfType(go2, typeof(GameObject));
    }

    // Thread-safety: many threads parsing the SAME content concurrently must never throw and must
    // converge on a single cached instance.
    [TestMethod]
    public void Concurrent_Parse_Is_Safe()
    {
        var shared = "ZqxConcurrentCacheWidget {3039943492370099}".AsMemory();

        var results = new Ability?[1000];
        Exception? ex = null;
        try
        {
            Parallel.For(0, results.Length, i => results[i] = Ability.Parse(shared));
        }
        catch (Exception e)
        {
            ex = e;
        }

        Assert.IsNull(ex);
        foreach (var r in results) Assert.IsNotNull(r);

        var first = results[0];
        foreach (var r in results) Assert.AreSame(first, r);
    }

    // PERF-CACHE-01: span lookup on a HIT returns the SAME instance the string lookup returns for
    // identical content, and the span-lookup hit does NOT grow the cache (zero string-key alloc).
    [TestMethod]
    public void Span_Lookup_Returns_Same_Instance_As_String_Lookup()
    {
        const int cap = 16;
        var cache = new BoundedCache<object>(cap);
        const string content = "ZqxSpanLookupSameInstance";

        var value = new object();
        var added = cache.GetOrAdd(content, value);
        Assert.AreSame(value, added);

        var countAfterAdd = cache.Count;

        Assert.IsTrue(cache.TryGetValue(content, out var byString));
        Assert.IsTrue(cache.TryGetValue(content.AsSpan(), out var bySpan));

        Assert.AreSame(byString, bySpan);
        Assert.AreSame(value, bySpan);
        Assert.AreEqual(countAfterAdd, cache.Count);
    }

    // PERF-CACHE-01: repeated span-lookup HITS never grow the cache.
    [TestMethod]
    public void Span_Lookup_Hit_Does_Not_Grow_Cache()
    {
        var cache = new BoundedCache<object>(32);
        const string content = "ZqxSpanLookupNoGrow";
        cache.GetOrAdd(content, new object());

        var before = cache.Count;
        for (var i = 0; i < 1000; i++)
        {
            Assert.IsTrue(cache.TryGetValue(content.AsSpan(), out _));
        }

        Assert.AreEqual(before, cache.Count);
    }

    // PERF-CACHE-01: span GetOrAdd materializes the key + inserts exactly once on a miss, and
    // returns the cached instance on subsequent (span) hits without growing the cache.
    [TestMethod]
    public void Span_GetOrAdd_Inserts_Once_On_Miss_Then_Hits()
    {
        var cache = new BoundedCache<object>(32);
        const string content = "ZqxSpanGetOrAddOnce";

        var factoryCalls = 0;
        var first = cache.GetOrAdd(content.AsSpan(), () => { factoryCalls++; return new object(); });
        var afterInsert = cache.Count;

        var second = cache.GetOrAdd(content.AsSpan(), () => { factoryCalls++; return new object(); });

        Assert.AreSame(first, second);
        Assert.AreEqual(1, factoryCalls);
        Assert.AreEqual(afterInsert, cache.Count);
    }

    // PERF-CACHE-01: the span hot path in GameObject.Parse returns the same instance as a prior
    // parse of identical content from a DISTINCT backing memory — content-keyed via span lookup.
    [TestMethod]
    public void GameObject_Span_Hot_Path_Dedups_Identical_Content()
    {
        const string content = "ZqxSpanHotPathWidget {3039943492375000}";

        var a = ("Y" + content).AsMemory().Slice(1); // distinct backing array
        var b = content.AsMemory();

        var first = GameObject.Parse(a);
        var second = GameObject.Parse(b);

        Assert.IsNotNull(first);
        Assert.AreSame(first, second);
    }

    // Bound: a small-cap BoundedCache must never exceed its capacity, even after adding many more
    // distinct keys than the cap. Tested directly on BoundedCache<int> for a deterministic proof
    // (the shared static caches use cap 4096 and are awkward to flood in isolation).
    [TestMethod]
    public void Cache_Is_Bounded()
    {
        const int cap = 8;
        var cache = new BoundedCache<int>(cap);

        for (var i = 0; i < cap * 4; i++)
            cache.GetOrAdd($"ZqxBoundKey-{i}", i);

        Assert.IsTrue(cache.Count <= cap, $"Count {cache.Count} exceeded cap {cap}");
    }
}
