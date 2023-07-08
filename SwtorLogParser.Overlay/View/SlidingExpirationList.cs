using System.ComponentModel;
using SwtorLogParser.Monitor;
using Timer = System.Threading.Timer;

namespace SwtorLogParser.Overlay.View;

/*
 * In WinForms, it supports IBindingList<T>
 * We inherit from the basic BindingList<T> and add auto removal logic using the Timer class
 */
public class SlidingExpirationList : BindingList<Entry>
{
    private static readonly object Lock = new();
    private readonly TimeSpan _expirationTime;
    private readonly Timer _expirationTimer;
    private readonly Timer _renderTimer;
    private readonly Control _control;
    private readonly List<Entry> _list = new();

    public SlidingExpirationList(Control control, TimeSpan expirationTime)
    {
        _control = control;
        _expirationTime = expirationTime;
        _expirationTimer = new Timer(RemoveExpiredItems, null, expirationTime, expirationTime);
        _renderTimer = new Timer(Redraw, null, 1000, 1000);
    }

    private void Redraw(object? state)
    {
        _control.Invoke(Refresh);
    }

    public void AddOrUpdate(CombatLogsMonitor.PlayerStats item)
    {
        lock (Lock)
        {
            Entry? entry = _list.Find(x => x.Stats.Player.Id == item.Player.Id);

            if (entry is null)
            {
                _list.Add(new Entry { Stats = item, Expiration = DateTime.Now.Add(_expirationTime) });
            }
            else
            {
                entry.Stats = item;
                entry.Expiration = DateTime.Now.Add(_expirationTime);
            }
                
            _list.Sort();
        }
    }

    private void Refresh()
    {
        lock (Lock)
        {
            ClearItems();
            for (var index = 0; index < _list.Count; index++)
            {
                var item = _list[index];
                InsertItem(index, item);
            }
        
            _control.Refresh();
        }
    }

    private void RemoveExpiredItems(object? state)
    {
        lock (Lock)
        {
            var expiredItems = new List<Entry>();
                
            foreach (var item in _list)
            {
                if (item.Expiration <= DateTime.Now)
                    expiredItems.Add(item);
            }

            foreach (var item in expiredItems)
            {
                _list.Remove(item);
            }

            _list.Sort();
        }
        
    }
}