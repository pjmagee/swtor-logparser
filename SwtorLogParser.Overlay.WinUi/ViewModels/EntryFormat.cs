using System.Globalization;
using SwtorLogParser.Monitor;

namespace SwtorLogParser.Overlay.WinUi.ViewModels;

/// <summary>
/// Pure, WinUI-free formatting helpers that project a <see cref="CombatLogsMonitor.PlayerStats"/>
/// into the display strings shown in the overlay grid. Kept in its own type with no XAML/WinAppSDK
/// dependency so the formatting + sort-key logic is trivially correct by construction (and could be
/// unit-tested over core types alone). Mirrors the WinForms grid behavior: rounded DPS/HPS, crit% as
/// a percentage string, and BLANK cells (empty string) for null values — never "0" or "null".
/// </summary>
internal static class EntryFormat
{
    /// <summary>Safe display name. Null/empty <c>Player.Name</c> renders as "?" rather than throwing.</summary>
    public static string Name(CombatLogsMonitor.PlayerStats stats)
    {
        var name = stats.Player?.Name;
        return string.IsNullOrWhiteSpace(name) ? "?" : name;
    }

    /// <summary>Rounded DPS/HPS value; null renders blank (parity with the WinForms grid's empty cells).</summary>
    public static string Rate(double? value) =>
        value.HasValue ? value.Value.ToString("F0", CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>Crit% as e.g. "42.5%"; null renders blank.</summary>
    public static string Crit(double? value) =>
        value.HasValue ? value.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%" : string.Empty;

    /// <summary>Numeric sort key for DPS-descending ordering. Null DPS sorts as 0 (to the bottom).</summary>
    public static double DpsSortKey(double? dps) => dps ?? 0d;
}
