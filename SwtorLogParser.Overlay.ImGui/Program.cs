using System.Globalization;
using System.Numerics;
using System.Threading;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using SwtorLogParser.Monitor;
using SwtorLogParser.View;
using Windows.Win32.Foundation;

namespace SwtorLogParser.Overlay.ImGui;

/// <summary>
/// Immediate-mode (Dear ImGui) game overlay. A transparent, borderless, always-on-top GLFW/OpenGL
/// window renders the live DPS/HPS table each frame from the frozen core stream. Replaces the WinForms
/// + WinUI overlays: GLFW's transparent framebuffer gives the clear per-pixel see-through WinUI 3 cannot.
/// CsWin32 (<see cref="WindowInterop"/>) supplies the click-through toggle, caption drag, tool-window /
/// no-activate styles, and the HWND_TOPMOST re-assert (BL-01) over a borderless/windowed game.
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
    private bool _clickThrough;
    private bool _chordWasDown;

    // SWTOR game-window tracking: poll until the game window appears, pin the overlay over it, follow it.
    private const double GamePollSeconds = 1.5d;
    private double _gamePollAccum = GamePollSeconds; // first frame polls immediately
    private bool _gameAcquired;
    private bool _gameFound;
    private RECT _lastGameRect;

    private Program()
    {
        _settings = _settingsService.Load();
        _fontScale = Math.Clamp(_settings.FontScale, 0.8f, 3.0f);
        _opacity = Math.Clamp(_settings.Opacity, 0.05d, 1.0d);
    }

    [STAThread]
    private static void Main() => new Program().Run();

    private void Run()
    {
        var options = WindowOptions.Default with
        {
            Title = "SWTOR Overlay",
            Size = new Vector2D<int>(460, 300),
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

        // Feed the frozen core stream OFF the render thread into the internally-locked sliding list.
        // Guard null Player.Id (Actor.Id is long?, null for malformed actors) — the core list
        // force-unwraps it, and an unguarded null would fault the subscription and kill the stream.
        _subscription = CombatLogsMonitor.Instance.DpsHps.Subscribe(stats =>
        {
            if (stats.Player?.Id is not null) _list.AddOrUpdate(stats);
        });
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
        // Global click-through toggle (Ctrl+Alt+O) — polled so it works while click-through/no-activate.
        var chord = WindowInterop.IsToggleChordDown();
        if (chord && !_chordWasDown) SetClickThrough(!_clickThrough);
        _chordWasDown = chord;

        // Find / follow the SWTOR window so the overlay pins itself over the game (polled, resilient).
        _gamePollAccum += delta;
        if (_gamePollAccum >= GamePollSeconds)
        {
            _gamePollAccum = 0d;
            PollGameWindow();
        }

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

        ImGuiNET.ImGui.End();

        ImGuiNET.ImGui.PopStyleVar();
        ImGuiNET.ImGui.PopStyleColor();
    }

    private void DrawControlBar()
    {
        // Drag grip — pressing it starts the OS caption move-loop (OVL-05).
        ImGuiNET.ImGui.Button("☰");          // ☰ grip
        if (ImGuiNET.ImGui.IsItemActivated()) _interop?.BeginDrag();

        ImGuiNET.ImGui.SameLine();
        if (ImGuiNET.ImGui.Button(" + ")) _fontScale = Math.Clamp(_fontScale + 0.1f, 0.8f, 3.0f);
        ImGuiNET.ImGui.SameLine();
        if (ImGuiNET.ImGui.Button(" - ")) _fontScale = Math.Clamp(_fontScale - 0.1f, 0.8f, 3.0f);

        ImGuiNET.ImGui.SameLine();
        var ct = _clickThrough;
        if (ImGuiNET.ImGui.Checkbox("Click-through", ref ct)) SetClickThrough(ct);

        ImGuiNET.ImGui.SameLine();
        ImGuiNET.ImGui.SetNextItemWidth(90f);
        var op = (float)_opacity;
        if (ImGuiNET.ImGui.SliderFloat("Opacity", ref op, 0.05f, 1.0f, "")) _opacity = op;

        if (!_gameFound)
            ImGuiNET.ImGui.TextColored(new Vector4(1f, 0.85f, 0.2f, 1f), "Waiting for SWTOR…");
    }

    private void DrawTable()
    {
        const ImGuiTableFlags tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp;
        if (!ImGuiNET.ImGui.BeginTable("stats", 5, tableFlags)) return;

        ImGuiNET.ImGui.TableSetupColumn("Player");
        ImGuiNET.ImGui.TableSetupColumn("DPS");
        ImGuiNET.ImGui.TableSetupColumn("Crit %##dcrit");
        ImGuiNET.ImGui.TableSetupColumn("HPS");
        ImGuiNET.ImGui.TableSetupColumn("Crit %##hcrit");
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

    private void SetClickThrough(bool enabled)
    {
        _clickThrough = enabled;
        _interop?.SetClickThrough(enabled);
    }

    /// <summary>
    /// Poll for the SWTOR window and keep the overlay pinned over it. On first acquisition the overlay
    /// snaps to the game window's top-right; thereafter it follows the game if it moves/resizes. If the
    /// game isn't running it keeps polling (and re-snaps when SWTOR (re)appears).
    /// </summary>
    private void PollGameWindow()
    {
        if (GameWindowTracker.TryGetGameRect(out var rect))
        {
            if (!_gameAcquired)
            {
                PositionOverGame(rect);
                _gameAcquired = true;
            }
            else if (!SameRect(rect, _lastGameRect))
            {
                // Game window moved/resized → shift the overlay by the same delta to follow it.
                var pos = _window.Position;
                _window.Position = new Vector2D<int>(
                    pos.X + (rect.left - _lastGameRect.left),
                    pos.Y + (rect.top - _lastGameRect.top));
            }

            _lastGameRect = rect;
            _gameFound = true;
            _interop?.ReassertTopmost(); // ensure we sit above the game window we just located
        }
        else
        {
            // SWTOR not running / window not ready → keep the overlay where it is and re-snap on return.
            _gameFound = false;
            _gameAcquired = false;
        }
    }

    private void PositionOverGame(RECT rect)
    {
        const int margin = 16;
        var x = rect.right - _window.Size.X - margin;
        var y = rect.top + margin;
        _window.Position = new Vector2D<int>(x, y);
    }

    private static bool SameRect(RECT a, RECT b) =>
        a.left == b.left && a.top == b.top && a.right == b.right && a.bottom == b.bottom;

    private void OnClosing()
    {
        _settings.WindowX = _window.Position.X;
        _settings.WindowY = _window.Position.Y;
        _settings.Opacity = _opacity;
        _settings.FontScale = _fontScale;
        _settingsService.Save(_settings);

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
