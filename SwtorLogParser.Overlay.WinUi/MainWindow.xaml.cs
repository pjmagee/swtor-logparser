using System;
using System.ComponentModel;
using System.Threading;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SwtorLogParser.Monitor;
using SwtorLogParser.Overlay.WinUi.Interop;
using SwtorLogParser.Overlay.WinUi.Settings;
using SwtorLogParser.Overlay.WinUi.ViewModels;
using Windows.Graphics;
using WinRT.Interop;

namespace SwtorLogParser.Overlay.WinUi;

/// <summary>
/// WinUI 3 overlay window. Phase 9 added the live DPS/HPS render surface; Phase 10 turns it into a
/// borderless, always-on-top, translucent, click-through-capable game overlay via CsWin32 interop
/// (<see cref="WindowInterop"/>): layered whole-window opacity (OVL-04/07), caption drag (OVL-05),
/// a click-through toggle with a global Ctrl+Alt+O hotkey (OVL-06), tool-window / no-activate styles so
/// it never steals focus or shows in Alt-Tab (INT-03), and HWND_TOPMOST re-assert on foreground changes
/// so it stays over a borderless/windowed game (INT-02 / BL-01). Exclusive-fullscreen is unsupported.
/// </summary>
public sealed partial class MainWindow : Window, INotifyPropertyChanged
{
    private const double MinFontSize = 8d;
    private const double MaxFontSize = 48d;
    private const double DefaultOpacity = 0.90d;

    private readonly SettingsService _settings = new();
    private readonly WindowInterop _interop;
    private readonly DispatcherQueueTimer _hotkeyTimer;
    private double _fontSize = OverlaySettings.DefaultFontSize;
    private double _opacity = DefaultOpacity;
    private bool _clickThrough;
    private bool _chordWasDown;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel ViewModel { get; }

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

        // Borderless + always-on-top presenter (the chrome the overlay needs; no title bar to drag,
        // hence the custom drag handle below). Acrylic backdrop gives reliable WinUI translucency
        // (WS_EX_LAYERED is avoided — it blanks the compositor); the Root tint brush sets opacity.
        ConfigureBorderlessAlwaysOnTop();
        SystemBackdrop = new DesktopAcrylicBackdrop();

        _interop = new WindowInterop(WindowNative.GetWindowHandle(this));

        // Load persisted settings: placement + font + opacity. Missing/corrupt → defaults (never throws).
        ApplySavedSettings(_settings.Load());

        // Apply the tool-window / no-activate styles and the foreground-topmost hook AFTER the window is
        // shown — applying no-activate before first paint can suppress the window entirely. TryEnqueue
        // runs once the message loop is pumping (post-Activate).
        DispatcherQueue.TryEnqueue(() =>
        {
            _interop.ApplyOverlayStyles();          // INT-03 tool-window + no-activate
            _interop.StartForegroundTopmostHook();  // INT-02 / BL-01 topmost re-assert
        });

        // Start the monitor here rather than on Activated: WS_EX_NOACTIVATE means the window may never
        // raise a normal activation, so first-activation start (the WinForms pattern) is unreliable.
        if (!CombatLogsMonitor.Instance.IsRunning)
            CombatLogsMonitor.Instance.Start(CancellationToken.None);

        // Poll the global click-through toggle chord (Ctrl+Alt+O). Polling works even when the window is
        // click-through + no-activate (it never holds keyboard focus, so XAML accelerators wouldn't fire).
        _hotkeyTimer = DispatcherQueue.CreateTimer();
        _hotkeyTimer.Interval = TimeSpan.FromMilliseconds(150);
        _hotkeyTimer.Tick += OnHotkeyTick;
        _hotkeyTimer.Start();

        Closed += OnClosed;
    }

    private void ConfigureBorderlessAlwaysOnTop()
    {
        if (GetAppWindow().Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
    }

    private void IncreaseFont_Click(object sender, RoutedEventArgs e) => FontSize += 1d;

    private void DecreaseFont_Click(object sender, RoutedEventArgs e) => FontSize -= 1d;

    // OVL-05: start the OS caption move loop from a press on the drag handle.
    private void DragHandle_PointerPressed(object sender, PointerRoutedEventArgs e) => _interop.BeginDrag();

    // OVL-06: user clicked the toggle (only reachable when NOT click-through). Apply the new state.
    private void ClickThroughToggle_Click(object sender, RoutedEventArgs e)
        => ApplyClickThrough(ClickThroughToggle.IsChecked == true);

    // OVL-07 opacity: slider is 25..100 (%). Apply as the Root panel tint-brush alpha (over the acrylic
    // backdrop) and remember it. Lower opacity → more of the game's acrylic shows through.
    private void OpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _opacity = e.NewValue / 100d;
        ApplyOpacity(_opacity);
    }

    // Tint the Root panel with the chosen opacity over the acrylic backdrop. No WS_EX_LAYERED.
    private void ApplyOpacity(double opacity) =>
        Root.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(OpacityToAlpha(opacity), 0x10, 0x10, 0x10));

    private void OnHotkeyTick(DispatcherQueueTimer sender, object args)
    {
        var down = WindowInterop.IsToggleChordDown();
        if (down && !_chordWasDown)          // rising edge → toggle once per press
            ApplyClickThrough(!_clickThrough);
        _chordWasDown = down;
    }

    private void ApplyClickThrough(bool enabled)
    {
        _clickThrough = enabled;
        _interop.SetClickThrough(enabled);
        if (ClickThroughToggle.IsChecked != enabled)
            ClickThroughToggle.IsChecked = enabled; // keep the button in sync when toggled by hotkey
    }

    private static byte OpacityToAlpha(double opacity) => (byte)Math.Clamp(opacity * 255d, 0d, 255d);

    private AppWindow GetAppWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }

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

        // Opacity: setting the slider value raises ValueChanged → ApplyOpacity (Root tint brush).
        _opacity = settings.Opacity is { } o && o is > 0d and <= 1d ? o : DefaultOpacity;
        OpacitySlider.Value = _opacity * 100d;
        ApplyOpacity(_opacity); // ensure applied even if the slider value didn't actually change
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Closed -= OnClosed;
        _hotkeyTimer.Stop();
        _hotkeyTimer.Tick -= OnHotkeyTick;

        SaveSettings();

        _interop.Dispose();   // unhook the foreground WinEvent hook
        ViewModel.Dispose();  // dispose the DpsHps subscription + render timer
    }

    private void SaveSettings()
    {
        var settings = new OverlaySettings { FontSize = FontSize, Opacity = _opacity };

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
            // If placement can't be read, still persist font + opacity; placement stays null → default.
        }

        _settings.Save(settings);
    }
}
