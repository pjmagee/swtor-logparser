using System.ComponentModel;
using System.Runtime.CompilerServices;
using SwtorLogParser.Monitor;

namespace SwtorLogParser.Overlay.WinUi.ViewModels;

/// <summary>
/// Display projection of a <see cref="CombatLogsMonitor.PlayerStats"/> row for the overlay grid.
/// Exposes the five parity columns as strings (Player / DPS / Crit% / HPS / Crit%) plus a numeric
/// <see cref="DpsValue"/> sort key (NOT displayed) used by the view-model to order rows DPS-descending.
/// Implements <see cref="INotifyPropertyChanged"/> so an in-place <see cref="Update"/> of an existing
/// row refreshes the bound cells without rebuilding the collection. All formatting is delegated to the
/// WinUI-free <see cref="EntryFormat"/> helper, so this type carries no formatting logic of its own.
/// </summary>
public sealed class EntryViewModel : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _dps = string.Empty;
    private string _dCrit = string.Empty;
    private string _hps = string.Empty;
    private string _hCrit = string.Empty;
    private double _dpsValue;

    public EntryViewModel(CombatLogsMonitor.PlayerStats stats)
    {
        // Player.Id is the stable key the core list uses; capture it for in-place reconciliation.
        PlayerId = stats.Player?.Id;
        Apply(stats);
    }

    /// <summary>Stable identity key (core list keys by <c>Player.Id</c>). Null only for malformed actors.</summary>
    public long? PlayerId { get; }

    public string Name
    {
        get => _name;
        private set => Set(ref _name, value);
    }

    public string DPS
    {
        get => _dps;
        private set => Set(ref _dps, value);
    }

    public string DCrit
    {
        get => _dCrit;
        private set => Set(ref _dCrit, value);
    }

    public string HPS
    {
        get => _hps;
        private set => Set(ref _hps, value);
    }

    public string HCrit
    {
        get => _hCrit;
        private set => Set(ref _hCrit, value);
    }

    /// <summary>Numeric DPS used ONLY for sorting (null DPS → 0). Not bound to any column.</summary>
    public double DpsValue
    {
        get => _dpsValue;
        private set => Set(ref _dpsValue, value);
    }

    /// <summary>Refresh this row in place from a newer snapshot, raising PropertyChanged per changed cell.</summary>
    public void Update(CombatLogsMonitor.PlayerStats stats) => Apply(stats);

    private void Apply(CombatLogsMonitor.PlayerStats stats)
    {
        Name = EntryFormat.Name(stats);
        DPS = EntryFormat.Rate(stats.DPS);
        DCrit = EntryFormat.Crit(stats.DPSCritP);
        HPS = EntryFormat.Rate(stats.HPS);
        HCrit = EntryFormat.Crit(stats.HPSCritP);
        DpsValue = EntryFormat.DpsSortKey(stats.DPS);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
