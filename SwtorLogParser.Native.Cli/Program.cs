using System.CommandLine;
using System.CommandLine.Invocation;
using SwtorLogParser.Monitor;
using SwtorLogParser.View;

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
        CombatLogsMonitor.Instance.DpsHps.Subscribe(playerStats => Update(list, playerStats));
        CombatLogsMonitor.Instance.Start(token);

        manualResetEvent.WaitOne();
    }

    private static int _lastRowCount;

    private static void Update(SlidingExpirationList list, CombatLogsMonitor.PlayerStats playerStats)
    {
        list.AddOrUpdate(playerStats);

        // Redirected output (e.g. piped to a file) has no cursor buffer — SetCursorPosition /
        // WindowWidth / WindowHeight throw or misbehave. Fall back to plain writes and do not
        // touch cursor state or _lastRowCount (Pitfall 5).
        if (Console.IsOutputRedirected)
        {
            foreach (var redirectedItem in list.Items)
                Console.WriteLine(FormatRow(redirectedItem));
            return;
        }

        // Clamp width so a 0/odd window cannot break PadRight; clamp the bottom row to the
        // buffer height so SetCursorPosition cannot exceed it on a short window.
        int width = Math.Max(1, Console.WindowWidth - 1);
        int maxRow = Math.Max(1, Console.WindowHeight - 1);

        int row = 0;
        foreach (var item in list.Items)
        {
            // Row 0 stays the filename header (OnCombatLogAdded); the stats block starts at row 1.
            int targetRow = 1 + row;
            if (targetRow > maxRow) break;

            Console.SetCursorPosition(0, targetRow);
            var text = FormatRow(item);
            Console.Write(text.Length > width ? text[..width] : text.PadRight(width));
            row++;
        }

        // Clear rows that were drawn last frame but not this frame (row count shrank).
        for (int r = row; r < _lastRowCount; r++)
        {
            int targetRow = 1 + r;
            if (targetRow > maxRow) break;
            Console.SetCursorPosition(0, targetRow);
            Console.Write(new string(' ', width));
        }

        _lastRowCount = row;
    }

    private static string FormatRow(CombatLogsMonitor.PlayerStats item) =>
        string.Format("{0}: DPS: {1} ({2}%); HPS: {3} ({4}%)",
            item.Player.Name!,
            item.DPS.HasValue ? item.DPS.Value.ToString("N") : "-",
            item.DPSCritP.HasValue ? item.DPSCritP.Value.ToString("N") : "-",
            item.HPS.HasValue ? item.HPS.Value.ToString("N") : "-",
            item.HPSCritP.HasValue ? item.HPSCritP.Value.ToString("N") : "-");

    private static void OnCombatLogAdded(object? _, CombatLog combatLog)
    {
        // Redirected output (e.g. piped to a file) has no cursor buffer — SetCursorPosition /
        // WindowWidth throw or misbehave. Fall back to a plain write that never touches cursor
        // state (mirrors the guard in Update).
        if (Console.IsOutputRedirected)
        {
            Console.WriteLine(combatLog.FileInfo);
            return;
        }

        // This runs on the Rx subscription thread; a console window resize between reading
        // WindowWidth and the SetCursorPosition calls can push us out of bounds. Swallow the
        // transient resize failure so it does not tear down the subscription.
        try
        {
            int width = Math.Max(1, Console.WindowWidth - 1);
            Console.SetCursorPosition(0, 0);
            Console.Write(new string(' ', width));
            Console.SetCursorPosition(0, 0);
            Console.Write(combatLog.FileInfo);
        }
        catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException)
        {
            // Transient console resize/buffer state — skip this header refresh.
        }
    }

    private static void ListCombatLogs()
    {
        foreach (var combatLog in CombatLogs.EnumerateCombatLogs()) Console.WriteLine(combatLog);
    }
}