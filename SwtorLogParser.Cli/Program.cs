using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Rendering;
using System.CommandLine.Rendering.Views;
using SwtorLogParser.Cli.View;
using SwtorLogParser.Monitor;

namespace SwtorLogParser.Cli;

public static class Program
{
    private static readonly Region Region = new(0, 0);
    private static readonly SlidingExpirationList List = new(TimeSpan.FromSeconds(30));

    private static void Update(ConsoleRenderer renderer, CombatLogsMonitor.PlayerStats playerStats)
    {
        List.AddOrUpdate(playerStats);
        var tableView = new TableView<CombatLogsMonitor.PlayerStats> { Items = List.Items };
        tableView.AddColumn(x => x.Player.Name, "Player", ColumnDefinition.Star(0.2));
        tableView.AddColumn(x => x.DPS.HasValue ? x.DPS.Value.ToString("N") : "-", "dps", ColumnDefinition.Star(0.2));
        tableView.AddColumn(x => x.DPSCritP.HasValue ? x.DPSCritP.Value.ToString("N") : "-", "(crit %)",
            ColumnDefinition.Star(0.2));
        tableView.AddColumn(x => x.HPS.HasValue ? x.HPS.Value.ToString("N") : "-", "hps", ColumnDefinition.Star(0.2));
        tableView.AddColumn(x => x.HPSCritP.HasValue ? x.HPSCritP.Value.ToString("N") : "-", "(crit %)",
            ColumnDefinition.Star(0.2));
        tableView.Render(renderer, Region);
    }

    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("SWTOR Log Parser");

        var listCommand = new Command("list", "list all swtor logs");
        var monitorCommand = new Command("monitor", "monitor log file changes");

        listCommand.SetHandler(ListCombatLogs);
        monitorCommand.SetHandler(MonitorCombatLogs);

        rootCommand.Add(listCommand);
        rootCommand.Add(monitorCommand);

        return await rootCommand.InvokeAsync(args);
    }

    private static void MonitorCombatLogs(InvocationContext context)
    {
        var token = context.GetCancellationToken();
        var terminal = context.Console.GetTerminal();
        terminal.HideCursor();

        using var manualResetEvent = new ManualResetEvent(false);
        manualResetEvent.SetSafeWaitHandle(token.WaitHandle.SafeWaitHandle);

        var consoleRenderer = new ConsoleRenderer(context.Console, OutputMode.Ansi);

        CombatLogsMonitor.Instance.CombatLogAdded += OnCombatLogAdded;
        CombatLogsMonitor.Instance.DpsHps.Subscribe(playerStats => Update(consoleRenderer, playerStats));
        CombatLogsMonitor.Instance.Start(token);

        manualResetEvent.WaitOne();
    }

    private static void OnCombatLogAdded(object? _, CombatLog combatLog)
    {
        Console.SetCursorPosition(0, 0);
        Console.Write(new string(' ', Console.WindowWidth - 1));
        Console.SetCursorPosition(0, 0);
        Console.Write(combatLog.FileInfo);
    }

    private static void ListCombatLogs()
    {
        foreach (var combatLog in CombatLogs.EnumerateCombatLogs()) Console.WriteLine(combatLog);
    }
}