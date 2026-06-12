using System.Text.Json;

namespace SwtorLogParser.Overlay.WinUi.Settings;

/// <summary>
/// Loads and saves <see cref="OverlaySettings"/> as JSON under
/// <c>%LocalAppData%\SwtorLogParser\settings.json</c> (D-05/D-06).
///
/// <para>
/// WinUI-free by design: it depends only on <see cref="System.Text.Json"/> + <see cref="System.IO"/>,
/// so it carries no dependency on the core library (OVL-07 "no core dependency") and no WinAppSDK types
/// (it can be unit-tested from a plain net10.0 test project by linking the source, without dragging the
/// overlay's WinUI TFM into the test/AOT graph).
/// </para>
///
/// <para><b>Robustness (D-06 / T-09-04):</b> a missing, malformed, or unreadable file degrades to
/// <see cref="OverlaySettings"/> defaults — <see cref="Load"/> NEVER throws. A failed write (e.g. a
/// non-writable disk) is swallowed by <see cref="Save"/> so window close can never crash.</para>
///
/// <para><b>Path safety (T-09-05):</b> the settings path is built ONLY from
/// <see cref="Environment.SpecialFolder.LocalApplicationData"/> + the fixed <c>"SwtorLogParser"</c>
/// subfolder + the fixed <c>"settings.json"</c> name. No persisted/user-controlled value ever
/// contributes to the path; persisted window coordinates are plain numbers, never paths.</para>
/// </summary>
public sealed class SettingsService
{
    private const string AppFolderName = "SwtorLogParser";
    private const string FileName = "settings.json";

    private readonly string _path;

    /// <summary>
    /// Creates a service targeting the default
    /// <c>%LocalAppData%\SwtorLogParser\settings.json</c> path.
    /// </summary>
    public SettingsService() : this(DefaultPath())
    {
    }

    /// <summary>
    /// Creates a service targeting an explicit settings-file path. Intended for unit tests
    /// (round-trip / corruption) so they can target a temp file. Production code uses the
    /// parameterless constructor.
    /// </summary>
    /// <param name="settingsPath">Absolute path of the settings JSON file.</param>
    public SettingsService(string settingsPath)
    {
        _path = settingsPath;
    }

    /// <summary>The settings-file path this service reads/writes.</summary>
    public string SettingsPath => _path;

    /// <summary>
    /// Computes the fixed default settings path under <c>%LocalAppData%\SwtorLogParser</c>. Built only
    /// from <see cref="Environment.SpecialFolder.LocalApplicationData"/> and fixed string literals — no
    /// user/persisted input (T-09-05).
    /// </summary>
    public static string DefaultPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, AppFolderName, FileName);
    }

    /// <summary>
    /// Reads and deserializes the settings file. Returns a populated <see cref="OverlaySettings"/> when
    /// the file exists and contains valid JSON; returns <see cref="OverlaySettings"/> defaults when the
    /// file is missing, unreadable, malformed, or deserializes to null. NEVER throws (D-06 / T-09-04).
    /// </summary>
    public OverlaySettings Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new OverlaySettings();

            var json = File.ReadAllText(_path);
            var settings = JsonSerializer.Deserialize(json, OverlaySettingsContext.Default.OverlaySettings);
            return settings ?? new OverlaySettings();
        }
        catch
        {
            // Any failure (IO error, JsonException, malformed content) → safe defaults, never propagate.
            return new OverlaySettings();
        }
    }

    /// <summary>
    /// Serializes and writes the settings file, creating the
    /// <c>%LocalAppData%\SwtorLogParser</c> directory if absent. A write failure (non-writable disk,
    /// permission error, …) is swallowed so window close can never crash (D-06).
    /// </summary>
    public void Save(OverlaySettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(settings, OverlaySettingsContext.Default.OverlaySettings);
            File.WriteAllText(_path, json);
        }
        catch
        {
            // Swallow: a failed persist must never throw out of the window Closed handler.
        }
    }
}
