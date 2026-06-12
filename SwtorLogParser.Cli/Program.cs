using Spectre.Console;
using SwtorLogParser.Monitor;
using SwtorLogParser.View;

namespace SwtorLogParser.Cli;

public static class Program
{
    private static readonly SlidingExpirationList List = new(TimeSpan.FromSeconds(30));

    public static int Main(string[] args)
    {
        switch (args.Length > 0 ? args[0] : "")
        {
            case "list":
                ListCombatLogs();
                return 0;
            case "monitor":
                MonitorCombatLogs();
                return 0;
            default:
                Console.Error.WriteLine("Usage: SwtorLogParser.Cli [list|monitor]");
                return 1;
        }
    }

    private static void MonitorCombatLogs()
    {
        using var cts = new CancellationTokenSource();
        // A second or late Ctrl+C can fire after cts is disposed (when MonitorCombatLogs
        // returns); guard the Cancel and unsubscribe in finally so the disposed CTS never
        // throws ObjectDisposedException on the SIGINT handler thread.
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            e.Cancel = true;
            try { cts.Cancel(); } catch (ObjectDisposedException) { /* already shutting down */ }
        };
        Console.CancelKeyPress += handler;

        try
        {
            Console.CursorVisible = false;

            CombatLogsMonitor.Instance.CombatLogAdded += OnCombatLogAdded;
            CombatLogsMonitor.Instance.DpsHps.Subscribe(Update);
            CombatLogsMonitor.Instance.Start(cts.Token);

            cts.Token.WaitHandle.WaitOne();

            CombatLogsMonitor.Instance.Stop();
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    private static void Update(CombatLogsMonitor.PlayerStats playerStats)
    {
        List.AddOrUpdate(playerStats);

        var table = new Table()
            .AddColumn("Player")
            .AddColumn("dps")
            .AddColumn("(crit %)")
            .AddColumn("hps")
            .AddColumn("(crit %)");

        foreach (var x in List.Items)
            table.AddRow(
                x.Player.Name ?? "-",
                x.DPS.HasValue ? x.DPS.Value.ToString("N") : "-",
                x.DPSCritP.HasValue ? x.DPSCritP.Value.ToString("N") : "-",
                x.HPS.HasValue ? x.HPS.Value.ToString("N") : "-",
                x.HPSCritP.HasValue ? x.HPSCritP.Value.ToString("N") : "-");

        AnsiConsole.Clear();
        AnsiConsole.Write(table);
    }

    private static void OnCombatLogAdded(object? _, CombatLog combatLog)
    {
        AnsiConsole.MarkupLineInterpolated($"[grey]{combatLog.FileInfo}[/]");
    }

    private static void ListCombatLogs()
    {
        foreach (var combatLog in CombatLogs.EnumerateCombatLogs()) Console.WriteLine(combatLog);
    }
}
