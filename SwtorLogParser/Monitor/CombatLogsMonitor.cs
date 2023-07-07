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

    public ReplaySubject<CombatLogLine> CombatLogLines { get; } = new(TimeSpan.FromMinutes(1));

    private DateTime? _lastWriteTime;
    private string? _lastFileName;

    public IObservable<PlayerStats> DPS { get; private set; }

    public IObservable<PlayerStats> HPS { get; private set; }


    private CombatLogsMonitor()
    {
        ConfigureObservables();
    }

    private void ConfigureObservables()
    {
        DPS = CombatLogLines
            .Where(combatLogLine => combatLogLine.TimeStamp > DateTime.Now.AddSeconds(-10))
            .Where(combatLogLine => combatLogLine.IsPlayerDamage())
            .GroupBy(combatLogLine => combatLogLine.Source!.Name)
            .SelectMany(group =>
            {
                return group
                    .Buffer(TimeSpan.FromSeconds(2))
                    .Where(b => b.Count >= 2)
                    .Select(buffer =>
                    {
                        var sortedBuffer = buffer.OrderBy(x => x.TimeStamp).ToList();
                        var totalSeconds = (sortedBuffer.Last().TimeStamp - sortedBuffer.First().TimeStamp)
                            .TotalSeconds;
                        var sum = sortedBuffer.Sum(x => x.Value!.Total);
                        var critical = sortedBuffer.Count(x => x.Value!.IsCritical) * 100.0 / sortedBuffer.Count;

                        _logger.LogDebug("seconds: {TotalSeconds}. sum: {Sum}. critical: {Critical}", totalSeconds, sum,
                            critical);

                        return new PlayerStats
                        {
                            Player = sortedBuffer[0].Source!, DPS = sum / totalSeconds,
                            DPSCritP = double.IsInfinity(critical) ? null : critical
                        };
                    });
            });

        HPS = CombatLogLines
            .Where(combatLogLine => combatLogLine.TimeStamp > DateTime.Now.AddSeconds(-10))
            .Where(combatLogLine => combatLogLine.IsPlayerHeal())
            .GroupBy(combatLogLine => combatLogLine.Source!.Name)
            .SelectMany(group =>
            {
                return group
                    .Buffer(TimeSpan.FromSeconds(5))
                    .Where(b => b.Count >= 2)
                    .Select(buffer =>
                    {
                        var sortedBuffer = buffer.OrderBy(x => x.TimeStamp).ToList();
                        var totalSeconds = (sortedBuffer.Last().TimeStamp - sortedBuffer.First().TimeStamp)
                            .TotalSeconds;
                        var sum = sortedBuffer.Sum(x => x.Value!.Total);
                        var critical = sortedBuffer.Count(x => x.Value!.IsCritical) * 100.0 / sortedBuffer.Count;

                        _logger.LogDebug("seconds: {TotalSeconds}. sum: {Sum}. critical: {Critical}", totalSeconds, sum,
                            critical);

                        return new PlayerStats
                        {
                            Player = sortedBuffer[0].Source!, HPS = sum / totalSeconds,
                            HPSCritP = double.IsInfinity(critical) ? null : critical
                        };
                    });
            });
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
                    fileStream?.Dispose();
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

                while (!streamReader.EndOfStream)
                {
                    var line = streamReader.ReadLine();

                    if (line is not null)
                        try
                        {
                            var item = CombatLogLine.Parse(line.AsMemory());

                            if (item is not null)
                            {
                                CombatLogLines.OnNext(item);
                                CombatLogChanged?.Invoke(this, item);
                            }
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, "Failed to parse line: {Line}", line);
                        }

                    position = streamReader.BaseStream.Position;
                }

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
        finally
        {
            fileStream?.DisposeAsync();
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