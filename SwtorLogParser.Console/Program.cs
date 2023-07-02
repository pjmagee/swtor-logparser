using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Rendering;
using System.CommandLine.Rendering.Views;
using System.Reactive.Linq;

namespace SwtorLogParser.Cli;

public static class Program
{
    static readonly Region Region = new(0, 0);
    static readonly SlidingExpirationList List = new(TimeSpan.FromSeconds(30));

    static void Update(ConsoleRenderer renderer, PlayerStats playerStats)
    {
        List.AddOrUpdate(playerStats);
        var tableView = new TableView<PlayerStats> { Items = List.Items };
        tableView.AddColumn(x => x.Player, "Player", ColumnDefinition.Star(0.2));
        tableView.AddColumn(x => x.DPS.HasValue ? x.DPS.Value.ToString("N") : "-", "dps", ColumnDefinition.Star(0.2));
        tableView.AddColumn(x => x.DPSCritP.HasValue ? x.DPSCritP.Value.ToString("N") : "-", "(crit %)", ColumnDefinition.Star(0.2));
        tableView.AddColumn(x => x.HPS.HasValue ? x.HPS.Value.ToString("N") : "-", "hps", ColumnDefinition.Star(0.2));
        tableView.AddColumn(x => x.HPSCritP.HasValue ? x.HPSCritP.Value.ToString("N") : "-", "(crit %)", ColumnDefinition.Star(0.2));
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
        ITerminal terminal = context.Console.GetTerminal();
        terminal.HideCursor();

        using (var manualResetEvent = new ManualResetEvent(false))
        {
            manualResetEvent.SetSafeWaitHandle(token.WaitHandle.SafeWaitHandle);

            ConsoleRenderer consoleRenderer = new ConsoleRenderer(context.Console, OutputMode.Ansi);

            CombatLogsMonitor.Instance.CombatLogAdded += (_, combatLog) =>
            {
                // Console.SetCursorPosition(0, 0);
                // Console.Write(new string(' ', Console.WindowWidth - 1));
                // Console.SetCursorPosition(0, 0);
                // Console.Write(combatLog.FileInfo);
            };

            var dps = CombatLogsMonitor.Instance.CombatLogLines.Where(combatLogLine => combatLogLine.IsPlayerDamage())
                .GroupBy(combatLogLine => combatLogLine.Source!.Name)
                .SelectMany(group =>
                {
                    return group.Buffer(10, 1)
                        .Where(b => b.Count >= 2)
                        .Select(buffer =>
                        {
                            var sortedBuffer = buffer.OrderBy(x => x.TimeStamp).ToList();
                            var duration = (sortedBuffer.Last().TimeStamp - sortedBuffer.First().TimeStamp).TotalSeconds;
                            var sum = sortedBuffer.Sum(x => x.Value!.Total);
                            var critical = sortedBuffer.Count(x => x.Value!.IsCritical) * 100.0 / sortedBuffer.Count;
                            return new PlayerStats { Player = sortedBuffer[0].Source!.Name!, DPS = sum / duration, DPSCritP = critical };
                        });
                });

            var hps = CombatLogsMonitor.Instance.CombatLogLines.Where(combatLogLine => combatLogLine.IsPlayerHeal())
                .GroupBy(combatLogLine => combatLogLine.Source!.Name)
                .SelectMany(group =>
                {
                    return group.Buffer(10, 1)
                        .Select(buffer =>
                        {
                            var sortedBuffer = buffer.OrderBy(x => x.TimeStamp).ToList();
                            var duration = (sortedBuffer.Last().TimeStamp - sortedBuffer.First().TimeStamp).TotalSeconds;
                            var sum = sortedBuffer.Sum(x => x.Value!.Total);
                            var critical = sortedBuffer.Count(x => x.Value!.IsCritical) / sortedBuffer.Count;
                            return new PlayerStats { Player = sortedBuffer[0].Source!.Name!, HPS = sum / duration, HPSCritP = critical };
                        });
                });

            dps.Subscribe((playerStats) => Update(consoleRenderer, playerStats));
            hps.Subscribe((playerStats) => Update(consoleRenderer, playerStats));

            CombatLogsMonitor.Instance.Start(token);

            manualResetEvent.WaitOne();
        }
    }

    private static void ListCombatLogs()
    {
        foreach (var combatLog in CombatLogs.EnumerateCombatLogs())
        {
            Console.WriteLine(combatLog);
        }
    }
}