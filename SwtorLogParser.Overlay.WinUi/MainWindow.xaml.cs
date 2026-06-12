using Microsoft.UI.Xaml;

namespace SwtorLogParser.Overlay.WinUi;

/// <summary>
/// Empty WinUI 3 overlay window. Phase 8 scaffold: the window simply opens.
/// No presenter customization, transparency, interop, or stream wiring yet.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
