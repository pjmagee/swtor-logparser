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

    // RFCT-02: defined unconditionally in every build configuration (previously gated behind
    // #if RELEASE/#elif DEBUG with no #else, leaving Instance undefined for any other config).
    // The default singleton is NullLogger-backed; console/debug logging providers move host-side
    // to keep the core library IsAotCompatible (no reflection/DI container here).
    public static CombatLogsMonitor Instance { get; } = new(NullLogger<CombatLogsMonitor>.Instance);

    private Task? _monitor;
    private Task? _reader;
    private CancellationTokenSource? _cancellationTokenSource;

    public event EventHandler<CombatLogLine>? CombatLogChanged;
    public event EventHandler<CombatLog>? CombatLogAdded;

    public bool IsRunning =>
        _monitor is { IsCompleted: false } && _reader is { IsCompleted: false };

    private Subject<CombatLogLine> CombatLogLines { get; } = new Subject<CombatLogLine>();

    // TEST-01 seam: lets tests push lines into the Rx pipeline through the
    // InternalsVisibleTo(SwtorLogParser.Tests) grant without exposing the Subject itself.
    internal void PublishForTest(CombatLogLine line) => CombatLogLines.OnNext(line);

    private DateTime? _lastWriteTime;
    private string? _lastFileName;
    public IObservable<PlayerStats> DpsHps { get; private set; }

    private static readonly CombatLogLineComparer CombatLogLineComparer = new();

    private CombatLogsMonitor()
    {
        // Default to NullLogger so _logger is always non-null even on this base path; the public
        // ILogger ctor (which chains here) overwrites it afterwards. DpsHps is assigned in
        // ConfigureObservables. Together these clear the CS8618 warnings (RFCT-02).
        _logger = NullLogger<CombatLogsMonitor>.Instance;
        DpsHps = ConfigureObservables();
    }

    private IObservable<PlayerStats> ConfigureObservables()
    {
        return CombatLogLines
            .Where(x => x.TimeStamp > DateTime.Now.AddSeconds(-10))
            .Where(x => x.Source is not null && x.Source.Name is not null)
            .GroupBy(x => x.Source?.Name)
            .SelectMany(g =>
                g.Where(l => l.IsPlayerDamage() || l.IsPlayerHeal())
                    .DistinctUntilChanged()
                    .Scan(new HashSet<CombatLogLine>(CombatLogLineComparer), Accumulator)
                    .Select(CalculateDpsHpsStats)
            );
    }

    private static readonly object Lock = new();

    // TEST-02 seam: internal (was private) so DpsHpsMathTests can call the DPS/HPS math directly
    // via the existing InternalsVisibleTo(SwtorLogParser.Tests) grant — this bypasses the
    // DateTime.Now Where filter in the Rx pipeline for deterministic assertions. VISIBILITY-ONLY
    // change: the lock, the 10s RemoveWhere, and the body are byte-identical to before.
    internal HashSet<CombatLogLine> Accumulator(
        HashSet<CombatLogLine> state,
        CombatLogLine combatLog
    )
    {
        lock (Lock)
        {
            state.RemoveWhere(line => line.TimeStamp < combatLog.TimeStamp.AddSeconds(-10));
            state.Add(combatLog);
            return state;
        }
    }

    // TEST-02 seam: internal (was private) so DpsHpsMathTests can assert DPS/HPS/crit% against
    // known inputs directly. VISIBILITY-ONLY change: crit% formula, order-by-TimeOfDay, and the
    // null-on-zero/infinity logic are unchanged.
    internal PlayerStats CalculateDpsHpsStats(HashSet<CombatLogLine> state)
    {
        // Oldest to latest
        var items = state.OrderBy(x => x.TimeStamp.TimeOfDay).ToList();

        var heals = items.Where(pe => pe.IsPlayerHeal()).ToList();
        var damage = items.Where(pe => pe.IsPlayerDamage()).ToList();

        var timeSpan =
            items.Count > 1 ? (items[^1].TimeStamp - items[0].TimeStamp) : TimeSpan.FromSeconds(1);

        int damageTotal = damage.Sum(pe => pe.Value!.Total);
        int healTotal = heals.Sum(pe => pe.Value!.Total);

        double dpsCrit = (double)damage.Count(pe => pe.Value!.IsCritical) / state.Count * 100;
        double hpsCrit = (double)heals.Count(pe => pe.Value!.IsCritical) / state.Count * 100;

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
            HPSCritP = hpsCritP,
        };
    }

    // RFCT-02: public constructor injection (was private) so the monitor is constructible for
    // DI hosts and tests. Chains to the parameterless ctor so ConfigureObservables() runs and
    // DpsHps/the Subject are assigned. Because Instance now flows through this ctor too, _logger
    // and DpsHps are always assigned — clearing the two CS8618 warnings on the parameterless ctor.
    public CombatLogsMonitor(ILogger<CombatLogsMonitor> logger)
        : this()
    {
        _logger = logger;
    }

    public void Start(CancellationToken cancellationToken)
    {
        // Dispose any previous linked source before creating a new one so a repeated
        // Start() does not orphan the prior CTS (which would leak a registration on the
        // outer token for its lifetime). Cancel first so existing workers wind down.
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        var token = _cancellationTokenSource.Token;
        _monitor = Task.Factory.StartNew(() => MonitorAsync(token), token);
        _reader = Task.Factory.StartNew(() => ReadAsync(token), token);
    }

    public void Stop()
    {
        try
        {
            _cancellationTokenSource?.Cancel();
        }
        catch (Exception e)
        {
            _logger?.LogError(e, "_cancellationTokenSource.Cancel() failed");
        }
        finally
        {
            // Dispose and null the linked CTS so it does not leak; null-safe so a
            // Stop()-before-Start() (BUG-02) stays a no-op.
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            _monitor = null;
            _reader = null;
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

                if (string.IsNullOrWhiteSpace(current))
                {
                    // No file selected yet: await the read cadence before retrying so the
                    // loop does not busy-spin at 100% CPU (the read-loop Task.Delay below is
                    // skipped by the continue). Respects cancellation.
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                if (fileStream is null)
                {
                    fileStream = new FileStream(
                        current,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite
                    );
                    fileStream.Seek(position, SeekOrigin.Begin);
                }

                streamReader ??= new StreamReader(fileStream, System.Text.Encoding.Latin1);

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
