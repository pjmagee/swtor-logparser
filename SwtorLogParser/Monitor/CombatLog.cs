using SwtorLogParser.Model;

namespace SwtorLogParser.Monitor;

public sealed class CombatLog
{
    public CombatLog(FileInfo fileInfo)
    {
        FileInfo = fileInfo;
    }

    public FileInfo FileInfo { get; init; }

    public override string ToString()
    {
        // PERF-01: report the line count WITHOUT constructing CombatLogLine objects (no Parse).
        // Counts non-empty lines (NOT parse-filtered like the old GetLogLines().Count) — intentional
        // per PERF-01 (no re-parse); the reported number may exceed the count of parseable combat
        // lines. Uses the same offset-tracking splitter GetLogLines() uses — one source of truth.
        var text = ReadAllText();

        var count = 0;
        foreach (var (_, length) in EnumerateLineSpans(text))
        {
            if (length > 0) count++;
        }

        return $"{FileInfo.Name}: {count}";
    }

    public List<CombatLogLine> GetLogLines()
    {
        var items = new List<CombatLogLine>();

        // Single heap string from ReadToEnd() — GC-rooted via the slices held by the returned list,
        // so every AsMemory window stays valid for the lifetime of the list (no use-after-free).
        var text = ReadAllText();

        foreach (var (start, length) in EnumerateLineSpans(text))
        {
            if (length == 0) continue; // skip empty lines (matches the old `if (line.IsEmpty) continue;`)

            // Zero-copy: slice a window into the single backing string — NO per-line char[] copy.
            var rom = text.AsMemory(start, length);
            var combatLogLine = CombatLogLine.Parse(rom);
            if (combatLogLine is not null) items.Add(combatLogLine);
        }

        return items;
    }

    private string ReadAllText()
    {
        // BUG-07 (locked): least-privilege read access while keeping FileShare.ReadWrite so the live
        // SWTOR writer is never blocked. Latin1 encoding is the established decode for these logs.
        using var stream = FileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, System.Text.Encoding.Latin1);
        return reader.ReadToEnd();
    }

    // Single offset-tracking line splitter — the ONE source of truth for both ToString()'s count and
    // GetLogLines()'s slices. Splits on '\r\n' (one break), bare '\r', and bare '\n' — the terminators
    // SWTOR logs actually use; the terminator is EXCLUDED from every emitted span (no trailing '\r').
    // Intentionally does NOT split on the exotic Unicode terminators (VT/FF/NEL/LS/PS) that
    // MemoryExtensions.EnumerateLines treats as breaks, because Latin-1-decoded log content can
    // legitimately contain those bytes (e.g. 0x85) inside names. Indices are bounded by construction.
    private static IEnumerable<(int Start, int Length)> EnumerateLineSpans(string text)
    {
        var i = 0;
        var n = text.Length;

        while (i < n)
        {
            var start = i;
            while (i < n && text[i] != '\r' && text[i] != '\n') i++;

            yield return (start, i - start); // terminator excluded

            if (i < n)
            {
                if (text[i] == '\r' && i + 1 < n && text[i + 1] == '\n')
                    i += 2; // CRLF counts as a single break
                else
                    i += 1; // bare '\r' or bare '\n'
            }
        }
    }
}
