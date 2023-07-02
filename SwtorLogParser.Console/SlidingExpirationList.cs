namespace SwtorLogParser.Cli;

using System;
using System.Collections.Generic;
using System.Threading;

public class SlidingExpirationList
{
    private readonly SortedList<string, PlayerStats> _items;
    private readonly Timer _expirationTimer;
    private readonly TimeSpan _expirationTime;
    
    public IReadOnlyList<PlayerStats> Items
    {
        get
        {
            lock (_items) return _items.Values.AsReadOnly();
        }
    }

    public SlidingExpirationList(TimeSpan expirationTime)
    {
        _items = new SortedList<string, PlayerStats>();
        _expirationTime = expirationTime;
        _expirationTimer = new Timer(RemoveExpiredItems, null, expirationTime, expirationTime);
    }

    public void AddOrUpdate(PlayerStats item)
    {
        lock (_items)
        {
            if (_items.ContainsKey(item.Player))
            {
                if (item.HPS.HasValue)
                {
                    _items[item.Player].HPS = item.HPS;
                    _items[item.Player].HPSCritP = item.HPSCritP;
                }

                if (item.DPS.HasValue)
                {
                    _items[item.Player].DPS = item.DPS;
                    _items[item.Player].DPSCritP = item.DPSCritP;
                }
            }
            else
            {
                _items.Add(item.Player, item);
            }
            
            _items[item.Player].Expiration = DateTime.Now.Add(_expirationTime);
        }
    }

    private void RemoveExpiredItems(object? state)
    {
        var expiredItems = new List<PlayerStats>();

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
                _items.Remove(item.Player);
            }
        }
    }
}
