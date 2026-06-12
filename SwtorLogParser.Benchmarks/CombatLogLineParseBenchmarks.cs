using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using SwtorLogParser.Model;

namespace SwtorLogParser.Benchmarks;

/// <summary>
/// Allocation/timing harness for the hot parse path (<see cref="CombatLogLine.Parse"/>).
///
/// The headline metric is bytes-allocated/op (from <see cref="MemoryDiagnoserAttribute"/>),
/// which is deterministic and does NOT need many iterations — so a [ShortRunJob] keeps each
/// run to ~1-3 min while still reporting exact allocation numbers and a stable-enough ns/op.
///
/// Three benchmarks expose the two locked optimizations:
///   - <see cref="ParseAllLines"/>      : pure Parse + drop savings (no sub-property touch)
///   - <see cref="ParseAllLines_TouchAll"/> : full-parse cost (Source/Action/Value read)
///   - <see cref="ParseAllLines_HotCache"/> : cache-HIT path (warmed in GlobalSetup)
///
/// Run in Release: `dotnet run --project SwtorLogParser.Benchmarks -c Release`.
/// </summary>
[ShortRunJob(RuntimeMoniker.Net10_0)]
[MemoryDiagnoser]
public class CombatLogLineParseBenchmarks
{
    private ReadOnlyMemory<char>[] _lines = Array.Empty<ReadOnlyMemory<char>>();

    [GlobalSetup]
    public void Setup()
    {
        var raw = LoadFixtureLines();
        _lines = new ReadOnlyMemory<char>[raw.Length];
        for (var i = 0; i < raw.Length; i++)
            _lines[i] = raw[i].AsMemory();

        // Warm the static parse caches so the HotCache benchmark exercises the cache-HIT path
        // (and so cold-cache string-key allocations are not double-counted on the first run of
        // the other benchmarks). Touch all sub-properties to fully populate GameObject/Ability/
        // Action caches.
        foreach (var line in _lines)
        {
            var parsed = CombatLogLine.Parse(line);
            if (parsed is null) continue;
            _ = parsed.Source;
            _ = parsed.Target;
            _ = parsed.Ability;
            _ = parsed.Action;
            _ = parsed.Value;
            _ = parsed.Threat;
        }
    }

    /// <summary>
    /// Loads the sanitized fixture from the embedded assembly manifest. Embedding (rather than a
    /// copied Content file) keeps the fixture reachable from BenchmarkDotNet's relocated runner.
    /// </summary>
    private static string[] LoadFixtureLines()
    {
        var asm = typeof(CombatLogLineParseBenchmarks).Assembly;
        var name = Array.Find(asm.GetManifestResourceNames(),
            n => n.EndsWith("sample-combat.log", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Embedded fixture 'sample-combat.log' not found.");

        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length > 0) lines.Add(line);
        }

        return lines.ToArray();
    }

    /// <summary>
    /// Pure parse: build the <see cref="CombatLogLine"/> and read ONLY TimeStamp.
    /// After Optimization 2 (lazy sub-parsing), dropped/untouched lines pay no sub-object
    /// parse cost — this benchmark exposes that win.
    /// </summary>
    [Benchmark]
    public long ParseAllLines()
    {
        long acc = 0;
        foreach (var line in _lines)
        {
            var parsed = CombatLogLine.Parse(line);
            if (parsed is not null)
                acc += parsed.TimeStamp.Ticks;
        }

        return acc;
    }

    /// <summary>
    /// Full parse: build each line AND read Source/Action/Value so every sub-object is parsed.
    /// Measures the full-parse cost (and, via Optimization 1, the cache-key allocation removed
    /// from each sub-parse cache lookup).
    /// </summary>
    [Benchmark]
    public long ParseAllLines_TouchAll()
    {
        long acc = 0;
        foreach (var line in _lines)
        {
            var parsed = CombatLogLine.Parse(line);
            if (parsed is null) continue;
            acc += parsed.TimeStamp.Ticks;
            if (parsed.Source?.Id is { } sid) acc += (long)sid;
            if (parsed.Action is not null) acc++;
            if (parsed.Value is not null) acc++;
        }

        return acc;
    }

    /// <summary>
    /// Cache-HIT path: the caches are fully warmed in GlobalSetup, so every sub-parse lookup
    /// is a HIT. This is the benchmark that exposes the per-line <c>rom.ToString()</c> cache-key
    /// allocation that Optimization 1 (span-keyed alternate lookup) removes.
    /// </summary>
    [Benchmark]
    public long ParseAllLines_HotCache()
    {
        long acc = 0;
        foreach (var line in _lines)
        {
            var parsed = CombatLogLine.Parse(line);
            if (parsed is null) continue;
            acc += parsed.TimeStamp.Ticks;
            if (parsed.Source?.Id is { } sid) acc += (long)sid;
            if (parsed.Target?.Id is { } tid) acc += (long)tid;
            if (parsed.Ability?.Id is { } aid) acc += (long)aid;
            if (parsed.Action is not null) acc++;
            if (parsed.Value is not null) acc++;
            if (parsed.Threat is not null) acc++;
        }

        return acc;
    }
}
