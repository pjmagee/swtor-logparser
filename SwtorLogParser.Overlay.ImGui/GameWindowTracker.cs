using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace SwtorLogParser.Overlay.ImGui;

/// <summary>
/// Locates the SWTOR game window so the overlay can pin itself over it. The game runs as
/// <c>swtor.exe</c>; we read its main window's screen rectangle. Resilient by design: returns
/// <c>false</c> when SWTOR isn't running yet or its window isn't ready, so the caller can keep polling
/// until it appears (and re-acquire if the game is closed and relaunched). Works for windowed/borderless
/// SWTOR; exclusive fullscreen cannot be overlaid by any normal window.
/// </summary>
internal static class GameWindowTracker
{
    // SWTOR's client process name (no extension). Case-insensitive match.
    private static readonly string[] ProcessNames = ["swtor"];

    /// <summary>
    /// True if the SWTOR window was found, with its screen rectangle in <paramref name="rect"/>.
    /// Never throws — process/window races (exited process, null window handle) resolve to <c>false</c>.
    /// </summary>
    internal static bool TryGetGameRect(out RECT rect)
    {
        rect = default;

        foreach (var name in ProcessNames)
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName(name); }
            catch { continue; }

            foreach (var process in processes)
            {
                try
                {
                    var handle = process.MainWindowHandle;
                    if (handle == IntPtr.Zero) continue;

                    var hwnd = new HWND(handle);
                    if (!PInvoke.IsWindow(hwnd)) continue;
                    if (!PInvoke.GetWindowRect(hwnd, out var r)) continue;
                    if (r.right - r.left <= 0 || r.bottom - r.top <= 0) continue;

                    rect = r;
                    return true;
                }
                catch
                {
                    // process exited mid-inspection, or window handle became invalid — keep looking.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return false;
    }
}
