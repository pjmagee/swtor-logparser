using SwtorLogParser;

if(args[0] == "combatlogs")
{
    foreach (var combatLog in CombatLogs.EnumerateCombatLogs())
    {
        Console.WriteLine(combatLog);
    }
}

if (args[0] == "monitor")
{
    using (var manualResetEvent = new ManualResetEventSlim())
    {
        CombatLogsMonitor.Instance.Start();
        
        Console.CancelKeyPress += (sender, args) =>
        {
            CombatLogsMonitor.Instance.Stop();
            manualResetEvent.Set();
        };
        
        CombatLogsMonitor.Instance.CombatLogAdded += (_, combatLog) =>
        {
            Console.WriteLine(combatLog.FileInfo);
        };
    
        CombatLogsMonitor.Instance.CombatLogChanged += (_, line) =>
        {
            Console.WriteLine(line);
        };  
    
        manualResetEvent.Wait();
    }
}


