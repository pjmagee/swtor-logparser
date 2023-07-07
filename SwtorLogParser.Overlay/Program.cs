using SwtorLogParser.Monitor;

namespace SwtorLogParser.Overlay;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new ParserForm(CombatLogsMonitor.Instance));
    }
}