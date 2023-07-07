using SwtorLogParser.Monitor;

namespace SwtorLogParser.Overlay;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new ParserForm(CombatLogsMonitor.Instance));
    }
}