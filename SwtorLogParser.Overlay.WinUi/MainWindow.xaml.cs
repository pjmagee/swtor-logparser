using System.ComponentModel;
using System.Threading;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SwtorLogParser.Monitor;
using SwtorLogParser.Overlay.WinUi.Settings;
using SwtorLogParser.Overlay.WinUi.ViewModels;
using Windows.Graphics;
using WinRT.Interop;

namespace SwtorLogParser.Overlay.WinUi;

/// <summary>
/// WinUI 3 overlay window (Phase 9). Replaces the empty Phase 8 scaffold with the live DPS/HPS
/// render surface. The window stays NORMAL, OPAQUE and movable this phase (transparency / borderless
/// / click-through / drag / topmost are Phase 10).
///
/// The <see cref="MainViewModel"/> is constructed here, on the UI thread, so its captured
/// <c>DispatcherQueue</c> is the UI dispatcher (D-02). The combat-log monitor is started on first
/// window activation (parity with <c>ParserForm.OnActivated</c>), and the view-model is disposed on
/// <c>Closed</c> so the DpsHps subscription and the render timer do not leak (IN-03).
/// </summary>
public sealed partial class MainWindow : Window, INotifyPropertyChanged
{
    /// <summary>Smallest usable font size for the +/- controls (keeps the window readable).</summary>
    private const double MinFontSize = 8d;

    /// <summary>Largest font size for the +/- controls (avoids an unusably huge window).</summary>
    private const double MaxFontSize = 48d;

    private readonly SettingsService _settings = new();
    private bool _monitorStarted;
    private double _fontSize = OverlaySettings.DefaultFontSize;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel ViewModel { get; }

    /// <summary>
    /// Row + header font size (OVL-08). Bound (OneWay) by the header TextBlocks and the rows' ListView;
    /// the +/- buttons mutate it. Clamped to <see cref="MinFontSize"/>..<see cref="MaxFontSize"/>. Held
    /// in a field so it can be persisted on window close (Task 3).
    /// </summary>
    public double FontSize
    {
        get => _fontSize;
        set
        {
            var clamped = Math.Clamp(value, MinFontSize, MaxFontSize);
            if (Math.Abs(clamped - _fontSize) < double.Epsilon) return;
            _fontSize = clamped;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FontSize)));
        }
    }

    public MainWindow()
    {
        InitializeComponent();

        // Constructed on the UI thread → MainViewModel captures the UI DispatcherQueue correctly.
        ViewModel = new MainViewModel();

        // Load persisted settings on startup and apply window placement + font (D-06). Missing/corrupt
        // settings already degrade to defaults inside SettingsService.Load (never throws).
        ApplySavedSettings(_settings.Load());

        Activated += OnActivated;
        Closed += OnClosed;
    }

    // WinForms parity (ParserForm IncreaseButton_Click / DecreaseButton_Click): ±1 to header + rows.
    private void IncreaseFont_Click(object sender, RoutedEventArgs e) => FontSize += 1d;

    private void DecreaseFont_Click(object sender, RoutedEventArgs e) => FontSize -= 1d;

    /// <summary>
    /// Resolves this window's managed <see cref="AppWindow"/> via the documented HWND chain
    /// (<c>WindowNative.GetWindowHandle</c> → <c>Win32Interop.GetWindowIdFromWindow</c> →
    /// <c>AppWindow.GetFromWindowId</c>). Position/size are handled with the managed AppWindow only — no
    /// CsWin32/Win32 interop this phase (Phase 10).
    /// </summary>
    private AppWindow GetAppWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    /// <summary>
    /// Applies the loaded font size and, when a full window placement was saved, moves + resizes the
    /// window via <c>AppWindow.MoveAndResize</c>. On first run (null placement) the default placement is
    /// left untouched. A saved position that lands off-screen is acceptable this phase (not clamped).
    /// </summary>
    private void ApplySavedSettings(OverlaySettings settings)
    {
        FontSize = settings.FontSize;

        if (settings.WindowX is { } x &&
            settings.WindowY is { } y &&
            settings.WindowWidth is { } w &&
            settings.WindowHeight is { } h &&
            w > 0 && h > 0)
        {
            GetAppWindow().MoveAndResize(new RectInt32(x, y, w, h));
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        // Start the monitor exactly once (Activated fires repeatedly on focus changes). Mirrors
        // ParserForm.OnActivated: start only if not already running.
        if (_monitorStarted) return;
        if (args.WindowActivationState == WindowActivationState.Deactivated) return;

        _monitorStarted = true;
        if (!CombatLogsMonitor.Instance.IsRunning)
            CombatLogsMonitor.Instance.Start(CancellationToken.None);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Activated -= OnActivated;
        Closed -= OnClosed;

        // Save window position + size + font on close (D-06). SettingsService.Save swallows write
        // failures, so this can never throw out of Closed; reading AppWindow placement is guarded too.
        SaveSettings();

        // Dispose the DpsHps subscription and stop the render timer (no leaked subscription/timer).
        ViewModel.Dispose();
    }

    /// <summary>
    /// Reads the current window placement + font and persists them (D-06). Guarded so a failure to read
    /// the AppWindow placement cannot crash window close; the underlying write is already swallowed.
    /// </summary>
    private void SaveSettings()
    {
        var settings = new OverlaySettings { FontSize = FontSize };

        try
        {
            var appWindow = GetAppWindow();
            settings.WindowX = appWindow.Position.X;
            settings.WindowY = appWindow.Position.Y;
            settings.WindowWidth = appWindow.Size.Width;
            settings.WindowHeight = appWindow.Size.Height;
        }
        catch
        {
            // If placement can't be read, still persist the font; placement stays null → default next run.
        }

        _settings.Save(settings);
    }
}
