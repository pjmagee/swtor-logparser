using System.Threading;
using Microsoft.UI.Xaml;
using SwtorLogParser.Monitor;
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
public sealed partial class MainWindow : Window
{
    private bool _monitorStarted;

    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();

        // Constructed on the UI thread → MainViewModel captures the UI DispatcherQueue correctly.
        ViewModel = new MainViewModel();

        Activated += OnActivated;
        Closed += OnClosed;
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

        // Dispose the DpsHps subscription and stop the render timer (no leaked subscription/timer).
        ViewModel.Dispose();
    }
}
