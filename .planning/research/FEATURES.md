# Feature Research

**Domain:** In-game combat-stats overlay (single-user Windows desktop tool) + .NET dev/test toolchain modernization
**Researched:** 2026-06-12
**Confidence:** HIGH (parity baseline read directly from shipped WinForms code; external facts verified against Microsoft Learn / framework docs)

> Scope note: v1.1 is a host re-implementation + tooling refresh, **not** new product capability. The parser and the live DPS/HPS stream are frozen. Every overlay feature below is bounded by one hard dependency: the overlay must consume `CombatLogsMonitor.Instance.DpsHps` (`IObservable<PlayerStats>`) **unchanged**, and start the monitor on activation — exactly as `ParserForm` does today (`ParserForm.cs:24,132`). The `View/` composition types (`Entry`, `SlidingExpirationList`) already live in the core library (`SwtorLogParser/View/`) after v1.0 RFCT-01, so the WinUI host re-binds to them rather than re-implementing them.

This document is organized by the five milestone categories: **Overlay**, **Interop**, **Testing**, **Dev Tooling**, **Docs**. Within each, items are tagged table-stakes / differentiator / anti-feature.

---

## Category 1 — Overlay (WinForms → WinUI 3 parity + BL-01 fix)

### Parity baseline (what the WinForms overlay actually does today)

Read directly from `ParserForm.cs` and `NativeMethods.cs`:

| Baseline behavior | Source | Carry to WinUI 3? |
|---|---|---|
| Transparent / semi-transparent window (`Opacity = 0.5`, `TransparencyKey` black, black `BackColor`) | `ParserForm.cs:32,37,38` | Yes — table stakes |
| Always-on-top (`TopMost = true`) | `ParserForm.cs:27` | Yes, **but currently broken over borderless** → BL-01 |
| Borderless / chromeless window (`FormBorderStyle.None`) | `ParserForm.cs:34` | Yes — table stakes |
| Draggable by clicking anywhere (drag via `ReleaseCapture` + `WM_NCLBUTTONDOWN`/`HT_CAPTION`) | `ParserForm.cs:140-147`, `NativeMethods.cs` | Yes — table stakes |
| Live per-player grid: Player / DPS / Crit% / HPS / Crit% columns | `ParserForm.cs:40-47` | Yes — table stakes |
| 10s sliding expiry of stale rows (`SlidingExpirationList`, `TimeSpan.FromSeconds(10)`) | `ParserForm.cs:115` | Yes — table stakes (reuses core `View/`) |
| Font size increase/decrease buttons (➕ / ➖) | `ParserForm.cs:82-92,118-128` | Yes — table stakes (legibility) |
| Starts monitor on window activation | `ParserForm.cs:130-133` | Yes — table stakes |
| DPI-aware autosize | `ParserForm.cs:31` | Yes — WinUI is DPI-aware by default (free) |
| **NOT click-through** | (no `WS_EX_TRANSPARENT` set today) | Decision point — see differentiators |
| **No position/size persistence** | (always `CenterScreen`) | Decision point — see differentiators |

> Important correction to the milestone brief: the **shipped WinForms overlay is draggable, semi-transparent, and topmost, but it is NOT click-through.** The window has no `WS_EX_TRANSPARENT` style and depends on receiving the `MouseDown` to drag itself (`ParserForm.cs:78,140`). "Click-through" is therefore a *new* capability for v1.1, not strict parity — and it directly conflicts with drag and the font buttons (see Feature Dependencies). Requirements should decide this explicitly.

### Table Stakes (must have for parity)

| Feature | Why Expected | Complexity | Notes |
|---|---|---|---|
| Transparent / low-opacity window | The point of an overlay is to see the game through it; matches `Opacity=0.5` today | MEDIUM | WinUI 3 transparency is non-trivial: no built-in "transparent window background." Achieved via `DesktopAcrylicController`/`SystemBackdrop` for a translucent backdrop, or layered-window interop for true per-pixel alpha. Plain "set background transparent" does not work like WinForms `TransparencyKey`. |
| Always-on-top | Core purpose; matches `TopMost=true` | LOW | First-class API: `AppWindow.Presenter as OverlappedPresenter` → `IsAlwaysOnTop = true`. Cleaner than WinForms. |
| Borderless / chromeless | No title bar over the game | LOW | `OverlappedPresenter.SetBorderAndTitleBar(false, false)`; can also `IsResizable=false`, `IsMaximizable=false`. |
| Draggable window | User positions the overlay; matches drag today | MEDIUM | WinUI has no `TopMost` mouse-drag built in. Reuse the same Win32 trick (`ReleaseCapture` + `SendMessage(hWnd, WM_NCLBUTTONDOWN, HTCAPTION, 0)`) via CsWin32, hooked from a pointer-pressed handler on a drag-handle/root element. |
| Live per-player rows (Player, DPS, Crit%, HPS, Crit%) | This IS the product; must render the `DpsHps` stream | MEDIUM | Bind a `ListView`/`ItemsRepeater`/`DataGrid` to the core `SlidingExpirationList`/`Entry`. **Marshal `OnNext` to the UI thread** via `DispatcherQueue.TryEnqueue` (Rx pushes from a background reader task). This is the #1 correctness risk of the port. |
| 10s sliding row expiry | Matches current behavior; stale players drop off | LOW | Reuse core `View/SlidingExpirationList` unchanged. The WinForms version drives a `DataGridView.DataSource`; WinUI needs an `ObservableCollection`-style adapter or to raise `INotifyCollectionChanged`. Small adapter only. |
| Font legibility controls (increase/decrease size) | Overlay text must stay readable over a busy game scene; matches ➕/➖ buttons | LOW | Bind font size to a property; +/- buttons or keyboard. |
| Topmost **over borderless/windowed SWTOR** (BL-01 fix) | The shipped overlay's stated bug — drops behind borderless game | MEDIUM | See Interop. `IsAlwaysOnTop` plus re-assert `SetWindowPos(HWND_TOPMOST, SWP_NOMOVE\|SWP_NOSIZE\|SWP_NOACTIVATE)` on a foreground-change hook (`SetWinEventHook` / `EVENT_SYSTEM_FOREGROUND`). |
| Start monitor on activation | Matches `OnActivated` → `monitor.Start` | LOW | WinUI `Window.Activated` event, guarded by `IsRunning` exactly as today. |
| Retire WinForms host after parity | Milestone goal: one overlay to maintain | LOW | Sequence: build WinUI to parity, validate live, then delete `SwtorLogParser.Overlay` (WinForms) and re-point the solution. Build-before-delete so there's never a window without a working overlay. |

### Differentiators (worth doing — improve on WinForms)

| Feature | Value Proposition | Complexity | Notes |
|---|---|---|---|
| **Click-through mode** (mouse passes to game) | A genuine overlay you never have to alt-tab around; biggest UX win over the current draggable-but-blocking window | MEDIUM | Set `WS_EX_LAYERED \| WS_EX_TRANSPARENT` on the HWND via CsWin32. **Conflicts with drag + buttons** — needs a toggle (e.g. a hotkey or a small always-interactive grab handle) to flip between "locked/click-through" and "edit/movable". Recommend: ship locked-and-movable by default, click-through as a toggle. |
| Position + size + opacity persistence | Survives restarts; today it always re-centers. Quality-of-life for a tool you launch every play session | LOW | Persist to a local JSON/settings file (no registry needed). Save on move/close, restore on launch via `AppWindow.Move/Resize`. No core dependency. |
| `WS_EX_NOACTIVATE` / `WS_EX_TOOLWINDOW` styling | Overlay never steals focus from the game and stays out of alt-tab | LOW | Pairs naturally with the BL-01 hook; recommended by the backlog item itself (BL-01 proposed fix). |
| Acrylic/Mica backdrop for legibility | Subtle dark translucent panel makes white stat text readable over bright scenes — better than flat 50% black | MEDIUM | `DesktopAcrylicController`. Tasteful default; ties into the transparency implementation anyway. |
| Global show/hide hotkey | Toggle overlay without touching the mouse mid-fight | MEDIUM | `RegisterHotKey` via CsWin32. Nice but optional. |

### Anti-Features (explicitly skip)

| Feature | Why Requested | Why Problematic | Alternative |
|---|---|---|---|
| Overlay over **exclusive fullscreen** SWTOR | "It should always be visible" | Architecturally impossible for a normal windowed overlay — an exclusive-fullscreen DirectX app owns the swap chain; only injected/DirectX-hook overlays (Steam/Discord-style) can draw there. Out of scope and a rabbit hole. | Document clearly: run SWTOR in **Fullscreen (Windowed)/Borderless**. BL-01 fix targets exactly that case. Matches BACKLOG.md BL-01 note. |
| Configurable columns / metric picker / themes / layouts | "Make it customizable" | New product capability — explicitly out of v1.1 scope per PROJECT.md; expands surface and risks touching the stream contract | Ship parity columns. Park in backlog as a future v1.2 idea. |
| New stats / metrics (per-ability breakdown, timelines, encounters) | "Add more data" | Out of scope — would change the core stream, which is frozen | None this milestone; core is untouched. |
| Full MVVM framework / DI container for the overlay | "Do it properly" | Over-engineering a single transparent window with ~5 columns; adds ceremony with no payoff | Minimal code-behind or a thin VM; the overlay is a leaf consumer of one observable. |
| Multi-window / detachable panels | "Flexibility" | Scope creep; one window is the parity baseline | One overlay window. |
| Rewriting `SlidingExpirationList` / `Entry` for WinUI | "WinForms types feel wrong" | They're core `View/` types (post-RFCT-01) shared by design; rewriting risks regressions and re-duplication | Add a thin WinUI-side `ObservableCollection` adapter; keep core logic intact. |

---

## Category 2 — Interop (hand-written `NativeMethods` → CsWin32)

### Table Stakes

| Feature | Why Expected | Complexity | Notes |
|---|---|---|---|
| Replace `NativeMethods.cs` with CsWin32-generated P/Invoke | Milestone goal (issue #3); type-safe, source-generated, no hand-maintained `DllImport` | LOW | Add `Microsoft.Windows.CsWin32` PackageReference + a `NativeMethods.txt` listing the APIs. Methods surface on `Windows.Win32.PInvoke`. |
| Generate `SendMessage`, `ReleaseCapture` (drag) | Direct parity with current `NativeMethods` | LOW | Same two calls used today, now generated. CsWin32 emits supporting consts (`WM_NCLBUTTONDOWN`, `HTCAPTION`) — drop the hand-defined consts in `NativeMethods.cs:10-11`. |
| Generate `SetWindowPos` + topmost constants (BL-01) | Re-assert topmost over borderless game | LOW | `SetWindowPos(HWND_TOPMOST, SWP_NOMOVE\|SWP_NOSIZE\|SWP_NOACTIVATE)`; add `HWND_TOPMOST`, `SWP_*` via the `.txt`. |
| Generate `SetWinEventHook` + `EVENT_SYSTEM_FOREGROUND` (BL-01) | Re-assert topmost when the game takes foreground | MEDIUM | The reliable BL-01 mechanism: hook foreground changes, re-`SetWindowPos` topmost. Must unhook on close. |

### Differentiators

| Feature | Value Proposition | Complexity | Notes |
|---|---|---|---|
| Generate `GetWindowLong`/`SetWindowLong` (ex-styles) for click-through + `NOACTIVATE`/`TOOLWINDOW` | Enables the differentiator overlay behaviors cleanly | LOW | Add to `NativeMethods.txt`; apply styles to the WinUI HWND obtained via `WindowNative.GetWindowHandle`. |
| Generate `RegisterHotKey`/`UnregisterHotKey` | Backing for show/hide hotkey differentiator | LOW | Only if hotkey ships. |

### Anti-Features

| Feature | Why Requested | Why Problematic | Alternative |
|---|---|---|---|
| Move interop into the core library | "Centralize native code" | Core is `IsAotCompatible=true` and intentionally Windows-path-light; interop is overlay-only. Keeping it in the overlay preserves the AOT/clean-core boundary | Keep CsWin32 + `NativeMethods.txt` in the overlay project only. |
| Hand-maintain any remaining `DllImport` | "It's just two methods" | Defeats the purpose of issue #3; leaves a split interop story | Generate everything; delete `NativeMethods.cs`. |

---

## Category 3 — Testing (xUnit → MSTest .NET SDK)

### Migration surface (measured from the actual suite)

The 106-test suite uses, concretely:
- `[Fact]` + heavy `[Theory]`/`[InlineData]` (data-driven parsing tests across `CombatLogLineTests`, `ActorTests`, `ValueTests`, `GameObjectTests`, `ThreatTests`, etc.).
- `[CollectionDefinition]` + `[Collection]` to **serialize global-state tests** that swap `CombatLogs.SetSource/ResetSource` (`Fixtures/CombatLogsSourceCollection.cs`) — this exists specifically because xUnit parallelizes classes by default and the monitor uses process-global state. **This is the single most important migration detail.**
- `InternalsVisibleTo("SwtorLogParser.Tests")` on the core (`SwtorLogParser.csproj:11`) — tests reach internal members.
- `global using Xunit;` (`GlobalUsings.cs`).
- xUnit `Assert.*` throughout.

### Table Stakes (must have for parity)

| Feature | Why Expected | Complexity | Notes |
|---|---|---|---|
| All 106 tests pass, same coverage | No regressions; CI must stay green | MEDIUM | Mechanical but broad — every test file touched. |
| `[Fact]` → `[TestMethod]` | Core test attribute mapping | LOW | 1:1. |
| `[Theory]/[InlineData]` → `[TestMethod]`+`[DataRow]` (or `[DynamicData]`) | All the data-driven parser tests | MEDIUM | The bulk of the churn — many `InlineData` rows. `[DataRow]` is inline; `[DynamicData]` for method/property-sourced data. MSTest no longer needs `[DataTestMethod]` (modern MSTest accepts `[TestMethod]` + `[DataRow]`). |
| Serialized global-state tests preserved | The `CombatLogs` source-seam race the collection guards is **real** and predates the test infra | MEDIUM | **Key gotcha:** MSTest runs assembly **sequentially by default** (opposite of xUnit). So the explicit `[Collection]` serialization is satisfied for free *if* parallelism is left off. If `[assembly: Parallelize(Scope = MethodLevel)]` is added for speed, the source-swapping classes must be marked `[DoNotParallelize]` to preserve determinism. Decide and document this. |
| `InternalsVisibleTo` still works | Tests access internals | LOW | Framework-agnostic — unchanged; keep the attribute as-is. |
| `Assert.*` mapping | All assertions | MEDIUM | xUnit `Assert.Equal(a,b)` → MSTest `Assert.AreEqual(a,b)` (note **arg order flips**: xUnit is (expected, actual) and so is MSTest, but method names differ: `True/False/Null/NotNull/IsType/Throws` → `IsTrue/IsFalse/IsNull/IsNotNull/IsInstanceOfType/ThrowsException`). Mechanical, error-prone; do carefully. |
| `[TestInitialize]`/`[TestCleanup]` for per-test setup | Replace xUnit constructor/`IDisposable` setup | LOW | xUnit's ctor-as-setup → `[TestInitialize]`; `Dispose` → `[TestCleanup]`. |
| MSTest.Sdk on Microsoft.Testing.Platform, CI runs it | Modern single-SDK test project (issue #2) | MEDIUM | `MSTest.Sdk` uses MTP by default; update the CI test step (`dotnet test`/`dotnet run`) and `Directory.Packages.props` (drop `xunit`, `xunit.runner.visualstudio`; add `MSTest`). Verify the GitHub Actions test step still discovers/reports. |

### Differentiators / capabilities gained

| Feature | Value Proposition | Complexity | Notes |
|---|---|---|---|
| MTP runner (no VSTest) | Faster, lighter, modern test host; aligns with .NET 10 testing direction | LOW | Default with `MSTest.Sdk`. Single SDK reference instead of Test.Sdk + runner + framework packages. |
| Explicit parallelism control | `[assembly: Parallelize(Scope=MethodLevel)]` can speed the suite; `[DoNotParallelize]` is precise | LOW | Net gain: clearer than xUnit's class-parallel default that forced the collection workaround. |

### Capabilities to watch (potential friction, not blockers)

| xUnit feature | MSTest equivalent | Note |
|---|---|---|
| Class-per-test isolation (new instance per test) | MSTest reuses one class instance per test by default | Could expose latent shared-field state in test classes; lean on `[TestInitialize]` to reset. |
| `[Collection]`/`ICollectionFixture` | `[DoNotParallelize]` + `[ClassInitialize]`/`[AssemblyInitialize]` | Different shape; the **intent** (serialize source-swapping classes) maps to disabling parallelism for those classes. |
| `ITestOutputHelper` | `TestContext.WriteLine` | Only if any test logs output (none detected in counts). |
| `Assert.Throws<T>` | `Assert.ThrowsException<T>` / `Assert.ThrowsExactly<T>` | Naming differs. |

### Anti-Features

| Feature | Why Requested | Why Problematic | Alternative |
|---|---|---|---|
| Rewrite tests / restructure suite during migration | "Clean up while we're in here" | Conflates a framework swap with behavior changes; makes regressions impossible to attribute | Pure mechanical 1:1 migration; refactors are a separate effort. |
| Keep `<UseVSTest>true</UseVSTest>` | "Familiar runner" | Opts out of the modern platform that is the point of issue #2 | Use MTP (the `MSTest.Sdk` default). |
| Add a third assertion library (FluentAssertions, Shouldly) | "Nicer asserts" | New dependency, more churn, out of scope | Stay on built-in `Assert`. |

---

## Category 4 — Dev Tooling (VSCode launch.json + tasks.json)

This is a multi-host solution: core lib, managed CLI (Spectre.Console), Native AOT CLI, WinUI 3 overlay, MSTest project. A contributor on VSCode expects to F5 each host and run common build/test flows.

### Table Stakes

| Feature | Why Expected | Complexity | Notes |
|---|---|---|---|
| `launch.json`: F5-debug each runnable host | Per-host debugging is the baseline VSCode .NET expectation | LOW | Configs for: managed CLI, Native AOT CLI (debug build, not the AOT-published exe), WinUI 3 overlay. Each is a `coreclr` launch with its own `program`/`cwd`, `preLaunchTask` = its build task. |
| `tasks.json`: build all | One command to build the solution | LOW | `dotnet build` on the `.slnx`; `$msCompile` problem matcher. |
| `tasks.json`: run tests | Run the MSTest suite | LOW | `dotnet test` (MTP). Make it the default test task. |
| `tasks.json`: per-host build tasks | `preLaunchTask` targets for each launch config | LOW | One build task per project; cheap. |
| `tasks.json`: AOT publish (Native CLI) | The CI does it; contributors should be able to repro locally | MEDIUM | `dotnet publish` with the AOT profile/RID. Long-running; mark as non-default. |

### Differentiators

| Feature | Value Proposition | Complexity | Notes |
|---|---|---|---|
| Recommended-extensions file (`.vscode/extensions.json`) | One-click "install C# Dev Kit / C#" for new contributors | LOW | Lists `ms-dotnettools.csharp` / `csdevkit`. |
| Compound/launch picker grouping | Clean F5 dropdown ("Overlay", "CLI", "Native CLI") | LOW | Just naming/ordering in `launch.json`. |
| Watch/run task for the overlay or CLI | Fast inner loop | LOW | `dotnet watch` task. Optional. |

### Anti-Features

| Feature | Why Requested | Why Problematic | Alternative |
|---|---|---|---|
| Debugging the **AOT-published** native exe via VSCode | "Debug what ships" | Native AOT debugging is awkward (no managed debugger); the source is identical to the JIT build | Debug the Native CLI as a normal `coreclr` build; keep AOT publish as a build/CI task only. |
| Committing machine-specific paths / user settings | "It works on my machine" | Breaks for other contributors; SWTOR log path is per-user | Use workspace-relative paths and OS special folders resolved at runtime (already how the app works). |
| Heavy task graph / custom scripts | "Automate everything" | Overkill for a 5-project solo tool; maintenance burden | Thin set: build-all, test, per-host build, AOT publish. |

---

## Category 5 — Docs (README / docs refresh)

### Table Stakes

| Feature | Why Expected | Complexity | Notes |
|---|---|---|---|
| "What it is" (SWTOR live DPS/HPS/APM parser + overlay) | Anyone landing on the repo needs the one-liner | LOW | Update to .NET 10, WinUI 3 overlay. |
| Install / run instructions | Users must be able to launch a host | LOW | Prereqs (.NET 10 / Windows App SDK runtime for overlay), how to run each host. |
| Overlay usage | The headline feature; how to position, drag, resize text, show/hide | MEDIUM | **Must state the fullscreen caveat:** run SWTOR in Fullscreen (Windowed)/Borderless; exclusive fullscreen unsupported. |
| The three hosts explained | Managed CLI vs Native AOT CLI vs WinUI overlay — when to use which | LOW | Short table. |
| Enable SWTOR combat logging | Non-obvious prerequisite — no logs, no data | LOW | In-game setting + log file location. Easy to forget; high support value. |
| Build / contributing | How to build, run tests, the VSCode F5 story | LOW | Cross-link the new `.vscode` setup. |

### Differentiators

| Feature | Value Proposition | Complexity | Notes |
|---|---|---|---|
| Architecture blurb / diagram (core → `DpsHps` → hosts) | Helps contributors grasp the producer/consumer seam fast | LOW | Reuse the existing `.planning/codebase/ARCHITECTURE.md` data-flow diagram. |
| Screenshot/GIF of the overlay over the game | Sells the tool instantly | LOW | One capture (windowed mode). |
| Troubleshooting (overlay behind game, no stats) | Pre-empts the most common issues | LOW | Maps to BL-01 caveat + logging-enabled caveat. |

### Anti-Features

| Feature | Why Requested | Why Problematic | Alternative |
|---|---|---|---|
| Exhaustive API docs / generated reference site | "Document everything" | It's a single-user tool, not a library for external consumers; high upkeep | A focused README + the `.planning/codebase/` maps. |
| Cross-platform install notes | Completeness | Misleading — app is intentionally Windows-only, reinforced by WinUI 3 | State Windows-only explicitly. |
| Marketing/feature roadmap in README | "Show the vision" | Drifts stale; out of scope | Keep roadmap in `.planning/`. |

---

## Feature Dependencies

```
[WinUI 3 overlay] ──requires──> [consumes core DpsHps IObservable unchanged]
                  ──requires──> [UI-thread marshalling via DispatcherQueue]   (correctness-critical)
                  ──requires──> [SlidingExpirationList/Entry from core View/]
                                       └──needs──> [ObservableCollection adapter for WinUI binding]

[Always-on-top over borderless (BL-01)] ──requires──> [CsWin32: SetWindowPos + SetWinEventHook + topmost/SWP consts]
[Window drag]                            ──requires──> [CsWin32: SendMessage + ReleaseCapture + WM_NCLBUTTONDOWN/HTCAPTION]
[Click-through (differentiator)]         ──requires──> [CsWin32: Get/SetWindowLong + WS_EX_LAYERED|WS_EX_TRANSPARENT]

[Click-through] ──conflicts──> [Window drag]            (a click-through window can't be grabbed)
[Click-through] ──conflicts──> [Font +/- buttons]       (buttons can't receive clicks when transparent to input)
                  └── resolved by ──> [toggle: locked/click-through  vs  movable/interactive]

[VSCode launch.json (debug overlay/CLIs)] ──requires──> [tasks.json per-host build tasks]
[VSCode test task]                         ──requires──> [MSTest migration complete]
[Docs: overlay usage + troubleshooting]    ──requires──> [BL-01 outcome known + fullscreen caveat decided]
[Retire WinForms overlay]                  ──requires──> [WinUI overlay validated at parity]   (build-before-delete)
```

### Dependency Notes

- **UI-thread marshalling is the port's top risk.** `DpsHps.OnNext` fires on a background reader task; WinForms tolerated cross-thread `DataSource` updates less strictly than WinUI's `DispatcherQueue` requirement. Every `OnNext` must `DispatcherQueue.TryEnqueue(...)`. Get this wrong and the overlay crashes or silently stops updating.
- **Transparency is harder in WinUI 3 than WinForms.** There is no `TransparencyKey` equivalent; expect `SystemBackdrop`/`DesktopAcrylicController` for translucency or layered-window interop for true alpha. Budget design time here — it is MEDIUM, not LOW.
- **Click-through is the one feature that fights the others.** Resolve with a mode toggle; do not ship it as an always-on default, or the user loses drag and the font buttons.
- **MSTest parallelism default inverts xUnit's.** The serialization the `[Collection]` provides is free under MSTest's sequential default — but the instant anyone enables `[Parallelize]`, the `CombatLogs` source-swap tests must be `[DoNotParallelize]` or they go non-deterministic (the race is real, documented in `CombatLogsSourceCollection.cs`).
- **Build-before-delete the WinForms host.** Per PROJECT.md key decision: never leave the repo without a working overlay.

---

## MVP Definition

### Launch With (v1.1 core)

- [ ] WinUI 3 overlay at **strict parity**: transparent, borderless, always-on-top, draggable, live 5-column rows, 10s expiry, font +/-, monitor-on-activation, consuming `DpsHps` unchanged — *essential, this is the milestone*
- [ ] **BL-01 fix**: stays on top of borderless/windowed SWTOR (SetWindowPos topmost re-assert + foreground hook) — *the overlay's whole reason to exist*
- [ ] **CsWin32** interop replacing `NativeMethods.cs` (SendMessage/ReleaseCapture/SetWindowPos/SetWinEventHook) — *issue #3*
- [ ] **MSTest migration**: all 106 tests pass, internals access intact, source-swap tests serialized, CI green — *issue #2*
- [ ] **VSCode** launch.json (per-host F5) + tasks.json (build-all / test / AOT-publish) — *contributor baseline*
- [ ] **Docs refresh**: README (what/install/run/3 hosts/overlay usage/fullscreen caveat/enable-logging/build) — *milestone deliverable*
- [ ] **Retire WinForms overlay** after WinUI parity validated — *one host to maintain*

### Add After Validation (v1.1.x — only if cheap and wanted)

- [ ] Position/size/opacity persistence — *trigger: user annoyed by re-centering each launch*
- [ ] Click-through mode toggle (+ `WS_EX_NOACTIVATE`/`TOOLWINDOW`) — *trigger: user wants true non-blocking overlay*
- [ ] Acrylic/Mica backdrop for legibility — *trigger: stat text hard to read over bright scenes*

### Future Consideration (v1.2+ — explicitly deferred)

- [ ] Global show/hide hotkey — *defer: nice-to-have, needs RegisterHotKey*
- [ ] Configurable columns / themes / new metrics — *defer: new product capability, out of v1.1 scope by decree*

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---|---|---|---|
| WinUI overlay strict parity (incl. UI-thread marshalling) | HIGH | MEDIUM | P1 |
| BL-01 topmost-over-borderless fix | HIGH | MEDIUM | P1 |
| CsWin32 interop replacement | MEDIUM | LOW | P1 |
| MSTest migration (parity + serialized source tests) | MEDIUM | MEDIUM | P1 |
| VSCode launch.json + tasks.json | MEDIUM | LOW | P1 |
| Docs refresh (+ fullscreen & enable-logging caveats) | MEDIUM | LOW | P1 |
| Retire WinForms overlay | MEDIUM | LOW | P1 |
| Position/opacity persistence | MEDIUM | LOW | P2 |
| Click-through mode (with toggle) | HIGH | MEDIUM | P2 |
| Acrylic/Mica backdrop | LOW | MEDIUM | P2 |
| Global show/hide hotkey | LOW | MEDIUM | P3 |
| Configurable columns / new metrics | LOW (this milestone) | HIGH | P3 (out of scope) |

## Competitor Feature Analysis

| Feature | Typical game DPS overlays (ACT, Details!, Recount-style) | Steam/Discord in-game overlay | Our approach |
|---|---|---|---|
| Render over game | Windowed/borderless; some use DirectX hook for exclusive fullscreen | DirectX/Vulkan injection → works in exclusive fullscreen | Windowed/borderless only; **no injection** — document the caveat |
| Click-through | Common, toggleable ("lock" the overlay) | Always pass-through until summoned | Differentiator (P2) via toggle; parity ships movable |
| Always-on-top | Standard | Standard | Table stakes via `IsAlwaysOnTop` + topmost re-assert |
| Persistence | Standard (saves position/layout) | N/A | Differentiator (P2) |
| Live per-actor rows w/ expiry | Standard (combat-scoped) | N/A | Table stakes (reuse core 10s `SlidingExpirationList`) |
| Customizable columns/themes | Standard | N/A | Anti-feature for v1.1 (out of scope) |

## Sources

- Parity baseline (read directly): `SwtorLogParser.Overlay/ParserForm.cs`, `SwtorLogParser.Overlay/NativeMethods.cs`, `SwtorLogParser/View/Entry.cs`, `SwtorLogParser/View/SlidingExpirationList.cs`, `SwtorLogParser.Tests/*` (incl. `Fixtures/CombatLogsSourceCollection.cs`, `GlobalUsings.cs`), `SwtorLogParser.csproj` (`InternalsVisibleTo`), `Directory.Packages.props`
- Project/milestone context: `.planning/PROJECT.md`, `.planning/codebase/ARCHITECTURE.md`, `.planning/BACKLOG.md` (BL-01)
- WinUI 3 overlay/transparency/topmost: Microsoft Q&A "[WinUI3] Semi Transparent Window + Click through Window"; microsoft/microsoft-ui-xaml issues #1247, #2956; Microsoft Learn "Manage app windows", `OverlappedPresenter.IsAlwaysOnTop` (MEDIUM confidence — WinUI transparency has no single official recipe; expect implementation iteration)
- MSTest migration: Microsoft Learn "Test execution and control in MSTest", MSTEST0001 (parallelization), "Migrating VSTest → Microsoft.Testing.Platform"; xUnit.net MTP docs (HIGH confidence)
- CsWin32: github.com/microsoft/CsWin32 README + NativeMethods.txt usage; NuGet `Microsoft.Windows.CsWin32` (HIGH confidence)

---
*Feature research for: in-game stats overlay re-implementation (WinForms → WinUI 3) + .NET dev/test toolchain modernization*
*Researched: 2026-06-12*
