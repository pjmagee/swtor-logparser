using System.Collections.Immutable;
using SwtorLogParser.Monitor;

namespace SwtorLogParser.Native.Cli.View;

public class SlidingExpirationList
{
    private readonly SortedList<long, Entry> _items;
    private readonly Timer _expirationTimer;
    private readonly TimeSpan _expirationTime;

    public IReadOnlyList<CombatLogsMonitor.PlayerStats> Items
    {
        get
        {
            lock (_items) return ImmutableList.Create(_items.Values.Select(x => x.Stats).ToArray());
        }
    }

    public SlidingExpirationList(TimeSpan expirationTime)
    {
        _items = new SortedList<long, Entry>();
        _expirationTime = expirationTime;
        _expirationTimer = new Timer(RemoveExpiredItems, null, expirationTime, expirationTime);
    }

    public void AddOrUpdate(CombatLogsMonitor.PlayerStats item)
    {
        lock (_items)
        {
            if(_items.TryGetValue(item.Player.Id!.Value, out var entry))
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
            }
            else
            {
                _items.Add(item.Player.Id!.Value, new Entry() { Stats = item });
            }

            _items[item.Player.Id!.Value].Expiration = DateTime.Now.Add(_expirationTime);
        }
    }

    private void RemoveExpiredItems(object? state)
    {
        var expiredItems = new List<Entry>();

        lock (_items)
        {
            foreach (var item in _items)
            {
                if (item.Value.Expiration <= DateTime.Now)
                {
                    expiredItems.Add(item.Value);
                }
            }

            foreach (var item in expiredItems)
            {
                _items.Remove(item.Stats.Player.Id!.Value);
            }
        }
    }
}
