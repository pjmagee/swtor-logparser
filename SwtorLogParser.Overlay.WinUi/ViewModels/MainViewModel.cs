using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using SwtorLogParser.Monitor;
using SwtorLogParser.View;

namespace SwtorLogParser.Overlay.WinUi.ViewModels;

/// <summary>
/// Owns the live render pipeline for the overlay (OVL-02). It bridges the frozen, thread-affine
/// boundary between the background Rx <see cref="CombatLogsMonitor.DpsHps"/> stream and the
/// UI-thread-only XAML collection, following ARCHITECTURE.md "Pattern 1" and the locked decisions:
///
/// <list type="bullet">
/// <item>D-01: aggregation stays OFF the UI thread in the core <see cref="SlidingExpirationList"/>
///   (internally locked); the UI is refreshed only on a 1s <see cref="DispatcherQueueTimer"/> tick —
///   never per <c>OnNext</c>. This caps UI churn regardless of combat-log volume (mitigates T-09-01).</item>
/// <item>D-02: the captured UI <see cref="DispatcherQueue"/> is read ONCE on the UI thread (ctor).
///   The Rx <c>OnNext</c> handler touches ONLY the locked core list — it never reads the dispatcher or
///   mutates <see cref="Rows"/> / any XAML object (mitigates T-09-02 / COMException 0x8001010E).</item>
/// <item>D-03: rows are sorted DPS-descending at render time over the immutable <c>Items</c> snapshot;
///   zero/null DPS rows sort last. The core list keeps its <c>Player.Id</c> keying unchanged.</item>
/// </list>
///
/// <see cref="Dispose"/> disposes the subscription and stops the timer (IN-03: no leaked subscription/timer).
/// </summary>
public sealed class MainViewModel : IDisposable
{
    private readonly DispatcherQueue _ui;
    private readonly SlidingExpirationList _core;
    private readonly IDisposable _sub;
    private readonly DispatcherQueueTimer _renderTimer;
    private bool _disposed;

    /// <summary>UI-thread-only render mirror bound via <c>x:Bind</c> from the ListView.</summary>
    public ObservableCollection<EntryViewModel> Rows { get; } = new();

    /// <param name="monitor">
    /// The stream producer. Defaults to <see cref="CombatLogsMonitor.Instance"/> (the singleton the
    /// WinForms host also resolves); injectable to keep the VM testable.
    /// </param>
    public MainViewModel(CombatLogsMonitor? monitor = null)
    {
        monitor ??= CombatLogsMonitor.Instance;

        // Must be constructed on the UI thread so the captured queue is the UI dispatcher (D-02).
        _ui = DispatcherQueue.GetForCurrentThread()
              ?? throw new InvalidOperationException(
                  "MainViewModel must be constructed on the UI thread (DispatcherQueue.GetForCurrentThread() was null).");

        // Reuse the core list unchanged: 10s sliding expiry, keyed by Player.Id, internally locked.
        _core = new SlidingExpirationList(TimeSpan.FromSeconds(10));

        // OnNext runs on the monitor's background reader thread. It does the ONLY allowed off-thread
        // work — feeding the internally-locked core list. It must NOT touch Rows or the dispatcher.
        // CR-01: guard null Player.Id — the core list force-unwraps Player.Id!.Value, and Actor.Id is
        // long? (null for malformed actors). An unguarded null would throw on the reader thread and
        // fault the subscription, silently killing the live stream (the milestone's core value).
        // SyncRows already treats null-id stats as non-renderable, so dropping them here is consistent.
        // The onError keeps an otherwise-silent stream fault observable instead of vanishing.
        _sub = monitor.DpsHps.Subscribe(
            stats =>
            {
                if (stats.Player?.Id is not null)
                    _core.AddOrUpdate(stats);
            },
            ex => System.Diagnostics.Debug.WriteLine($"DpsHps stream faulted: {ex}"));

        // 1s UI-thread render tick mirrors the core snapshot into Rows (parity with the WinForms timer).
        _renderTimer = _ui.CreateTimer();
        _renderTimer.Interval = TimeSpan.FromSeconds(1);
        _renderTimer.Tick += OnRenderTick;
        _renderTimer.Start();
    }

    private void OnRenderTick(DispatcherQueueTimer sender, object args) => SyncRows(_core.Items);

    /// <summary>
    /// Reconciles the DPS-descending ordered snapshot into <see cref="Rows"/> on the UI thread:
    /// updates existing rows in place by <c>Player.Id</c>, inserts new rows at the correct ordered
    /// position, and removes rows no longer in the snapshot — avoiding a full clear/rebuild that would
    /// flicker the ListView. Must be called on the UI thread (it is, from the DispatcherQueueTimer tick).
    /// </summary>
    internal void SyncRows(IReadOnlyList<CombatLogsMonitor.PlayerStats> snapshot)
    {
        var ordered = OrderByDpsDescending(snapshot).ToList();

        // Remove stale rows (present in Rows, absent from the new snapshot) by Player.Id.
        var liveIds = new HashSet<long>();
        foreach (var s in ordered)
            if (s.Player?.Id is { } id)
                liveIds.Add(id);

        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            var rowId = Rows[i].PlayerId;
            if (rowId is null || !liveIds.Contains(rowId.Value))
                Rows.RemoveAt(i);
        }

        // Upsert in order: update-in-place if present, else insert at the ordered index.
        for (var i = 0; i < ordered.Count; i++)
        {
            var stats = ordered[i];
            var id = stats.Player?.Id;

            var existingIndex = -1;
            for (var j = 0; j < Rows.Count; j++)
            {
                if (Rows[j].PlayerId == id)
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                Rows[existingIndex].Update(stats);
                if (existingIndex != i)
                    Rows.Move(existingIndex, i);
            }
            else
            {
                Rows.Insert(i, new EntryViewModel(stats));
            }
        }
    }

    /// <summary>
    /// Pure DPS-descending ordering (D-03): highest DPS first, zero/null DPS last. Stable, no WinUI
    /// dependency — operates only on core types so it can be unit-tested without a DispatcherQueue.
    /// </summary>
    public static IEnumerable<CombatLogsMonitor.PlayerStats> OrderByDpsDescending(
        IReadOnlyList<CombatLogsMonitor.PlayerStats> snapshot) =>
        snapshot.OrderByDescending(s => s.DPS ?? 0d);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _sub.Dispose();             // stop feeding the core list (no leaked subscription)
        _renderTimer.Stop();        // stop the UI render tick (no leaked timer)
        _renderTimer.Tick -= OnRenderTick;
    }
}
