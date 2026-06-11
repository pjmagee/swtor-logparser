using System.ComponentModel;
using SwtorLogParser.Monitor;
using CoreList = SwtorLogParser.View.SlidingExpirationList;
using Timer = System.Threading.Timer;

namespace SwtorLogParser.Overlay.View;

/*
 * In WinForms, it supports IBindingList<T>
 * We inherit from the basic BindingList<T> to bind to the DataGridView.
 *
 * The add/update bookkeeping and the sliding-expiration logic live in exactly ONE place:
 * the UI-free core list (SwtorLogParser.View.SlidingExpirationList). This WinForms adapter
 * COMPOSES that core list and only owns the host-side concern: projecting the core's
 * PlayerStats into display Entry rows and refreshing the DataGridView on a render timer.
 */
public class SlidingExpirationList : BindingList<Entry>
{
    private static readonly object Lock = new();
    private readonly CoreList _core;
    private readonly Timer _renderTimer;
    private readonly Control _control;

    public SlidingExpirationList(Control control, TimeSpan expirationTime)
    {
        _control = control;
        _core = new CoreList(expirationTime);
        _renderTimer = new Timer(Redraw, null, 1000, 1000);
    }

    private void Redraw(object? state)
    {
        _control.Invoke(Refresh);
    }

    public void AddOrUpdate(CombatLogsMonitor.PlayerStats item)
    {
        _core.AddOrUpdate(item);
    }

    private void Refresh()
    {
        lock (Lock)
        {
            var stats = _core.Items
                .OrderBy(x => x.Player.Name, StringComparer.Ordinal)
                .ToList();

            ClearItems();
            for (var index = 0; index < stats.Count; index++)
            {
                InsertItem(index, new Entry { Stats = stats[index] });
            }

            _control.Refresh();
        }
    }
}
