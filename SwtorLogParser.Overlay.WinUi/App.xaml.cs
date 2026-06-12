using Microsoft.UI.Xaml;

namespace SwtorLogParser.Overlay.WinUi;

/// <summary>
/// WinUI 3 application bootstrap. Scope for Phase 8 is an empty window only:
/// no CombatLogsMonitor, no DPS/HPS stream subscription, no interop (those land in later phases).
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
