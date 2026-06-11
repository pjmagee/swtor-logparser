using System.Text;
using SwtorLogParser.Model;

namespace SwtorLogParser.Tests;

// SWTOR combat logs are Windows-1252 / Latin-1: an accented character is a single
// high byte (á = 0xE1, é = 0xE9), NOT a UTF-8 multi-byte sequence. The file readers
// (CombatLog.GetLogLines, CombatLogsMonitor.ReadAsync) must decode with Encoding.Latin1.
// Decoding these bytes as UTF-8 corrupts each high byte to U+FFFD '�' — the bug seen as
// "Matt W�lsh" in the overlay. These tests lock the encoding decision.
public class EncodingTests
{
    // Realistic player line; {0} is the accented name as the game writes it.
    private const string LineTemplate =
        "[20:33:17.759] [@{0}#689921479616853|(422.51,620.88,33.46,84.27)|(300469/379924)] " +
        "[=] [Progressive Scan {{3394132265402368}}] [ApplyEffect {{836045448945477}}: " +
        "Heal {{836045448945500}}] (8622) <3880>";

    [Fact]
    public void Latin1_StreamReader_Decodes_Accented_Name_Intact()
    {
        var line = string.Format(LineTemplate, "Tést Wálsh"); // é=0xE9, á=0xE1
        var latin1Bytes = Encoding.Latin1.GetBytes(line);

        using var ms = new MemoryStream(latin1Bytes);
        using var reader = new StreamReader(ms, Encoding.Latin1); // matches production readers
        var decoded = reader.ReadLine();

        Assert.NotNull(decoded);
        Assert.Contains("Tést Wálsh", decoded);
        Assert.DoesNotContain('�', decoded); // no replacement char

        var parsed = CombatLogLine.Parse(decoded.AsMemory());
        Assert.NotNull(parsed?.Source?.Name);
        Assert.Contains("Wálsh", parsed!.Source!.Name!);
        Assert.DoesNotContain('�', parsed.Source.Name!);
    }

    [Fact]
    public void Utf8_StreamReader_Corrupts_Latin1_Bytes()
    {
        // Documents WHY Latin1 is required: lone 0xE1/0xE9 bytes are invalid UTF-8.
        var line = string.Format(LineTemplate, "Tést Wálsh");
        var latin1Bytes = Encoding.Latin1.GetBytes(line);

        using var ms = new MemoryStream(latin1Bytes);
        using var reader = new StreamReader(ms, Encoding.UTF8); // the OLD (buggy) behavior
        var decoded = reader.ReadLine();

        Assert.NotNull(decoded);
        Assert.Contains('�', decoded); // UTF-8 mangles the accents
    }
}
