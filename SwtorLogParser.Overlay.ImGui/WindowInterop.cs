using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SwtorLogParser.Overlay.ImGui;

/// <summary>
/// CsWin32-generated Win32 interop (INT-01) over the GLFW overlay window's HWND. GLFW handles the
/// transparent framebuffer + always-on-top; this supplies the native behaviours GLFW does not: the
/// tool-window / no-activate styles so the overlay stays out of Alt-Tab and never steals focus from the
/// game (INT-03), a click-through toggle (OVL-06), caption-style drag (OVL-05), and HWND_TOPMOST
/// re-assert on foreground changes so it stays over a borderless/windowed game (INT-02 / BL-01).
/// </summary>
public sealed class WindowInterop : IDisposable
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x00000020;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const nuint HTCAPTION = 2;

    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_O = 0x4F;

    private static readonly HWND HWND_TOPMOST = new(-1);

    private readonly HWND _hwnd;
    private WINEVENTPROC? _foregroundProc;       // kept alive — unmanaged hook holds a raw pointer
    private UnhookWinEventSafeHandle? _foregroundHook;
    private bool _disposed;

    public WindowInterop(nint hwnd) => _hwnd = new HWND(hwnd);

    /// <summary>Tool-window + no-activate styles (INT-03). Apply after the window is shown.</summary>
    public void ApplyOverlayStyles() => SetExStyle(CurrentExStyle() | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

    /// <summary>Toggle whole-window click-through — mouse passes to the game (OVL-06).</summary>
    public void SetClickThrough(bool enabled)
    {
        var ex = CurrentExStyle();
        ex = enabled ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        SetExStyle(ex);
    }

    /// <summary>Start the OS caption move-loop from a drag-handle press (OVL-05).</summary>
    public void BeginDrag()
    {
        PInvoke.ReleaseCapture();
        PInvoke.SendMessage(_hwnd, WM_NCLBUTTONDOWN, new WPARAM(HTCAPTION), default);
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

    /// <summary>True while Ctrl+Alt+O is held (global toggle for click-through; polled, focus-independent).</summary>
    public static bool IsToggleChordDown() => IsDown(VK_CONTROL) && IsDown(VK_MENU) && IsDown(VK_O);

    private static bool IsDown(int vk) => (PInvoke.GetAsyncKeyState(vk) & 0x8000) != 0;

    private void OnForegroundChanged(
        HWINEVENTHOOK hook, uint @event, HWND hwnd, int idObject, int idChild, uint thread, uint time)
        => ReassertTopmost();

    private long CurrentExStyle() => PInvoke.GetWindowLongPtr(_hwnd, (WINDOW_LONG_PTR_INDEX)GWL_EXSTYLE);

    private void SetExStyle(long exStyle) =>
        PInvoke.SetWindowLongPtr(_hwnd, (WINDOW_LONG_PTR_INDEX)GWL_EXSTYLE, (nint)exStyle);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _foregroundHook?.Dispose();
        _foregroundProc = null;
    }
}
