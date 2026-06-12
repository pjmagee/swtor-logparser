using Spectre.Console;
using SwtorLogParser.Monitor;
using SwtorLogParser.View;

namespace SwtorLogParser.Cli.Common;

public static class SwtorCliApp
{
    private static readonly SlidingExpirationList List = new(TimeSpan.FromSeconds(30));

    // The single live-updating table. Rows are rebuilt in place each frame and the live
    // context is refreshed, so the table updates without clearing the whole screen (WR-04).
    private static readonly Table Table = new Table()
        .AddColumn("Player")
        .AddColumn("dps")
        .AddColumn("(crit %)")
        .AddColumn("hps")
        .AddColumn("(crit %)");

    // Latest combat-log filename; rendered as the table title so it stays pinned above the
    // rows in the same frame instead of scrolling out-of-band (WR-03).
    private static string? _currentFile;

    private static LiveDisplayContext? _live;

    // When output is redirected/non-interactive, cursor + live-update operations throw or
    // misbehave, so we fall back to plain line writes (WR-05).
    private static bool _interactive;

    public static int Run(string[] args)
    {
        switch (args.Length > 0 ? args[0] : "")
        {
            case "list":
                List_();
                return 0;
            case "monitor":
                Monitor();
                return 0;
            default:
                Console.Error.WriteLine("Usage: SwtorLogParser.Cli [list|monitor]");
                return 1;
        }
    }

    private static void Monitor()
    {
        using var cts = new CancellationTokenSource();
        // A second or late Ctrl+C can fire after cts is disposed (when Monitor
        // returns); guard the Cancel and unsubscribe in finally so the disposed CTS never
        // throws ObjectDisposedException on the SIGINT handler thread.
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            e.Cancel = true;
            try { cts.Cancel(); } catch (ObjectDisposedException) { /* already shutting down */ }
        };
        Console.CancelKeyPress += handler;

        // Only drive cursor/live-update rendering when we actually own an interactive
        // console buffer; piped/redirected output degrades to plain writes (WR-05).
        _interactive = !Console.IsOutputRedirected
                       && AnsiConsole.Profile.Capabilities.Interactive;

        try
        {
            if (_interactive)
            {
                Console.CursorVisible = false;

                CombatLogsMonitor.Instance.CombatLogAdded += OnCombatLogAdded;
                CombatLogsMonitor.Instance.DpsHps.Subscribe(Update);
                CombatLogsMonitor.Instance.Start(cts.Token);

                // Live updates the table in place; the callback blocks until Ctrl+C cancels
                // the token, while Update/OnCombatLogAdded refresh the live frame (WR-04).
                AnsiConsole.Live(Table).Start(ctx =>
                {
                    _live = ctx;
                    cts.Token.WaitHandle.WaitOne();
                });

                CombatLogsMonitor.Instance.Stop();
            }
            else
            {
                CombatLogsMonitor.Instance.CombatLogAdded += OnCombatLogAdded;
                CombatLogsMonitor.Instance.DpsHps.Subscribe(Update);
                CombatLogsMonitor.Instance.Start(cts.Token);

                cts.Token.WaitHandle.WaitOne();

                CombatLogsMonitor.Instance.Stop();
            }
        }
        finally
        {
            _live = null;
            Console.CancelKeyPress -= handler;
            if (_interactive && !Console.IsOutputRedirected)
                Console.CursorVisible = true;
        }
    }

    private static void Update(CombatLogsMonitor.PlayerStats playerStats)
    {
        List.AddOrUpdate(playerStats);

        if (!_interactive)
        {
            foreach (var x in List.Items)
                Console.WriteLine(FormatRow(x));
            return;
        }

        RebuildTable();
        _live?.Refresh();
    }

    private static void RebuildTable()
    {
        Table.Rows.Clear();
        Table.Title = _currentFile is null
            ? null
            : new TableTitle(Markup.Escape(_currentFile), new Style(Color.Grey));

        foreach (var x in List.Items)
            Table.AddRow(
                x.Player.Name ?? "-",
                x.DPS.HasValue ? x.DPS.Value.ToString("N") : "-",
                x.DPSCritP.HasValue ? x.DPSCritP.Value.ToString("N") : "-",
                x.HPS.HasValue ? x.HPS.Value.ToString("N") : "-",
                x.HPSCritP.HasValue ? x.HPSCritP.Value.ToString("N") : "-");
    }

    private static string FormatRow(CombatLogsMonitor.PlayerStats x) =>
        string.Format("{0}: dps: {1} ({2}); hps: {3} ({4})",
            x.Player.Name ?? "-",
            x.DPS.HasValue ? x.DPS.Value.ToString("N") : "-",
            x.DPSCritP.HasValue ? x.DPSCritP.Value.ToString("N") : "-",
            x.HPS.HasValue ? x.HPS.Value.ToString("N") : "-",
            x.HPSCritP.HasValue ? x.HPSCritP.Value.ToString("N") : "-");

    private static void OnCombatLogAdded(object? _, CombatLog combatLog)
    {
        _currentFile = combatLog.FileInfo?.ToString();

        if (!_interactive)
        {
            Console.WriteLine(combatLog.FileInfo);
            return;
        }

        // Pin the filename in the live frame as the table title rather than emitting an
        // independent scrolling line on the Rx thread (WR-03).
        RebuildTable();
        _live?.Refresh();
    }

    private static void List_()
    {
        foreach (var combatLog in CombatLogs.EnumerateCombatLogs()) Console.WriteLine(combatLog);
    }
}
