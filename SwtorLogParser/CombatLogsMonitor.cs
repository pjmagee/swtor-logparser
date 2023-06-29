using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SwtorLogParser;

public class CombatLogsMonitor
{
    private readonly ILogger<CombatLogsMonitor> _logger;
    
    public Stack<CombatLogLine> CombatLogItems { get; } = new();
    
    #if RELEASE
    public static CombatLogsMonitor Instance { get; } = new(NullLogger<CombatLogsMonitor>.Instance);
    #elif DEBUG
    public static CombatLogsMonitor Instance { get; } = new(LoggerFactory.Create(x => x.AddConsole()).CreateLogger<CombatLogsMonitor>());
    #endif
    
    private Task? _monitor;
    private Task? _reader;
    private readonly CancellationTokenSource _cancellationTokenSource;

    public event EventHandler<CombatLogLine>? CombatLogChanged;
    public event EventHandler<CombatLog>? CombatLogAdded; 

    private DateTime? _lastWriteTime = null;
    private string? _lastFileName = null;

    public CombatLogsMonitor(ILogger<CombatLogsMonitor> logger)
    {
        _logger = logger;
        _cancellationTokenSource = new CancellationTokenSource();
    }
    
    public void Start()
    {
        _cancellationTokenSource.TryReset();
        _monitor = Task.Factory.StartNew(() => MonitorAsync(_cancellationTokenSource.Token));
        _reader = Task.Factory.StartNew(() => ReadAsync(_cancellationTokenSource.Token));
    }

    public void Stop()
    {
        try
        {
            _cancellationTokenSource.Cancel();
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"_cancellationTokenSource.CancelAsync() failed");
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
                    CombatLogItems.Clear();

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
                    var line = await streamReader.ReadLineAsync(cancellationToken);

                    if (line is not null)
                    {
                        try
                        {
                            CombatLogLine? item =  CombatLogLine.Parse(line.AsMemory());

                            if (item is not null)
                            {
                                CombatLogItems.Push(item);
                                CombatLogChanged?.Invoke(this,  item);
                                _logger.LogDebug("{@Item}", item);
                            }
                        }
                        catch(Exception e)
                        {
                            _logger.LogError(e, "Failed to parse line: {Line}", line);
                        }                        
                    }
                }
				
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);					
                
                position = fileStream.Position;
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
        _logger.LogDebug($"Monitor async");
        
        while (!cancellationToken.IsCancellationRequested)
        {
            CombatLogs.CombatLogsDirectory.Refresh();

            if (CombatLogs.CombatLogsDirectory.LastWriteTime != _lastWriteTime)
            {
                CombatLog? latestLogFile = CombatLogs.GetLatestCombatLog();

                if (latestLogFile is not null)
                {
                    CombatLogAdded?.Invoke(this, latestLogFile);
                    
                    _lastFileName = latestLogFile.FileInfo.FullName;
                    _lastWriteTime = CombatLogs.CombatLogsDirectory.LastWriteTime;
                    _logger.LogDebug($"CombatLogsDirectory.LastWriteTime != LastWriteTime");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }
}