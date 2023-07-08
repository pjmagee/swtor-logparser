using System.Reactive.Linq;
using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwtorLogParser.Extensions;
using SwtorLogParser.Model;

namespace SwtorLogParser.Monitor;

public class CombatLogsMonitor
{
    private readonly ILogger<CombatLogsMonitor> _logger;


#if RELEASE
    public static CombatLogsMonitor Instance { get; } = new(NullLogger<CombatLogsMonitor>.Instance);
#elif DEBUG
    public static CombatLogsMonitor Instance { get; } =
 new(LoggerFactory.Create(x => x.ClearProviders().AddConsole().AddDebug()).CreateLogger<CombatLogsMonitor>());
#endif

    private Task? _monitor;
    private Task? _reader;
    private CancellationTokenSource _cancellationTokenSource;

    public event EventHandler<CombatLogLine>? CombatLogChanged;
    public event EventHandler<CombatLog>? CombatLogAdded;

    public bool IsRunning => _monitor is { IsCompleted: false } && _reader is { IsCompleted: false };

    private Subject<CombatLogLine> CombatLogLines { get; } = new Subject<CombatLogLine>();

    private DateTime? _lastWriteTime;
    private string? _lastFileName;
    public IObservable<PlayerStats> DpsHps { get; private set; }
    
    private static readonly CombatLogLineComparer CombatLogLineComparer = new();

    private CombatLogsMonitor()
    {
        ConfigureObservables();
    }
    
    private void ConfigureObservables()
    {
        DpsHps = CombatLogLines
            
            .Where(x => x.TimeStamp > DateTime.Now.AddSeconds(-10))
            .Where(x => x.Source is not null && x.Source.Name is not null)
            .GroupBy(x => x.Source?.Name)
            .SelectMany(g => g
                .Where(l => l.IsPlayerDamage() || l.IsPlayerHeal())
                .DistinctUntilChanged()
                .Scan(new HashSet<CombatLogLine>(CombatLogLineComparer), Accumulator)
                .Select(CalculateDpsHpsStats));
    }
    
    private static object Lock = new object();

    private HashSet<CombatLogLine> Accumulator(HashSet<CombatLogLine> state, CombatLogLine combatLog)
    {
        lock (Lock)
        {
            state.RemoveWhere(line => line.TimeStamp < combatLog.TimeStamp.AddSeconds(-10));
            state.Add(combatLog);
            return state;    
        }
    }

    private PlayerStats CalculateDpsHpsStats(HashSet<CombatLogLine> state)
    {
        // Oldest to latest
        var items = state.OrderBy(x => x.TimeStamp.TimeOfDay).ToList();
        
        var heals = items.Where(pe => pe.IsPlayerHeal()).ToList();
        var damage = items.Where(pe => pe.IsPlayerDamage()).ToList();

        var timeSpan = items.Count > 1 ? (items[^1].TimeStamp - items[0].TimeStamp) : TimeSpan.FromSeconds(1);

        int damageTotal = damage.Sum(pe => pe.Value!.Total);
        int healTotal = heals.Sum(pe => pe.Value!.Total);

        double dpsCrit = (double) damage.Count(pe => pe.Value!.IsCritical) / state.Count * 100;
        double hpsCrit = (double) heals.Count(pe => pe.Value!.IsCritical) / state.Count * 100;

        double? dps = damage.Count > 0 ? damageTotal / timeSpan.TotalSeconds : null;
        double? hps = heals.Count > 0 ? healTotal / timeSpan.TotalSeconds : null;

        double? dpsCritP = double.IsInfinity(dpsCrit) || dpsCrit == 0.0d ? null : dpsCrit;
        double? hpsCritP = double.IsInfinity(hpsCrit) || hpsCrit == 0.0d ? null : hpsCrit;

        return new PlayerStats
        {
            Player = state.ElementAt(0).Source!,
            DPS = dps,
            HPS = hps,
            DPSCritP = dpsCritP,
            HPSCritP = hpsCritP
        };
    }

    private CombatLogsMonitor(ILogger<CombatLogsMonitor> logger) : this()
    {
        _logger = logger;
    }

    public void Start(CancellationToken cancellationToken)
    {
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitor = Task.Factory.StartNew(() => MonitorAsync(cancellationToken), cancellationToken);
        _reader = Task.Factory.StartNew(() => ReadAsync(cancellationToken), cancellationToken);
    }

    public void Stop()
    {
        try
        {
            _cancellationTokenSource.Cancel();
            _monitor = null;
            _reader = null;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "_cancellationTokenSource.CancelAsync() failed");
        }
    }

    private async Task ReadAsync(CancellationToken cancellationToken = default)
    {
        string? current = null;
        long position = 0;
        FileStream? fileStream = null;
        StreamReader? streamReader = null;

        try
        {
            while (!cancellationToken!.IsCancellationRequested)
            {
                if (current != _lastFileName)
                {
                    current = _lastFileName;
                    position = 0;

                    if (fileStream is not null)
                        await fileStream.DisposeAsync();
                    
                    streamReader?.Dispose();
                    fileStream = null;
                    streamReader = null;

                    _logger.LogDebug("current: {Current}. position: {Position}", current, position);
                }

                if (string.IsNullOrWhiteSpace(current)) continue;

                if (fileStream is null)
                {
                    fileStream = new FileStream(current, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    fileStream.Seek(position, SeekOrigin.Begin);
                }

                streamReader ??= new StreamReader(fileStream);

                while (await streamReader.ReadLineAsync(cancellationToken) is { } line)
                {
                    try
                    {
                        var item = CombatLogLine.Parse(line.AsMemory());

                        if (item is not null)
                        {
                            CombatLogLines.OnNext(item);                   
                            CombatLogChanged?.Invoke(this, item);
                        }
                            
                        position = streamReader.BaseStream.Position;
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Failed to parse line: {Line}", line);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
        finally
        {
            if (fileStream is not null)
                await fileStream.DisposeAsync();
            streamReader?.Dispose();
        }
    }

    private async Task MonitorAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Monitor async");

        while (!cancellationToken.IsCancellationRequested)
        {
            CombatLogs.CombatLogsDirectory.Refresh();

            if (CombatLogs.CombatLogsDirectory.LastWriteTime != _lastWriteTime)
            {
                var latestLogFile = CombatLogs.GetLatestCombatLog();

                if (latestLogFile is not null)
                {
                    CombatLogAdded?.Invoke(this, latestLogFile);

                    _lastFileName = latestLogFile.FileInfo.FullName;
                    _lastWriteTime = CombatLogs.CombatLogsDirectory.LastWriteTime;
                    _logger.LogDebug("CombatLogsDirectory.LastWriteTime != LastWriteTime");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }

    public class PlayerStats
    {
        public Actor Player { get; set; } = null!;

        public double? HPS { get; set; }

        public double? HPSCritP { get; set; }

        public double? DPS { get; set; }

        public double? DPSCritP { get; set; }
    }
}