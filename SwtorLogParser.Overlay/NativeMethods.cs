using System.Runtime.InteropServices;

namespace SwtorLogParser.Overlay;

/*
 * Investigate Windows.Sdk for .NET 8 once it's supported
 */
public static class NativeMethods
{
    public const int WM_NCL_BUTTON_DOWN = 0x00A1;
    public const int HT_CAPTION = 0x0002;

    [DllImport("user32.dll")]
    public static extern int SendMessage(IntPtr hWnd, int wMsg, int wParam, int lParam);

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();
}