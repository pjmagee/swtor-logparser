using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.UI.Xaml;
using SwtorLogParser.Monitor;
using SwtorLogParser.Overlay.WinUi.Settings;
using SwtorLogParser.Overlay.WinUi.ViewModels;

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

        Activated += OnActivated;
        Closed += OnClosed;
    }

    // WinForms parity (ParserForm IncreaseButton_Click / DecreaseButton_Click): ±1 to header + rows.
    private void IncreaseFont_Click(object sender, RoutedEventArgs e) => FontSize += 1d;

    private void DecreaseFont_Click(object sender, RoutedEventArgs e) => FontSize -= 1d;

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

        // Dispose the DpsHps subscription and stop the render timer (no leaked subscription/timer).
        ViewModel.Dispose();
    }
}
