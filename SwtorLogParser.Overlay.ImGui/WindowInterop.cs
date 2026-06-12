using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SwtorLogParser.Overlay.ImGui;

/// <summary>
/// CsWin32-generated Win32 interop (INT-01) over the GLFW overlay window's HWND. GLFW handles the
/// transparent framebuffer + always-on-top; this supplies the native behaviours GLFW does not: the
/// tool-window / no-activate styles so the overlay stays out of Alt-Tab and never steals focus from the
/// game (INT-03), and HWND_TOPMOST re-assert on foreground changes so it stays over a
/// borderless/windowed game (INT-02 / BL-01).
/// </summary>
public sealed class WindowInterop : IDisposable
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    private static readonly HWND HWND_TOPMOST = new(-1);

    private readonly HWND _hwnd;
    private WINEVENTPROC? _foregroundProc;       // kept alive — unmanaged hook holds a raw pointer
    private UnhookWinEventSafeHandle? _foregroundHook;
    private bool _disposed;

    public WindowInterop(nint hwnd) => _hwnd = new HWND(hwnd);

    /// <summary>
    /// No-activate style so the overlay never steals focus from the game (INT-03). NOTE: the tool-window
    /// style is deliberately NOT set — it would hide the overlay from the taskbar (and its icon). The
    /// trade-off is that the overlay now appears in the taskbar + Alt-Tab. Apply after the window is shown.
    /// </summary>
    public void ApplyOverlayStyles()
    {
        long ex = PInvoke.GetWindowLongPtr(_hwnd, (WINDOW_LONG_PTR_INDEX)GWL_EXSTYLE);
        ex |= WS_EX_NOACTIVATE;
        PInvoke.SetWindowLongPtr(_hwnd, (WINDOW_LONG_PTR_INDEX)GWL_EXSTYLE, (nint)ex);
    }

    /// <summary>Re-assert HWND_TOPMOST without moving/activating (INT-02 / BL-01).</summary>
    public void ReassertTopmost() =>
        PInvoke.SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    /// <summary>Re-assert topmost on every foreground change (a borderless game otherwise steals the band).</summary>
    public void StartForegroundTopmostHook()
    {
        _foregroundProc = OnForegroundChanged;
        _foregroundHook = PInvoke.SetWinEventHook(
            PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND,
            null, _foregroundProc, 0, 0, PInvoke.WINEVENT_OUTOFCONTEXT);
        ReassertTopmost();
    }

    private void OnForegroundChanged(
        HWINEVENTHOOK hook, uint @event, HWND hwnd, int idObject, int idChild, uint thread, uint time)
        => ReassertTopmost();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _foregroundHook?.Dispose();
        _foregroundProc = null;
    }
}
