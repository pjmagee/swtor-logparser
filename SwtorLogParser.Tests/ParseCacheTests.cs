using SwtorLogParser.Caching;
using SwtorLogParser.Model;

namespace SwtorLogParser.Tests;

// RFCT-03 cache contract: content-keyed (rom.ToString), per-type-separate, thread-safe, bounded.
// The static parse caches persist across tests within a run, so every test uses a UNIQUE literal
// content to avoid a leftover cached entry from another test masking the path under test.
public class ParseCacheTests
{
    // Content-key dedup: two DISTINCT backing memories with identical content resolve to the SAME
    // cached instance — proving the cache keys on content, not ReadOnlyMemory.GetHashCode identity.
    [Fact]
    public void Content_Key_Dedups_Identical_Content()
    {
        const string content = "ZqxCacheDedupWidget {3039943492370432}";

        // Two genuinely distinct backing arrays: slice off a prepended char vs. the plain string.
        var a = ("X" + content).AsMemory().Slice(1);
        var b = content.AsMemory();

        var first = GameObject.Parse(a);
        var second = GameObject.Parse(b);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Same(first, second);
    }

    // Cross-type no-collision: parsing the same content as a GameObject then an Ability (and the
    // reverse for a second content) must NOT throw InvalidCastException, and each returns its own
    // concrete type. Separate per-type caches make this impossible to collide.
    [Fact]
    public void Ability_And_GameObject_No_Cross_Type_Collision()
    {
        const string contentA = "ZqxCrossTypeAlpha {3039943492370001}";
        var go = GameObject.Parse(contentA.AsMemory());
        var ab = Ability.Parse(contentA.AsMemory());

        Assert.NotNull(go);
        Assert.NotNull(ab);
        Assert.IsType<GameObject>(go);
        Assert.IsType<Ability>(ab);

        // Reverse order with a second unique content.
        const string contentB = "ZqxCrossTypeBeta {3039943492370002}";
        var ab2 = Ability.Parse(contentB.AsMemory());
        var go2 = GameObject.Parse(contentB.AsMemory());

        Assert.NotNull(ab2);
        Assert.NotNull(go2);
        Assert.IsType<Ability>(ab2);
        Assert.IsType<GameObject>(go2);
    }

    // Thread-safety: many threads parsing the SAME content concurrently must never throw and must
    // converge on a single cached instance.
    [Fact]
    public void Concurrent_Parse_Is_Safe()
    {
        var shared = "ZqxConcurrentCacheWidget {3039943492370099}".AsMemory();

        var results = new Ability?[1000];
        var ex = Record.Exception(() =>
            Parallel.For(0, results.Length, i => results[i] = Ability.Parse(shared)));

        Assert.Null(ex);
        Assert.All(results, r => Assert.NotNull(r));

        var first = results[0];
        Assert.All(results, r => Assert.Same(first, r));
    }

    // Bound: a small-cap BoundedCache must never exceed its capacity, even after adding many more
    // distinct keys than the cap. Tested directly on BoundedCache<int> for a deterministic proof
    // (the shared static caches use cap 4096 and are awkward to flood in isolation).
    [Fact]
    public void Cache_Is_Bounded()
    {
        const int cap = 8;
        var cache = new BoundedCache<int>(cap);

        for (var i = 0; i < cap * 4; i++)
            cache.GetOrAdd($"ZqxBoundKey-{i}", i);

        Assert.True(cache.Count <= cap, $"Count {cache.Count} exceeded cap {cap}");
    }
}
