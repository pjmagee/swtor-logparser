using System.Globalization;
using System.Numerics;
using System.Threading;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using SwtorLogParser.Model;
using SwtorLogParser.Monitor;
using SwtorLogParser.View;
using Windows.Win32;

namespace SwtorLogParser.Overlay.ImGui;

/// <summary>
/// Immediate-mode (Dear ImGui) game overlay. A transparent, borderless, always-on-top GLFW/OpenGL
/// window renders the live DPS/HPS table (and an optional human-readable combat log) each frame from the
/// frozen core stream. Replaces the WinForms + WinUI overlays: GLFW's transparent framebuffer gives the
/// clear per-pixel see-through WinUI 3 cannot. CsWin32 (<see cref="WindowInterop"/>) supplies the
/// no-activate style (never steals game focus) and the HWND_TOPMOST re-assert (BL-01). The user
/// positions it by dragging — no game-window detection.
/// </summary>
internal sealed class Program
{
    private readonly SettingsService _settingsService = new();
    private readonly OverlaySettings _settings;
    private readonly SlidingExpirationList _list = new(TimeSpan.FromSeconds(10));

    private IWindow _window = null!;
    private GL _gl = null!;
    private IInputContext _input = null!;
    private ImGuiController _controller = null!;
    private WindowInterop? _interop;
    private IDisposable? _subscription;

    private float _fontScale;
    private double _opacity;
    private bool _showLog;

    // Mini human-readable combat log (toggled): a bounded ring of formatted ability events, fed from the
    // core CombatLogChanged event on the reader thread and rendered on the UI thread. Stripped of GUIDs
    // and the raw log syntax — useful for streamers sharing their rotation.
    private const int LogCapacity = 250;
    private readonly object _logLock = new();
    private readonly Queue<string> _log = new();
    private static readonly Vector2D<int> MeterSize = new(600, 320);
    private static readonly Vector2D<int> MeterWithLogSize = new(680, 700);

    // Manual window drag (the OS caption-move loop doesn't work on a no-activate window).
    private bool _dragging;
    private int _dragCursorX, _dragCursorY, _dragWinX, _dragWinY;

    private Program()
    {
        _settings = _settingsService.Load();
        _fontScale = Math.Clamp(_settings.FontScale, 0.8f, 3.0f);
        _opacity = Math.Clamp(_settings.Opacity, 0.05d, 1.0d);
        _showLog = _settings.ShowLog;
    }

    [STAThread]
    private static void Main() => new Program().Run();

    private void Run()
    {
        // Single-file (and trimmed/AOT) deployments can't auto-discover the Silk.NET windowing/input
        // backends by reflection, which fails with "Couldn't find a suitable window platform". Register
        // the GLFW platform explicitly so the published single-file .exe runs.
        Silk.NET.Windowing.Glfw.GlfwWindowing.RegisterPlatform();
        Silk.NET.Input.Glfw.GlfwInput.RegisterPlatform();

        var options = WindowOptions.Default with
        {
            Title = "SWTOR Overlay",
            Size = _showLog ? MeterWithLogSize : MeterSize,
            Position = _settings is { WindowX: { } x, WindowY: { } y } ? new Vector2D<int>(x, y) : new Vector2D<int>(40, 40),
            WindowBorder = WindowBorder.Hidden,
            TransparentFramebuffer = true,
            TopMost = true,
            VSync = true,
        };

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.Closing += OnClosing;
        _window.Run();
    }

    private void OnLoad()
    {
        _gl = _window.CreateOpenGL();
        _input = _window.CreateInput();
        _controller = new ImGuiController(_gl, _window, _input);

        // Don't write an imgui.ini layout file next to the exe — our window state is persisted in our own
        // settings, and the overlay window already uses NoSavedSettings.
        unsafe { ImGuiNET.ImGui.GetIO().NativePtr->IniFilename = null; }

        // Keep the GL viewport in lock-step with the window size. Without this, resizing the window
        // (e.g. toggling the combat log) squishes the render into the old viewport — skewed, shrunk, and
        // with the mouse hit-test offset from where things are drawn.
        _window.FramebufferResize += size => _gl.Viewport(size);

        // Feed the frozen core stream OFF the render thread into the internally-locked sliding list.
        // Guard null Player.Id (Actor.Id is long?, null for malformed actors) — the core list
        // force-unwraps it, and an unguarded null would fault the subscription and kill the stream.
        _subscription = CombatLogsMonitor.Instance.DpsHps.Subscribe(stats =>
        {
            if (stats.Player?.Id is not null) _list.AddOrUpdate(stats);
        });
        // Per-line feed for the mini combat log (fires on the reader thread; we just format + enqueue).
        CombatLogsMonitor.Instance.CombatLogChanged += OnCombatLine;

        if (!CombatLogsMonitor.Instance.IsRunning)
            CombatLogsMonitor.Instance.Start(CancellationToken.None);

        // CsWin32 interop on the GLFW HWND: tool-window/no-activate (INT-03) + topmost re-assert (BL-01).
        var hwnd = _window.Native!.Win32!.Value.Hwnd;
        _interop = new WindowInterop(hwnd);
        _interop.ApplyOverlayStyles();
        _interop.StartForegroundTopmostHook();
    }

    private void OnRender(double delta)
    {
        ImGuiNET.ImGui.GetIO().FontGlobalScale = _fontScale;

        _controller.Update((float)delta);

        _gl.ClearColor(0f, 0f, 0f, 0f);  // transparent framebuffer → clear see-through
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);

        DrawOverlay();

        _controller.Render();
    }

    private void DrawOverlay()
    {
        // Fill the (fixed-size, OS-draggable) window with one panel — avoids the ImGui-clamps-to-viewport
        // feedback loop that an auto-resizing window hits. The panel's translucent bg over the CLEAR
        // transparent framebuffer shows the game through it; opacity controls how much.
        var io = ImGuiNET.ImGui.GetIO();
        ImGuiNET.ImGui.SetNextWindowPos(Vector2.Zero);
        ImGuiNET.ImGui.SetNextWindowSize(io.DisplaySize);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.06f, (float)_opacity));
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f);

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoBringToFrontOnFocus;

        ImGuiNET.ImGui.Begin("##swtor-overlay", flags);

        DrawControlBar();
        DrawTable();
        if (_showLog) DrawLog();

        ImGuiNET.ImGui.End();

        ImGuiNET.ImGui.PopStyleVar();
        ImGuiNET.ImGui.PopStyleColor();
    }

    private void DrawControlBar()
    {
        // Drag grip — manual drag (OVL-05): track the global cursor and move the window by the delta.
        // (The OS caption move-loop is unreliable on a WS_EX_NOACTIVATE window.) ASCII label — the
        // default ImGui font has no glyph icons (they render as '?'); a Hand cursor signals "draggable".
        ImGuiNET.ImGui.Button("Move");
        if (ImGuiNET.ImGui.IsItemHovered())
        {
            ImGuiNET.ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGuiNET.ImGui.SetTooltip("Drag to move the overlay");
        }
        if (ImGuiNET.ImGui.IsItemActive() && PInvoke.GetCursorPos(out var cursor))
        {
            if (!_dragging)
            {
                _dragging = true;
                _dragCursorX = cursor.X; _dragCursorY = cursor.Y;
                _dragWinX = _window.Position.X; _dragWinY = _window.Position.Y;
            }
            else
            {
                _window.Position = new Vector2D<int>(
                    _dragWinX + (cursor.X - _dragCursorX),
                    _dragWinY + (cursor.Y - _dragCursorY));
            }
        }
        else
        {
            _dragging = false;
        }

        ImGuiNET.ImGui.SameLine();
        if (ImGuiNET.ImGui.Button(" + ")) _fontScale = Math.Clamp(_fontScale + 0.1f, 0.8f, 3.0f);
        ImGuiNET.ImGui.SameLine();
        if (ImGuiNET.ImGui.Button(" - ")) _fontScale = Math.Clamp(_fontScale - 0.1f, 0.8f, 3.0f);

        ImGuiNET.ImGui.SameLine();
        ImGuiNET.ImGui.SetNextItemWidth(90f);
        var op = (float)_opacity;
        if (ImGuiNET.ImGui.SliderFloat("Opacity", ref op, 0.05f, 1.0f, "")) _opacity = op;

        ImGuiNET.ImGui.SameLine();
        var showLog = _showLog;
        if (ImGuiNET.ImGui.Checkbox("Log", ref showLog))
        {
            _showLog = showLog;
            ResizeForLog(); // grow/shrink the window to make room for the combat log
        }

        // Close button — the borderless window has no OS title-bar 'X', so provide one in-UI.
        ImGuiNET.ImGui.SameLine();
        if (ImGuiNET.ImGui.Button("X")) _window.Close();
        if (ImGuiNET.ImGui.IsItemHovered()) ImGuiNET.ImGui.SetTooltip("Close the overlay");
    }

    private void DrawTable()
    {
        const ImGuiTableFlags tableFlags =
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp;
        if (!ImGuiNET.ImGui.BeginTable("stats", 5, tableFlags)) return;

        // Player gets ~2.2x the width of each numeric column so DPS/HPS/Crit% have real room and don't
        // clip/merge; BordersInnerV separates them visually.
        ImGuiNET.ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthStretch, 2.2f);
        ImGuiNET.ImGui.TableSetupColumn("DPS", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGuiNET.ImGui.TableSetupColumn("Crit %##dcrit", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGuiNET.ImGui.TableSetupColumn("HPS", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGuiNET.ImGui.TableSetupColumn("Crit %##hcrit", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGuiNET.ImGui.TableHeadersRow();

        // DPS-descending; zero/null DPS sorts to the bottom (read the locked snapshot once per frame).
        foreach (var s in _list.Items.OrderByDescending(x => x.DPS ?? 0d))
        {
            ImGuiNET.ImGui.TableNextRow();
            ImGuiNET.ImGui.TableSetColumnIndex(0); ImGuiNET.ImGui.TextUnformatted(Name(s));
            ImGuiNET.ImGui.TableSetColumnIndex(1); ImGuiNET.ImGui.TextUnformatted(Rate(s.DPS));
            ImGuiNET.ImGui.TableSetColumnIndex(2); ImGuiNET.ImGui.TextUnformatted(Crit(s.DPSCritP));
            ImGuiNET.ImGui.TableSetColumnIndex(3); ImGuiNET.ImGui.TextUnformatted(Rate(s.HPS));
            ImGuiNET.ImGui.TableSetColumnIndex(4); ImGuiNET.ImGui.TextUnformatted(Crit(s.HPSCritP));
        }

        ImGuiNET.ImGui.EndTable();
    }

    /// <summary>
    /// Mini human-readable combat log (the streamer "rotation" view): a scrollable list of recent ability
    /// events — time, who, ability, target, amount + crit — with the raw GUIDs/syntax stripped. Renders a
    /// snapshot of the bounded buffer and auto-scrolls while pinned to the bottom.
    /// </summary>
    private void DrawLog()
    {
        ImGuiNET.ImGui.Separator();
        ImGuiNET.ImGui.TextDisabled("Combat log");

        if (ImGuiNET.ImGui.BeginChild("##combatlog"))
        {
            string[] lines;
            lock (_logLock) lines = _log.ToArray();

            foreach (var line in lines) ImGuiNET.ImGui.TextUnformatted(line);

            // Keep the newest line in view only when the user is already scrolled to the bottom.
            if (ImGuiNET.ImGui.GetScrollY() >= ImGuiNET.ImGui.GetScrollMaxY())
                ImGuiNET.ImGui.SetScrollHereY(1.0f);
        }

        ImGuiNET.ImGui.EndChild();
    }

    private void ResizeForLog() => _window.Size = _showLog ? MeterWithLogSize : MeterSize;

    // Reader-thread handler: format the line and push it into the bounded ring (oldest dropped).
    private void OnCombatLine(object? sender, CombatLogLine line)
    {
        var text = FormatLine(line);
        if (text is null) return;

        lock (_logLock)
        {
            _log.Enqueue(text);
            while (_log.Count > LogCapacity) _log.Dequeue();
        }
    }

    /// <summary>
    /// Projects a parsed line into a readable "rotation" entry, or null to skip it. Only ability events
    /// are kept; GUIDs and the raw bracketed syntax are dropped. Lazy <see cref="CombatLogLine"/> getters
    /// can throw on odd lines, so the whole projection is guarded.
    /// </summary>
    private static string? FormatLine(CombatLogLine line)
    {
        try
        {
            var ability = line.Ability?.Name;
            if (string.IsNullOrWhiteSpace(ability)) return null;

            var time = line.TimeStamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            var source = line.Source?.Name;
            source = string.IsNullOrWhiteSpace(source) ? "?" : source;
            var target = line.Target?.Name;

            var value = line.Value;
            var amount = value is { Total: > 0 } ? value.Total.ToString("N0", CultureInfo.InvariantCulture) : null;
            // ASCII only — ImGui's default font has no arrow/star glyphs (they render as '?').
            var crit = value?.IsCritical == true ? " (crit)" : string.Empty;

            var text = $"{time}  {source}  {ability}";
            if (!string.IsNullOrWhiteSpace(target) && target != source) text += $" -> {target}";
            if (amount is not null) text += $"  {amount}{crit}";
            return text;
        }
        catch
        {
            return null;
        }
    }

    private void OnClosing()
    {
        _settings.WindowX = _window.Position.X;
        _settings.WindowY = _window.Position.Y;
        _settings.Opacity = _opacity;
        _settings.FontScale = _fontScale;
        _settings.ShowLog = _showLog;
        _settingsService.Save(_settings);

        CombatLogsMonitor.Instance.CombatLogChanged -= OnCombatLine;
        _subscription?.Dispose();
        _interop?.Dispose();
        _controller?.Dispose();
        _input?.Dispose();
        _gl?.Dispose();
    }

    // ---- formatting (parity with the WinForms/WinUI grid: rounded rates, crit% string, blank for null) ----

    private static string Name(CombatLogsMonitor.PlayerStats s)
    {
        var name = s.Player?.Name;
        return string.IsNullOrWhiteSpace(name) ? "?" : name;
    }

    private static string Rate(double? value) =>
        value.HasValue ? value.Value.ToString("F0", CultureInfo.InvariantCulture) : string.Empty;

    private static string Crit(double? value) =>
        value.HasValue ? value.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%" : string.Empty;
}
