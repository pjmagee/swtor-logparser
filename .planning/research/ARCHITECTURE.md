# Architecture Research

**Domain:** WinUI 3 / Windows App SDK overlay host integrating with an existing Rx (`IObservable<PlayerStats>`) stream + Win32 interop (brownfield, .NET 10, Windows-only)
**Researched:** 2026-06-12
**Confidence:** HIGH (existing code read directly; WinUI 3 / CsWin32 / DispatcherQueue patterns verified against Microsoft Learn + dotnet/reactive)

## TL;DR for the roadmapper

- The core (`SwtorLogParser`) and the `DpsHps` stream are **frozen**. The WinUI 3 host is a *new pure consumer*, exactly like the WinForms one. The single integration seam is unchanged: `CombatLogsMonitor.Instance.DpsHps.Subscribe(...)`.
- **Three things change in the host layer**, nothing else: (1) thread marshaling moves from `Control.Invoke` → `DispatcherQueue.TryEnqueue` / Rx `ObserveOn(DispatcherQueueSynchronizationContext)`; (2) the render surface moves from `DataGridView` + `BindingList<Entry>` → XAML `ListView`/`ItemsRepeater` bound (via `x:Bind`) to an `ObservableCollection<EntryViewModel>`; (3) Win32 interop moves from hand-written `NativeMethods` P/Invoke → CsWin32 source-generated `PInvoke.*` operating on the HWND obtained from the WinUI `Window`.
- **`SwtorLogParser.View.SlidingExpirationList` (core) is reused as-is.** It is UI-free (a `SortedList` + `Timer`, no WinForms types). The WinForms-specific adapter `SwtorLogParser.Overlay/View/SlidingExpirationList.cs` (the `BindingList<Entry>` one) is **deleted**, not ported.
- **BL-01 (stay-topmost over borderless SWTOR) still needs explicit `SetWindowPos(HWND_TOPMOST)` re-assertion via CsWin32.** WinUI's high-level `OverlappedPresenter.IsAlwaysOnTop` exists but has documented Z-order reliability gaps against fullscreen-borderless games — so the proven Win32 re-assert pattern carries forward, now source-generated.
- **Build order is parity-then-delete:** scaffold the WinUI 3 project → wire the stream + dispatcher → port render → CsWin32 interop (transparency / click-through / drag / topmost) → reach parity → *only then* delete `SwtorLogParser.Overlay` (WinForms). MSTest migration and VSCode config are independent workstreams that can run in parallel.

## Standard Architecture

### System Overview

```
┌──────────────────────────────────────────────────────────────────┐
│                       CORE LIBRARY (FROZEN)                        │
│                 SwtorLogParser  (net10.0, AOT-clean)              │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │ CombatLogsMonitor.Instance                                 │  │
│  │   ReadAsync/MonitorAsync (background Tasks)                 │  │
│  │   Subject<CombatLogLine> → Rx pipeline →                   │  │
│  │   IObservable<PlayerStats>  DpsHps   ◄── THE ONLY SEAM     │  │
│  └────────────────────────────────────────────────────────────┘  │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │ SwtorLogParser.View (UI-free, REUSED)                      │  │
│  │   SlidingExpirationList (SortedList<long,Entry> + Timer)   │  │
│  │   Entry { PlayerStats Stats; DateTime Expiration }         │  │
│  └────────────────────────────────────────────────────────────┘  │
└───────────────────────────────┬──────────────────────────────────┘
        DpsHps.Subscribe(...)    │   (background thread OnNext)
   ┌─────────────────────────────┼─────────────────────────────┐
   ▼                             ▼                             ▼
┌──────────┐            ┌──────────────┐         ┌──────────────────────────┐
│ Cli      │            │ Native.Cli   │         │ NEW: WinUI 3 Overlay     │
│ (Spectre)│            │ (AOT console)│         │ SwtorLogParser.Overlay   │
└──────────┘            └──────────────┘         │ (net10.0-windows10.x)    │
                                                 │ ┌──────────────────────┐ │
   background thread  ──ObserveOn──►  UI thread  │ │ MainViewModel        │ │
                                                 │ │  ObservableCollection│ │
                                                 │ │  <EntryViewModel>    │ │
                                                 │ └─────────┬────────────┘ │
                                                 │   x:Bind  ▼              │
                                                 │ ┌──────────────────────┐ │
                                                 │ │ MainWindow.xaml       │ │
                                                 │ │  ListView / Repeater  │ │
                                                 │ └─────────┬────────────┘ │
                                                 │  HWND     ▼              │
                                                 │ ┌──────────────────────┐ │
                                                 │ │ WindowInterop (CsWin32)│ │
                                                 │ │  layered/transparent  │ │
                                                 │ │  click-through, drag, │ │
                                                 │ │  HWND_TOPMOST reassert│ │
                                                 │ └──────────────────────┘ │
                                                 └──────────────────────────┘
```

### Component Responsibilities

| Component | Responsibility | New / Modified / Reused |
|-----------|----------------|-------------------------|
| `CombatLogsMonitor.Instance.DpsHps` | Producer of `IObservable<PlayerStats>` | **Reused, frozen** — no change |
| `SwtorLogParser.View.SlidingExpirationList` (core) | UI-free 10s sliding-window aggregation by player Id | **Reused as-is** (already AOT/UI-free) |
| `SwtorLogParser.View.Entry` (core) | `{ PlayerStats Stats; Expiration }` holder | **Reused** |
| `MainViewModel` | Owns `ObservableCollection<EntryViewModel>`; subscribes to `DpsHps` on the UI dispatcher; mirrors `SlidingExpirationList.Items` into the collection on a render tick | **NEW** |
| `EntryViewModel` | Display projection (`Name`, `DPS`, `DCrit`, `HPS`, `HCrit` strings) + `INotifyPropertyChanged` | **NEW** (replaces Overlay `View/Entry.cs`) |
| `MainWindow.xaml` / `.xaml.cs` | XAML render surface (`ListView`/`ItemsRepeater` + buttons), `x:Bind` to VM | **NEW** (replaces `ParserForm`) |
| `WindowInterop` (helper) | HWND acquisition; layered/transparent style; click-through; drag-to-move; `HWND_TOPMOST` re-assert (BL-01) | **NEW** (replaces `NativeMethods` + `ParserForm` window setup) |
| `NativeMethods.txt` (CsWin32 input) | Declares the Win32 surface to source-generate | **NEW** |
| `App.xaml` / `App.xaml.cs` (+ optional `Program.cs`) | WinUI application bootstrap; creates `MainWindow`, starts monitor | **NEW** (replaces WinForms `Program.cs`) |
| `SwtorLogParser.Overlay` (WinForms) — `ParserForm`, `NativeMethods`, `View/*` | The whole WinForms host | **DELETED after parity** |

## Recommended Project Structure

Decision: **replace, don't rename.** Keep the *name* `SwtorLogParser.Overlay` for the new WinUI 3 project (so the solution/host story and CI references stay stable) but treat the existing directory contents as throwaway. Concretely: create the new WinUI 3 project (new csproj + XAML files), delete the WinForms `.cs` files, and swap the csproj. The project *identity* (`SwtorLogParser.Overlay.csproj`, namespace `SwtorLogParser.Overlay`) is preserved; the *implementation* is wholly new. A rename to e.g. `SwtorLogParser.Overlay.WinUI` is also defensible but churns the solution, CI workflow, and docs for no functional gain — prefer same-name replace.

```
SwtorLogParser.Overlay/                 # same project identity, WinUI 3 internals
├── SwtorLogParser.Overlay.csproj       # MODIFIED: WinUI 3 / Windows App SDK SDK-style
├── app.manifest                         # NEW: DPI awareness, longPathAware
├── Package.appxmanifest                 # optional (only if packaged variant chosen)
├── NativeMethods.txt                    # NEW: CsWin32 generator input
├── App.xaml / App.xaml.cs               # NEW: Microsoft.UI.Xaml.Application bootstrap
├── Program.cs                           # NEW (optional, unpackaged custom Main)
├── MainWindow.xaml / .xaml.cs           # NEW: render surface (was ParserForm)
├── ViewModels/
│   ├── MainViewModel.cs                 # NEW: ObservableCollection + DpsHps subscription
│   └── EntryViewModel.cs               # NEW: display projection (was Overlay/View/Entry.cs)
└── Interop/
    └── WindowInterop.cs                 # NEW: HWND + CsWin32 transparency/clickthru/drag/topmost
                                         #      (was NativeMethods.cs + ParserForm window setup)

# DELETED at parity:
#   SwtorLogParser.Overlay/ParserForm.cs
#   SwtorLogParser.Overlay/NativeMethods.cs
#   SwtorLogParser.Overlay/View/Entry.cs
#   SwtorLogParser.Overlay/View/SlidingExpirationList.cs   (the BindingList adapter)
#   SwtorLogParser.Overlay/Program.cs                      (WinForms Application.Run)
```

### csproj shape (MODIFIED)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>   <!-- WinUI requires a 10.0.x TFM -->
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWinUI>true</UseWinUI>                                        <!-- replaces UseWindowsForms -->
    <RootNamespace>SwtorLogParser.Overlay</RootNamespace>
    <WindowsPackageType>None</WindowsPackageType>                    <!-- unpackaged (see §Startup) -->
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>    <!-- no separate runtime install -->
    <EnableMsixTooling>true</EnableMsixTooling>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" />          <!-- version pinned in Directory.Packages.props -->
    <PackageReference Include="Microsoft.Windows.CsWin32">          <!-- source generator, dev-only -->
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\SwtorLogParser\SwtorLogParser.csproj" />
  </ItemGroup>
</Project>
```

**Central package management:** add `Microsoft.WindowsAppSDK` and `Microsoft.Windows.CsWin32` as `<PackageVersion>` entries in the existing root `Directory.Packages.props` (which already governs `System.Reactive`, the test SDK, etc.). The Overlay csproj references them version-less, matching the repo's established CPM pattern. CsWin32 is dev-only (`<PrivateAssets>all</PrivateAssets>`) so it doesn't flow transitively.

### Structure Rationale

- **ViewModels/ split** mirrors standard WinUI MVVM and keeps the XAML code-behind thin; the VM is where the Rx→dispatcher seam lives so it's the one testable, isolated place.
- **Interop/ folder** isolates the only platform-native surface, exactly as `NativeMethods.cs` did — but the body is now CsWin32-generated, so the hand-written file is just orchestration over `PInvoke.*`.
- **Same project name** keeps the solution graph, CI workflow job, and `.planning` references stable across the WinForms→WinUI swap.

## Architectural Patterns

### Pattern 1: Rx → WinUI dispatcher marshaling (the core integration)

**What:** `DpsHps` fires `OnNext` on a background thread (`ReadAsync`'s `Task`). WinUI XAML elements and any `ObservableCollection` bound via `x:Bind` are STA-bound to the UI thread's `DispatcherQueue`. There is no `Control.Invoke` in WinUI 3 — you marshal with the UI thread's `DispatcherQueue.TryEnqueue(...)`, or, more cleanly, install a `DispatcherQueueSynchronizationContext` and let Rx's `ObserveOn` do it.

**When to use:** every cross-thread UI update. Mandatory here.

**Trade-offs:** `ObserveOn(SynchronizationContext)` is the cleanest (one operator, declarative) but requires the sync context to be current on the subscribing thread. `DispatcherQueue.TryEnqueue` is explicit and always available but pushes marshaling into the `OnNext` body.

**Recommended (Rx ObserveOn via the WinUI sync context):**

```csharp
// MainViewModel — constructed on the UI thread (App startup / MainWindow ctor)
private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();
private readonly SlidingExpirationList _core = new(TimeSpan.FromSeconds(10)); // CORE class, reused
public ObservableCollection<EntryViewModel> Rows { get; } = new();

public MainViewModel(CombatLogsMonitor monitor)
{
    // Ensure SynchronizationContext.Current is the WinUI one on this (UI) thread so ObserveOn works.
    if (SynchronizationContext.Current is null)
        SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(_ui));
    var uiContext = SynchronizationContext.Current!;

    // Aggregation stays off-thread in the core list; only the render mirror touches the collection.
    _sub = monitor.DpsHps
        .Subscribe(stats => _core.AddOrUpdate(stats));   // background thread; core list is internally locked

    // Render tick on the UI thread mirrors core.Items → ObservableCollection (parity with WinForms 1s timer).
    _renderTimer = _ui.CreateTimer();                    // DispatcherQueueTimer — UI-thread callback
    _renderTimer.Interval = TimeSpan.FromSeconds(1);
    _renderTimer.Tick += (_, _) => SyncRows(_core.Items);
    _renderTimer.Start();
}
```

**Why this shape (and not "ObserveOn straight into the ObservableCollection"):** the existing design already separates *aggregation* (the thread-safe core `SlidingExpirationList`, locked internally) from *rendering* (a 1s timer that snapshots `Items` and rebuilds the displayed rows). Preserving that split keeps behavior identical to WinForms (BUG-free, validated) and avoids hammering `ObservableCollection` (which is not thread-safe and raises `CollectionChanged` per mutation) on every `OnNext`. The `DispatcherQueueTimer` is the WinUI analogue of the WinForms render `Timer` + `Control.Invoke(Refresh)`.

**Alternative if you want pure-Rx (no core list):** `monitor.DpsHps.ObserveOn(uiContext).Subscribe(stats => /* mutate ObservableCollection directly */)`. Viable, but you'd reimplement the sliding-expiration/dedupe-by-Id logic that the core list already provides — not recommended; reuse the core list.

**`SlidingExpirationList`/`Entry` reuse verdict:** **reuse the core `SwtorLogParser.View.SlidingExpirationList` and `Entry` directly** (they are UI-free). **Replace** the WinForms `BindingList<Entry>` adapter and the WinForms `Overlay/View/Entry.cs` display class with an MVVM `ObservableCollection<EntryViewModel>` + `x:Bind`. So: core list = data source of truth (reused); `ObservableCollection` = the XAML-facing render mirror (new).

### Pattern 2: HWND acquisition + CsWin32 interop seam

**What:** A WinUI 3 `Microsoft.UI.Xaml.Window` is backed by an `AppWindow` and an HWND. Native styling (layered/transparent, click-through, drag, topmost) is applied to that **HWND** via Win32 calls, now source-generated by CsWin32.

**HWND acquisition (the canonical chain):**

```csharp
using WinRT.Interop;                       // WindowNative
using Microsoft.UI;                         // Win32Interop
// ...
nint hwnd = WindowNative.GetWindowHandle(window);             // Window  -> HWND
WindowId id = Win32Interop.GetWindowIdFromWindow(hwnd);      // HWND     -> WindowId
AppWindow appWindow = AppWindow.GetFromWindowId(id);         // WindowId -> AppWindow (presenter, size, pos)
```

`AppWindow`/`OverlappedPresenter` cover the *managed* knobs (borderless via `SetBorderAndTitleBar(false, false)`, `IsResizable=false`, `IsMaximizable=false`, position/size). Everything WinUI doesn't expose — layered transparency, `WS_EX_TRANSPARENT` click-through, `WM_NCLBUTTONDOWN` drag, `HWND_TOPMOST` re-assert — drops to the raw HWND through CsWin32.

**CsWin32 plug-in point:** add `NativeMethods.txt` listing the exact Win32 surface; the generator emits a `Windows.Win32.PInvoke` static class + typed constants/structs at compile time. The hand-written `Interop/WindowInterop.cs` calls those generated members. This **replaces** `SwtorLogParser.Overlay/NativeMethods.cs` (the two `[DllImport]`s for `SendMessage`/`ReleaseCapture`) and closes concern #3.

```text
# NativeMethods.txt  (CsWin32 input — declares what to generate)
GetWindowLong
SetWindowLong
SetWindowPos
ReleaseCapture
SendMessage
WS_EX_LAYERED
WS_EX_TRANSPARENT
WS_EX_TOOLWINDOW
GWL_EXSTYLE
HWND_TOPMOST
SWP_NOSIZE
SWP_NOMOVE
SWP_NOACTIVATE
WM_NCLBUTTONDOWN
HTCAPTION
SetLayeredWindowAttributes      # if using LWA-based opacity
DwmExtendFrameIntoClientArea    # alt transparency path (see Pattern 3)
```

```csharp
// Interop/WindowInterop.cs — orchestration over generated PInvoke.*
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

internal static class WindowInterop
{
    public static void MakeClickThrough(HWND hwnd)
    {
        var ex = (uint)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE,
            (int)(ex | (uint)WINDOW_EX_STYLE.WS_EX_LAYERED
                      | (uint)WINDOW_EX_STYLE.WS_EX_TRANSPARENT       // <-- the click-through bit
                      | (uint)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW));     // hide from alt-tab
    }

    public static void ReassertTopmost(HWND hwnd) =>                 // BL-01
        PInvoke.SetWindowPos(hwnd, HWND.HWND_TOPMOST, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    public static void BeginDrag(HWND hwnd)                          // drag-to-move (was ParserForm.MouseDown)
    {
        PInvoke.ReleaseCapture();
        PInvoke.SendMessage(hwnd, 0x00A1 /*WM_NCLBUTTONDOWN*/, 0x0002 /*HTCAPTION*/, 0);
    }
}
```

**When to use:** all native window behavior the WinUI managed API can't express.
**Trade-offs:** CsWin32 gives type-safe, AOT-friendly, no-runtime-reflection P/Invoke (zero hand-written `[DllImport]`), at the cost of learning `NativeMethods.txt`. It is the Microsoft-recommended path for exactly this WinUI-3-needs-Win32 scenario.

### Pattern 3: Transparent / click-through overlay + BL-01 topmost (the hard part)

**What:** WinUI 3 renders content with hardware-accelerated DirectX composition, so the window has **no software back-buffer** the OS can sample for per-pixel alpha hit-testing. Consequence: you cannot get "clicks pass through transparent pixels but land on opaque rows" purely from WinUI. The pragmatic overlay design is:

1. **Whole-window click-through** via `WS_EX_TRANSPARENT` on the HWND (set in `WindowInterop.MakeClickThrough`). The overlay is informational (DPS/HPS readout); it does not need per-pixel input. This matches the WinForms overlay's *intent* (a passive topmost readout) while being honest about WinUI's compositor limitation.
2. **Drag handle exception:** because a fully click-through window can't be grabbed, expose drag either (a) via a hotkey/modifier that temporarily clears `WS_EX_TRANSPARENT`, or (b) a small non-transparent grip whose pointer events run `WindowInterop.BeginDrag`. Either reproduces the WinForms `MouseDown → ReleaseCapture + WM_NCLBUTTONDOWN` drag. (b) is closest to current UX.
3. **Transparency rendering:** set the WinUI window background to `Colors.Transparent` (or a `SystemBackdrop`), and configure the HWND as layered. Two viable transparency paths: `WS_EX_LAYERED` + `SetLayeredWindowAttributes` (color-key / alpha, closest to the WinForms `TransparencyKey`), or `DwmExtendFrameIntoClientArea` + blur region. Prefer the layered/alpha path for parity with the current 50%-opacity black look.
4. **BL-01 topmost over borderless SWTOR:** set `OverlappedPresenter.IsAlwaysOnTop = true` as the baseline, **but also** re-assert `SetWindowPos(HWND_TOPMOST, …, SWP_NOACTIVATE)` on a low-frequency tick / on the game's focus changes. WinUI's `IsAlwaysOnTop` and `AppWindow.MoveInZOrderAtTop()` have **documented Z-order reliability gaps** against fullscreen-borderless games — the same gap that motivated BL-01 in the first place. The explicit Win32 re-assert (already proven in the WinForms era) is the reliable fix and carries straight into the CsWin32 path.

**When to use:** this *is* the overlay. There is no simpler correct path in WinUI 3.
**Trade-offs:** whole-window click-through is a slight behavioral change from "interactive grid" toward "passive readout"; acceptable for an overlay and arguably better (you can't accidentally click it mid-fight). The font +/- buttons from WinForms become either a drag-grip context affordance or are dropped — flag this as a UX decision for the roadmap, not a blocker.

### Pattern 4: WinUI startup (unpackaged) + DispatcherQueue bootstrap

**What:** The host needs a `Microsoft.UI.Xaml.Application` (`App.xaml`/`App.xaml.cs`) that creates `MainWindow` in `OnLaunched`, and (for an **unpackaged** app) a `Program.Main` that bootstraps the dispatcher before `Application.Start`. The WinUI generated `Main` (`Application.Start(p => { new DispatcherQueueSynchronizationContext(...); new App(); })`) handles this; you only hand-roll `Program.cs` (with `DISABLE_XAML_GENERATED_MAIN`) if you need custom pre-init.

**Recommendation: unpackaged + self-contained.** This tool is a local dev/utility overlay with no Store/MSIX story; `WindowsPackageType=None` + `WindowsAppSDKSelfContained=true` lets it run straight from the build output with no runtime install — matching how the CLIs and the old WinForms overlay run today, and keeping the VSCode F5 / `tasks.json` story simple. (A packaged variant is possible later but adds MSIX identity/manifest overhead with no benefit for a local tool.)

```csharp
// App.xaml.cs
public partial class App : Application
{
    private Window? _window;
    public App() => InitializeComponent();
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();                 // sets up VM + interop in its ctor
        _window.Activate();
        if (!CombatLogsMonitor.Instance.IsRunning)  // parity with ParserForm.OnActivated
            CombatLogsMonitor.Instance.Start(CancellationToken.None);
    }
}
```

The `DispatcherQueueController`/`DispatcherQueueSynchronizationContext` is created by the WinUI bootstrap on the main thread; `MainViewModel` then reads it (Pattern 1). No manual `DispatcherQueueController.CreateOnCurrentThread()` is needed unless you spin a second UI thread (you don't).

## Data Flow

### Stream → render flow (changed segment only)

```
[disk log] → CombatLogsMonitor (background Tasks)        ── UNCHANGED ──
        → Subject<CombatLogLine> → Rx pipeline
        → IObservable<PlayerStats> DpsHps               ── THE SEAM (unchanged) ──
                │  .Subscribe(...)   (background thread OnNext)
                ▼
        SlidingExpirationList (core, locked)  AddOrUpdate ── REUSED ──
                │
   DispatcherQueueTimer.Tick (UI thread, 1s)             ── NEW marshaling ──
                ▼
        SyncRows(core.Items) → ObservableCollection<EntryViewModel>  ── NEW ──
                │  x:Bind
                ▼
        ListView / ItemsRepeater in MainWindow.xaml      ── NEW render surface ──
```

### Native-window flow (new)

```
MainWindow ctor → WindowNative.GetWindowHandle → HWND
        → AppWindow / OverlappedPresenter  (borderless, no-resize, IsAlwaysOnTop=true)
        → WindowInterop.MakeClickThrough (WS_EX_LAYERED|TRANSPARENT|TOOLWINDOW)
        → layered transparency (SetLayeredWindowAttributes)
grip pointer-press → WindowInterop.BeginDrag (ReleaseCapture + WM_NCLBUTTONDOWN)
topmost tick / focus-change → WindowInterop.ReassertTopmost (SetWindowPos HWND_TOPMOST)  ── BL-01 ──
```

### State management

`SlidingExpirationList` (core, internally locked) remains the single source of truth for "who is currently active and their latest stats." The `ObservableCollection<EntryViewModel>` is a *derived, UI-thread-only* projection refreshed on the dispatcher tick — never mutated from a background thread.

## Build Order (dependency-respecting)

Parity-before-deletion is the controlling constraint: **never leave the milestone in a state with no working overlay.** The WinForms overlay keeps building and running until WinUI 3 reaches parity.

```
Phase A — Scaffold (no behavior yet)
  A1. Add Microsoft.WindowsAppSDK + Microsoft.Windows.CsWin32 to Directory.Packages.props
  A2. Stand up the WinUI 3 project (new csproj alongside / replacing internals; App.xaml,
      MainWindow.xaml, unpackaged self-contained startup). Window opens, empty.
      └─ depends on: A1
  (WinForms overlay still present and building.)

Phase B — Stream + dispatcher marshaling   ◄ the integration crux
  B1. MainViewModel: subscribe DpsHps → core SlidingExpirationList; DispatcherQueueTimer
      → ObservableCollection mirror; x:Bind ListView. Live DPS/HPS rows render.
      └─ depends on: A2, core SlidingExpirationList (already exists, reused)

Phase C — Native window behavior via CsWin32
  C1. NativeMethods.txt + WindowInterop: HWND acquisition, borderless presenter.
  C2. Transparency (layered) + whole-window click-through (WS_EX_TRANSPARENT).
  C3. Drag-to-move (grip → WM_NCLBUTTONDOWN).
  C4. BL-01: HWND_TOPMOST re-assert (presenter IsAlwaysOnTop + SetWindowPos tick).
      └─ C1→C2→C3→C4; all depend on B1 (need a real window with content to style)

Phase D — Parity gate + WinForms deletion
  D1. Verify parity (transparent, click-through, drag, live render, topmost over borderless SWTOR).
  D2. DELETE SwtorLogParser.Overlay WinForms files (ParserForm, NativeMethods, View/*, old Program.cs).
      └─ depends on: D1 (HARD GATE — parity must hold first)

Independent / parallel workstreams (no dependency on A–D):
  • MSTest migration (xUnit → MSTest.Sdk) — test project only; see §MSTest.
  • VSCode launch.json (debug all hosts incl. WinUI overlay) + tasks.json (build/test/AOT-publish).
      └─ the overlay launch config can only target the WinUI host once A2 exists; the CLI/Native
         configs and the build/test/AOT tasks have no overlay dependency and can land first.
  • Docs refresh (README/docs) — should land LAST, after D2, so it describes the shipped state.
```

**Critical-path dependencies summarized:** A1 → A2 → B1 → C1 → C2/C3/C4 → **D1 (parity gate)** → D2 (delete WinForms). CsWin32 is introduced in C1 and is used *only* by the Overlay (the CLIs and core do not need it; the core stays reflection-free/AOT-clean and gains no new dependency). MSTest + VSCode + docs hang off the side.

## MSTest Migration Architecture (brief — overlay is the crux)

Structural, mechanical, and independent of the overlay work. Three moving parts:

1. **SDK / package swap** in `SwtorLogParser.Tests.csproj`. Either keep `Microsoft.NET.Sdk` and swap packages (`xunit`, `xunit.runner.visualstudio` → `MSTest.TestFramework` + `MSTest.TestAdapter`, keep `Microsoft.NET.Test.Sdk` + `coverlet.collector`), or adopt the newer `MSTest.Sdk` (`<Project Sdk="MSTest.Sdk">`) which bundles framework+adapter+runner. Update `Directory.Packages.props` accordingly (drop the two `xunit*` `PackageVersion`s, add `MSTest.*`). Recommended: `MSTest.Sdk` for the "modern single-SDK test project" the milestone calls for.
2. **`InternalsVisibleTo` target unchanged.** The core's `<InternalsVisibleTo Include="SwtorLogParser.Tests" />` stays — the assembly name doesn't change, so the existing test seams (`PublishForTest`, `internal Accumulator`, `internal CalculateDpsHpsStats`) keep working untouched. This is the key reason the migration is low-risk for the frozen core.
3. **Attribute mapping** (mechanical, per-file): `[Fact]`→`[TestMethod]`; `[Theory]`→`[TestMethod]` + `[DataRow(...)]` (xUnit `[InlineData]`→MSTest `[DataRow]`); xUnit constructor setup → `[TestInitialize]` (and `IDisposable.Dispose`→`[TestCleanup]`); `Assert.Equal(expected, actual)`→`Assert.AreEqual(expected, actual)` (note argument-order + method-name differences — the most error-prone part across 106 tests). No structural change to what's asserted; the DPS/HPS math seams are identical.

Risk: low and isolated to the test project; can land before, during, or after the overlay phases. Sequence it independently so it never blocks the parity gate.

## Anti-Patterns

### Anti-Pattern 1: Mutating the `ObservableCollection` (or any XAML-bound state) from the Rx background thread
**What people do:** `DpsHps.Subscribe(s => Rows.Add(...))` directly in `OnNext`.
**Why it's wrong:** `OnNext` runs on the monitor's background `Task`; `ObservableCollection` and `x:Bind` targets are UI-thread-affine. You get `COMException`/`RPC_E_WRONG_THREAD` or silent corruption.
**Do this instead:** marshal via `ObserveOn(DispatcherQueueSynchronizationContext)` or update only inside a `DispatcherQueue.TryEnqueue` / `DispatcherQueueTimer.Tick`. Keep aggregation in the locked core `SlidingExpirationList`; mirror to the collection on the UI tick (Pattern 1).

### Anti-Pattern 2: Expecting per-pixel alpha click-through from WinUI alone
**What people do:** set the window background transparent and assume clicks fall through the see-through areas.
**Why it's wrong:** WinUI's DirectX compositor has no back-buffer the OS can sample for alpha hit-testing; transparent pixels still capture input.
**Do this instead:** make the whole window click-through with `WS_EX_TRANSPARENT` on the HWND and provide an explicit drag affordance (Pattern 3). Don't chase per-pixel hit-testing.

### Anti-Pattern 3: Trusting `OverlappedPresenter.IsAlwaysOnTop` to win against a borderless fullscreen game
**What people do:** set `IsAlwaysOnTop = true` and call BL-01 done.
**Why it's wrong:** documented Z-order gaps; the game's focus/borderless transitions can steal top.
**Do this instead:** set `IsAlwaysOnTop` *and* re-assert `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)` on a tick/focus-change (BL-01), via CsWin32.

### Anti-Pattern 4: Porting the WinForms `BindingList<Entry>` adapter to WinUI
**What people do:** try to reuse `Overlay/View/SlidingExpirationList.cs` (the `BindingList`) by swapping the control.
**Why it's wrong:** `BindingList<T>`/`DataGridView` data-binding is WinForms-specific; WinUI binds via `ObservableCollection` + `INotifyPropertyChanged` + `x:Bind`.
**Do this instead:** delete the WinForms adapter; reuse only the **core** UI-free `SlidingExpirationList`; introduce `ObservableCollection<EntryViewModel>` as the WinUI render mirror.

### Anti-Pattern 5: Deleting the WinForms overlay before WinUI parity
**What people do:** swap the csproj and delete `ParserForm`/`NativeMethods` up front to "start clean."
**Why it's wrong:** leaves the milestone with no working overlay if WinUI work stalls; violates the project's stated "build-before-delete" decision.
**Do this instead:** keep WinForms building until the D1 parity gate passes, then delete in D2.

## Integration Points

### Internal Boundaries

| Boundary | Communication | Notes |
|----------|---------------|-------|
| Core ↔ WinUI host | `CombatLogsMonitor.Instance.DpsHps.Subscribe(...)` (`IObservable<PlayerStats>`) | **Only seam.** Identical to WinForms/CLI hosts. Core frozen. |
| Core list ↔ ViewModel | `SlidingExpirationList.AddOrUpdate(stats)` / `.Items` snapshot | Core list reused as-is (UI-free, internally locked). |
| ViewModel ↔ XAML | `ObservableCollection<EntryViewModel>` + `x:Bind` | New MVVM mirror; UI-thread-only. |
| Host ↔ Win32 | CsWin32-generated `Windows.Win32.PInvoke.*` over the HWND | Replaces hand-written `NativeMethods` (#3). HWND via `WindowNative.GetWindowHandle`. |
| Host ↔ Windows App SDK runtime | `Microsoft.WindowsAppSDK` (self-contained, unpackaged) | No runtime install; runs from build output like the CLIs. |
| Test project ↔ core | `InternalsVisibleTo("SwtorLogParser.Tests")` (unchanged) | MSTest swap keeps the assembly name, so internal test seams survive. |

### External Services

None — local tool, no network/DB/auth. Only native dependency is `user32.dll`/`dwmapi.dll` (now via CsWin32) and the Windows App SDK runtime (self-contained).

## Confidence & Gaps

- **HIGH** on: the integration seam (read directly), Rx→DispatcherQueue marshaling, HWND/AppWindow interop chain, CsWin32 `NativeMethods.txt` mechanism, build ordering, MSTest seam survival.
- **MEDIUM** on: the *exact* transparency recipe for parity with the current 50%-opacity black look (layered+`SetLayeredWindowAttributes` vs DWM blur) — both work; the precise visual match is a Phase-C tuning detail, not an architectural fork.
- **Gap / UX flag (not a blocker):** WinForms had font +/- buttons and an interactive grid; a click-through overlay changes that interaction model. The roadmap should make an explicit UX call (drag-grip + optional hotkey to toggle interactivity) rather than assume 1:1 control parity.
- **Pin versions at execution time:** `Microsoft.WindowsAppSDK` and `Microsoft.Windows.CsWin32` versions should be set in `Directory.Packages.props` against the current GA at build time (not pinned here) to match the repo's GA-only policy.

## Sources

- [DispatcherQueueUpdates spec — DispatcherQueueSynchronizationContext (microsoft-ui-xaml-specs)](https://github.com/microsoft/microsoft-ui-xaml-specs/blob/master/winui3/DispatcherQueueUpdates.md) — HIGH
- [dotnet/reactive #1651 — ObserveOn for WinUI 3 DispatcherQueue](https://github.com/dotnet/reactive/issues/1651) — HIGH
- [Mark Heath — Observing on the Dispatcher Thread with Rx](https://markheath.net/post/observing-on-the-dispatcher-thread-with-rx) — MEDIUM
- [Microsoft Q&A — (WinUI3) Semi Transparent Window + Click-through](https://learn.microsoft.com/en-us/answers/questions/1418063/(winui3)-semi-transparent-window-click-through-win) — HIGH
- [Build a C# .NET app with WinUI 3 and Win32 interop (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/desktop-winui3-app-with-basic-interop) — HIGH
- [Retrieve a window handle (HWND) — WindowNative.GetWindowHandle (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/apps/develop/ui/retrieve-hwnd) — HIGH
- [dotMorten — Using the C#/Win32 code generator to enhance your WinUI 3 app](https://www.sharpgis.net/post/Using-the-CWin32-code-generator-to-enhance-your-WinUI-3-app) — HIGH
- [OverlappedPresenter.IsAlwaysOnTop (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.overlappedpresenter.isalwaysontop) — HIGH
- [microsoft-ui-xaml #8562 — MoveInZOrderAtTop not working (topmost reliability)](https://github.com/microsoft/microsoft-ui-xaml/issues/8562) — MEDIUM
- [Nick's .NET Travels — Packaged, Unpackaged and Self-Contained WinUI 3 Apps](https://nicksnettravels.builttoroam.com/packaged-unpackaged-self-contained/) — HIGH
- [Distribute an unpackaged WinUI 3 app (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app) — HIGH
- [microsoft/microsoft-ui-xaml #2956 — transparent XAML island overlay (compositor limitation)](https://github.com/microsoft/microsoft-ui-xaml/issues/2956) — MEDIUM
- Existing codebase (read directly): `CombatLogsMonitor.cs`, `ParserForm.cs`, `NativeMethods.cs`, core + Overlay `View/*`, `*.csproj`, `Directory.Packages.props` — HIGH

---
*Architecture research for: WinUI 3 overlay host integration with a frozen Rx stream + Win32 interop*
*Researched: 2026-06-12*
