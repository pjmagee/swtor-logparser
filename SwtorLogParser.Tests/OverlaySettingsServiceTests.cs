using SwtorLogParser.Overlay.WinUi.Settings;
using Xunit;

namespace SwtorLogParser.Tests;

/// <summary>
/// Round-trip + corruption-robustness tests for the overlay settings persistence (plan 09-02, Task 1).
///
/// These exercise the REAL <see cref="SettingsService"/> / <see cref="OverlaySettings"/> /
/// <see cref="OverlaySettingsContext"/> source files, which are <c>&lt;Compile Include Link&gt;</c>'d
/// into this plain net10.0 test project. The three files depend ONLY on System.Text.Json + System.IO
/// (no WinUI / WinAppSDK types), so linking them keeps the WinUI TFM out of the test/AOT graph while
/// still verifying the actual code (D-05/D-06, T-09-04).
///
/// Each test targets a fresh temp path via the path-override constructor — never the real
/// <c>%LocalAppData%</c> file.
/// </summary>
public sealed class OverlaySettingsServiceTests
{
    private static string NewTempPath() =>
        Path.Combine(Path.GetTempPath(), "SwtorLogParserTests", Guid.NewGuid().ToString("N"), "settings.json");

    [Fact]
    public void Load_MissingFile_ReturnsDefaults_DoesNotThrow()
    {
        var path = NewTempPath();
        Assert.False(File.Exists(path));

        var service = new SettingsService(path);
        var settings = service.Load();

        Assert.NotNull(settings);
        Assert.Equal(OverlaySettings.DefaultFontSize, settings.FontSize);
        Assert.Null(settings.WindowX);
        Assert.Null(settings.WindowY);
        Assert.Null(settings.WindowWidth);
        Assert.Null(settings.WindowHeight);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaults_DoesNotThrow()
    {
        var path = NewTempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");

        var service = new SettingsService(path);
        var settings = service.Load();

        Assert.NotNull(settings);
        Assert.Equal(OverlaySettings.DefaultFontSize, settings.FontSize);
        Assert.Null(settings.WindowX);
        Assert.Null(settings.WindowWidth);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips_PositionSizeAndFont()
    {
        var path = NewTempPath();
        var service = new SettingsService(path);

        var saved = new OverlaySettings
        {
            WindowX = 120,
            WindowY = 340,
            WindowWidth = 800,
            WindowHeight = 480,
            FontSize = 22d
        };

        service.Save(saved);
        Assert.True(File.Exists(path));

        var loaded = new SettingsService(path).Load();

        Assert.Equal(120, loaded.WindowX);
        Assert.Equal(340, loaded.WindowY);
        Assert.Equal(800, loaded.WindowWidth);
        Assert.Equal(480, loaded.WindowHeight);
        Assert.Equal(22d, loaded.FontSize);
    }

    [Fact]
    public void Save_CreatesDirectory_WhenAbsent()
    {
        var path = NewTempPath();
        var directory = Path.GetDirectoryName(path)!;
        Assert.False(Directory.Exists(directory));

        new SettingsService(path).Save(new OverlaySettings { FontSize = 16d });

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void DefaultPath_IsUnderLocalAppData_SwtorLogParser()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var expected = Path.Combine(localAppData, "SwtorLogParser", "settings.json");

        Assert.Equal(expected, SettingsService.DefaultPath());
    }
}
