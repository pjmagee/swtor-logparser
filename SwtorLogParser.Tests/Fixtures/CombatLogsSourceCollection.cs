namespace SwtorLogParser.Tests.Fixtures;

/// <summary>
/// Serializes test classes that mutate the process-global <see cref="Monitor.CombatLogs"/>
/// source seam (SetSource/ResetSource). xUnit runs test CLASSES in parallel by default, so
/// without a shared collection two classes could interleave a SetSource(fixture) in one with
/// a ResetSource() in another against the single static `_source`, making PlayerNames reads
/// non-deterministic. Grouping every source-swapping class into one collection forces them to
/// run sequentially, keeping the hermetic seam tests deterministic. (Surfaced by WR-03's
/// Lazy-init timing change; the underlying parallelism race predates it.)
/// </summary>
[CollectionDefinition(Name)]
public sealed class CombatLogsSourceCollection
{
    public const string Name = "CombatLogs source seam (serialized)";
}
