using System.CommandLine;
using System.CommandLine.Invocation;
using SwtorLogParser.Monitor;
using SwtorLogParser.Native.Cli.View;

namespace SwtorLogParser.Native.Cli;

public static class Program
{
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
        using var manualResetEvent = new ManualResetEvent(false);
        var token = context.GetCancellationToken();
        var list = new SlidingExpirationList(TimeSpan.FromSeconds(30));
        manualResetEvent.SetSafeWaitHandle(token.WaitHandle.SafeWaitHandle);

        CombatLogsMonitor.Instance.CombatLogAdded += OnCombatLogAdded;
        CombatLogsMonitor.Instance.DPS.Subscribe(playerStats => Update(list, playerStats));
        CombatLogsMonitor.Instance.HPS.Subscribe(playerStats => Update(list, playerStats));
        CombatLogsMonitor.Instance.Start(token);

        manualResetEvent.WaitOne();
    }

    private static void Update(SlidingExpirationList list, CombatLogsMonitor.PlayerStats playerStats)
    {
        list.AddOrUpdate(playerStats);

        Console.Clear();
        Console.SetCursorPosition(0, 1);

        foreach (var item in list.Items)
            Console.WriteLine("{0}: DPS: {1} ({2}%); HPS: {3} ({4}%)",
                item.Player.Name!,
                item.DPS.HasValue ? item.DPS.Value.ToString("N") : "-",
                item.DPSCritP.HasValue ? item.DPSCritP.Value.ToString("N") : "-",
                item.HPS.HasValue ? item.HPS.Value.ToString("N") : "-",
                item.HPSCritP.HasValue ? item.HPSCritP.Value.ToString("N") : "-");
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