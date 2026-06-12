using System.Collections.Concurrent;

namespace SwtorLogParser.Caching;

/// <summary>
/// AOT-safe bounded, content-keyed, thread-safe cache wrapper (no new package — BCL only).
///
/// Keyed by the content <see cref="string"/> (e.g. <c>rom.ToString()</c>) so two
/// <c>ReadOnlyMemory&lt;char&gt;</c> values with identical content but different backing
/// memory resolve to the SAME cached instance (RFCT-03 / ME-02 fix).
///
/// Bounded: a fixed capacity cap with simple INSERTION-ORDER (FIFO) eviction — NOT strict LRU
/// (the locked Phase 3 decision; true LRU would require touch-on-read bookkeeping). Caps
/// unbounded memory growth from high-volume log tailing (T-03-02 DoS mitigation).
///
/// Thread-safe: backed by <see cref="ConcurrentDictionary{TKey,TValue}"/> (content hashing) plus
/// a <see cref="ConcurrentQueue{T}"/> for eviction order. Preserves the Phase 2
/// "another thread won the race → return the cached instance" semantics.
/// </summary>
internal sealed class BoundedCache<TValue>
{
    private readonly int _capacity;

    // Ordinal comparer is required for the span alternate lookup: StringComparer.Ordinal implements
    // IAlternateEqualityComparer<ReadOnlySpan<char>, string> (net9+/net10), which is what
    // GetAlternateLookup<ReadOnlySpan<char>>() needs. Ordinal also matches the prior content-key
    // semantics (rom.ToString() compared with the default ordinal string equality).
    private readonly ConcurrentDictionary<string, TValue> _map = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _order = new(); // FIFO eviction (simplest bound)

    // PERF-CACHE-01: zero-allocation span lookup. AOT-safe and reflection-free — the core library
    // stays IsAotCompatible=true. The alternate lookup hashes/compares the span directly, so a HIT
    // never materializes a string key (the per-line rom.ToString() allocation is removed).
    private readonly ConcurrentDictionary<string, TValue>.AlternateLookup<ReadOnlySpan<char>> _altLookup;

    public BoundedCache(int capacity)
    {
        _capacity = capacity;
        _altLookup = _map.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>Current number of cached entries (test/bound assertion accessor).</summary>
    internal int Count => _map.Count;

    public bool TryGetValue(string key, out TValue value) => _map.TryGetValue(key, out value!);

    /// <summary>
    /// Span-keyed lookup — ZERO string allocation on a hit. Delegates to the ordinal alternate
    /// lookup, hashing the span content directly against the existing string keys.
    /// </summary>
    public bool TryGetValue(ReadOnlySpan<char> key, out TValue value) =>
        _altLookup.TryGetValue(key, out value!);

    /// <summary>
    /// Span-keyed insert: returns the existing cached value on a hit (no allocation), or — only on
    /// a MISS — materializes the string key exactly once, invokes <paramref name="valueFactory"/>,
    /// and runs the SAME insert/eviction/lost-race body as <see cref="GetOrAdd(string, TValue)"/>.
    /// </summary>
    public TValue GetOrAdd(ReadOnlySpan<char> key, Func<TValue> valueFactory)
    {
        if (_altLookup.TryGetValue(key, out var existing)) return existing;

        // Miss: materialize the string key once, then defer to the string overload (which repeats
        // the TryGetValue guard — benign — and owns the FIFO/eviction/lost-race semantics).
        return GetOrAdd(key.ToString(), valueFactory());
    }

    /// <summary>
    /// Returns the existing cached value for <paramref name="key"/>, or adds
    /// <paramref name="value"/> and returns it. On a lost add race, returns the winner's instance.
    /// </summary>
    public TValue GetOrAdd(string key, TValue value)
    {
        if (_map.TryGetValue(key, out var existing)) return existing;

        if (_map.TryAdd(key, value))
        {
            _order.Enqueue(key);
            while (_map.Count > _capacity && _order.TryDequeue(out var oldest))
                _map.TryRemove(oldest, out _);
        }

        // Another thread won the add race for this key — return the cached instance.
        return _map.TryGetValue(key, out var v) ? v : value;
    }
}
