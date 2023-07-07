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
    private readonly Form _form;
    private readonly TimeSpan _expirationTime;
    private readonly Timer _expirationTimer;
    
    private static readonly object Lock = new();

    public SlidingExpirationList(Form form, TimeSpan expirationTime)
    {
        _form = form;
        _expirationTime = expirationTime;
        _expirationTimer = new Timer(RemoveExpiredItems, null, expirationTime, expirationTime);
        RaiseListChangedEvents = true;
        AllowRemove = true;
        AllowEdit = true;
    }

    public void AddOrUpdate(CombatLogsMonitor.PlayerStats item)
    {
        _form.Invoke(() =>
        {
            lock (Lock)
            {
                if (Count > 0)
                {
                    for (int i = 0; i < Count; i++)
                    {
                        var entry = this[i];
                    
                        if (entry.Stats.Player.Id == item.Player.Id)
                        {
                            if (item.HPS.HasValue)
                            {
                                entry.Stats.HPS = item.HPS;
                                entry.Stats.HPSCritP = item.HPSCritP;
                            }

                            if (item.DPS.HasValue)
                            {
                                entry.Stats.DPS = item.DPS;
                                entry.Stats.DPSCritP = item.DPSCritP;
                            }

                            entry.Expiration = DateTime.Now.Add(_expirationTime);
                    
                            ResetItem(i);
                        }
                    }
                }
                else
                {
                    Add(new Entry() { Stats = item, Expiration = DateTime.Now.Add(_expirationTime) });
                }
            }
        });
    }

    private void RemoveExpiredItems(object? state)
    {
        var expiredItems = new List<Entry>();

        lock (Lock)
        {
            foreach (var item in this)
            {
                if (item.Expiration <= DateTime.Now)
                {
                    expiredItems.Add(item);
                }
            }

            foreach (var item in expiredItems)
            {
                _form.Invoke(() => Remove(item));
            }
        }
    }
}
