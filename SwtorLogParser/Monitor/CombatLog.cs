using SwtorLogParser.Model;

namespace SwtorLogParser.Monitor;

public sealed class CombatLog
{
    public FileInfo FileInfo
    {
        get; init;
    }

    public CombatLog(FileInfo fileInfo)
    {
        FileInfo = fileInfo;
    }

    public override string ToString()
    {
        var count = GetLogLines().Count;
        return $"{FileInfo.Name}: {count}";
    }

    public List<CombatLogLine> GetLogLines()
    {
        var items = new List<CombatLogLine>();

        using (var stream = FileInfo.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            using (var reader = new StreamReader(stream))
            {
                var span = reader.ReadToEnd().AsSpan();

                foreach (var line in span.EnumerateLines())
                {
                    if (line.IsEmpty) continue;
                    var combatLogLine = CombatLogLine.Parse(new ReadOnlyMemory<char>(line.ToArray()));
                    if (combatLogLine is not null) items.Add(combatLogLine);
                }
            }
        }

        return items;
    }
}