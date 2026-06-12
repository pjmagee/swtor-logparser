using System.Text.Json;
using System.Text.Json.Serialization;

namespace SwtorLogParser.Overlay.ImGui;

/// <summary>
/// Persisted overlay state (OVL-07): window position, panel opacity, and ImGui font scale. Stored as a
/// local JSON file under <c>%LocalAppData%\SwtorLogParser\imgui-overlay.json</c> (no roaming/packaged
/// store). Window size is derived from content (auto-fit), so it is not persisted.
/// </summary>
public sealed class OverlaySettings
{
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public double Opacity { get; set; } = 0.45d;   // panel background alpha over the clear game
    public float FontScale { get; set; } = 1.4f;    // ImGui global font scale (legibility over a game)
    public bool ShowLog { get; set; }               // expanded mini combat-log panel visible
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(OverlaySettings))]
internal sealed partial class OverlaySettingsContext : JsonSerializerContext;

/// <summary>Corruption-safe load/save for <see cref="OverlaySettings"/> — never throws on read/write.</summary>
public sealed class SettingsService
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SwtorLogParser", "imgui-overlay.json");

    public OverlaySettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new OverlaySettings();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize(json, OverlaySettingsContext.Default.OverlaySettings) ?? new OverlaySettings();
        }
        catch
        {
            return new OverlaySettings(); // missing/corrupt → defaults
        }
    }

    public void Save(OverlaySettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = JsonSerializer.Serialize(settings, OverlaySettingsContext.Default.OverlaySettings);
            File.WriteAllText(_path, json);
        }
        catch
        {
            // best-effort persistence; never crash the overlay on a write failure
        }
    }
}
