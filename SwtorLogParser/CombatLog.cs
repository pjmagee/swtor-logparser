namespace SwtorLogParser;

public class CombatLog
{
	public FileInfo FileInfo { get; }
	
	public CombatLog(FileInfo fileInfo)
	{
		FileInfo = fileInfo;		
	}

	public List<CombatLogLine> Parse()
	{
		List<CombatLogLine> items = new List<CombatLogLine>();

		using (var stream = FileInfo.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
		{
			using (var reader = new StreamReader(stream))
			{
				var memory = reader.ReadToEnd().AsMemory();
				
				foreach(var line in memory.Span.EnumerateLines())
				{
					if(line.IsEmpty) continue;
					var combatLogLine = CombatLogLine.Parse(new ReadOnlyMemory<char>(line.ToArray()));
					if (combatLogLine is not null) items.Add(combatLogLine);
				}
			}
		}
		
		return items;
	}
}