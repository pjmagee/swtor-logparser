# Pitfalls Research

**Domain:** WinUI 3 transparent game overlay (Windows App SDK) + CsWin32 interop + xUnit→MSTest migration, on .NET 10
**Researched:** 2026-06-12
**Confidence:** MEDIUM-HIGH (MS Learn docs HIGH; WinUI 3 transparency/topmost community-sourced, MEDIUM; cross-checked where possible)

> Scope note: the WinForms overlay already achieves transparency (`TransparencyKey`/`Opacity`), click-through-where-transparent, drag (`WM_NCLBUTTONDOWN`), always-on-top, and live DPS/HPS render (`ParserForm.cs`). The risk in v1.1 is that **WinUI 3 makes several of these harder or removes the WinForms-native mechanism entirely.** This document is about the concrete mistakes made when re-implementing those exact features in WinUI 3.

---

## Critical Pitfalls

### Pitfall 1: Expecting a WinForms-style `TransparencyKey` / per-pixel color-key transparency in WinUI 3

**What goes wrong:**
Developers look for the WinForms equivalent of `TransparencyKey = Color.Black` + `Opacity = 0.5` and find nothing. There is **no first-class color-key transparency** in WinUI 3. Setting `Window.SystemBackdrop` to a Mica/Acrylic material does *not* give you a hole-punched, click-through-where-transparent window — it gives you a blurred/tinted backdrop. Attempts to "just set the background to Transparent" leave an opaque black or white window, because the XAML root and the host HWND still paint a solid surface.

**Why it happens:**
WinUI 3's compositor model is fundamentally different from GDI/WinForms. Transparency must be achieved by (a) extending the DWM frame into the client area and/or (b) making the underlying Win32 host window layered, then only painting the opaque content (the stats grid) and leaving the rest genuinely transparent. The WinForms `TransparencyKey` machinery has no port.

**How to avoid:**
Adopt this proven recipe rather than searching for a built-in property:
1. Set the XAML root `Grid.Background` and `Window` content backgrounds to `Transparent` (or `null`), and only give a visible background to the actual stats panel.
2. Drop the title bar/border via `OverlappedPresenter.SetBorderAndTitleBar(false, false)` (the AppWindow presenter route) — this is the WinUI-native way to get borderless, replacing `FormBorderStyle.None`.
3. For true see-through (not just a backdrop material), make the host HWND layered/extend the DWM frame: get the HWND (`WindowNative.GetWindowHandle`), then either `DwmExtendFrameIntoClientArea` with `MARGINS{-1}` or apply `WS_EX_LAYERED`. Validate that desktop/game shows through the transparent regions.
4. Treat **transparency** and **click-through** as two separate problems (see Pitfall 2) — making a pixel visually transparent does NOT make clicks fall through it.
5. Prototype this in a throwaway spike *before* porting the grid/drag/subscription logic. If the transparency spike fails, the whole overlay value proposition is at risk.

**Warning signs:**
Window renders solid black/white; `SystemBackdrop` "works" but you can still not see the game; the only transparency you can get is a uniform `Opacity` on the whole window (which dims your text too, unlike the WinForms setup where text stayed crisp).

**Phase to address:** Phase 1 (WinUI 3 overlay shell / transparency spike) — this is the highest-risk item and must be de-risked first.

---

### Pitfall 2: Conflating "visually transparent" with "click-through", and then losing input on the drag handle

**What goes wrong:**
Two opposite failures:
- (a) The transparent regions still **eat mouse clicks**, so clicking "through" the overlay onto the game does nothing — the player can't play.
- (b) Over-correcting by slapping `WS_EX_TRANSPARENT` on the whole window makes the *entire* overlay click-through, so the **drag handle and the +/- font buttons stop responding** — you can no longer move or resize the overlay (the WinForms overlay's `WM_NCLBUTTONDOWN` drag and `increase/decreaseButton` clicks both break).

**Why it happens:**
`WS_EX_TRANSPARENT` is whole-window: it makes the HWND ignore *all* mouse input, with no per-region granularity. WinForms got selective behavior implicitly via `TransparencyKey` hit-testing; WinUI 3 gives you no such automatic per-pixel hit-test.

**How to avoid:**
Pick one of two strategies and commit:
- **Strategy A (region toggle):** Keep the window normally hit-testable. When the pointer is *not* over the stats panel/drag handle, you want clicks to pass through — but a static `WS_EX_TRANSPARENT` can't do "sometimes." Practical compromise for this app: make the overlay a small, tightly-sized window that only covers the stats grid + handle (so there are no large transparent dead zones over the game). This sidesteps click-through entirely and matches the existing small WinForms overlay footprint.
- **Strategy B (dynamic style flip):** Add/remove `WS_EX_TRANSPARENT` at runtime via `SetWindowLong`/`GetWindowLong` based on a hotkey or hover, OR set `IsHitTestVisible="False"` on non-interactive XAML elements while keeping the drag handle/buttons hit-testable. (MS Q&A's working sample combines `WS_EX_TRANSPARENT` toggling with `IsHitTestVisible`.)
- Either way, **explicitly keep the drag region and the +/- buttons hit-testable** and add a manual UAT step "drag the overlay; click +/-; click through empty area onto the game."

**Warning signs:**
Clicking the game underneath the overlay does nothing; OR the overlay can no longer be dragged/buttons dead; OR `IsHitTestVisible="False"` accidentally applied to the panel containing the buttons.

**Phase to address:** Phase 1 (overlay shell) — must be solved alongside transparency; they're coupled.

---

### Pitfall 3: Re-implementing window drag — WinForms `WM_NCLBUTTONDOWN` trick must be re-wired through CsWin32

**What goes wrong:**
The existing drag is `ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0)` on `MouseDown` (`ParserForm.cs:140-147`). In WinUI 3 there is no `Form.Handle`, no `Control.MouseDown` with `MouseEventArgs.Button`, and the borderless window has no non-client caption to fake. Naive ports either (a) can't get the HWND, or (b) send the message but nothing moves because the presenter manages the title bar, or (c) the pointer events never fire because the element is `IsHitTestVisible="False"`.

**Why it happens:**
WinUI 3 uses `PointerPressed`/`PointerReleased` on `UIElement`, the HWND comes from `WindowNative.GetWindowHandle(this)`, and `SendMessage`/`ReleaseCapture` now come from CsWin32's `PInvoke` class (not hand-written `NativeMethods`). The message constants (`WM_NCLBUTTONDOWN = 0x00A1`, `HTCAPTION = 0x0002`) are generated by CsWin32 too and may be named slightly differently.

**How to avoid:**
- Wire drag via `PointerPressed` on the designated drag-handle element; obtain HWND once via `WindowNative.GetWindowHandle`.
- Use the CsWin32-generated `PInvoke.ReleaseCapture()` and `PInvoke.SendMessage(hwnd, WM_NCLBUTTONDOWN, HTCAPTION, 0)`; confirm the constant names CsWin32 emits.
- Alternative (cleaner, no message hack): move the window directly via `AppWindow.Move(new PointInt32(x, y))` computed from pointer delta — avoids the non-client message trick entirely and is fully managed.
- Keep the drag handle a real, hit-testable element (ties into Pitfall 2).

**Warning signs:** Overlay won't move; `GetWindowHandle` throws/returns zero; pointer events never fire on the handle.

**Phase to address:** Phase 1 (overlay shell) — drag is parity-critical.

---

### Pitfall 4: Always-on-top regressions — WinUI 3 drops topmost on foreground change, and topmost never beats exclusive fullscreen (BL-01)

**What goes wrong:**
This is the original BL-01 bug carried forward, *plus* a WinUI-3-specific aggravation:
- Setting `OverlappedPresenter.IsAlwaysOnTop = true` (the WinUI route for `TopMost`) gets the overlay above other windows initially, but when the **game takes foreground** (a borderless/maximized window grabbing focus), the overlay silently drops behind it — exactly the WinForms BL-01 symptom.
- **WinUI-3-specific:** there is a documented Windows App SDK bug (1.6.x, microsoft-ui-xaml #9990) where launching a WinUI 3 window can knock *other* processes' topmost windows out of the topmost band. Z-order interactions are flakier than WinForms.
- **Hard limit (state honestly):** `HWND_TOPMOST` / `IsAlwaysOnTop` works for **windowed and borderless/fullscreen-windowed** SWTOR but **CANNOT** sit above **exclusive (true) fullscreen** — exclusive fullscreen bypasses the DWM compositor and no normal topmost overlay can cover it. This is not fixable in v1.1; it must be documented as a known limitation, with the user instructed to run SWTOR in **Fullscreen (Windowed)/Borderless**.

**Why it happens:**
`TopMost`/`IsAlwaysOnTop` only sets the topmost *band* once; the OS re-orders z-order on foreground changes and does not re-assert it. Exclusive fullscreen uses a dedicated swap chain that the compositor doesn't layer over.

**How to avoid (windowed/borderless case — the BL-01 fix):**
- Set `OverlappedPresenter.IsAlwaysOnTop = true`, and additionally re-assert via CsWin32 `PInvoke.SetWindowPos(hwnd, HWND_TOPMOST, 0,0,0,0, SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE)`.
- Re-assert on a **low-frequency timer** (e.g. every 1–2 s) AND/OR on **foreground change** via `PInvoke.SetWinEventHook(EVENT_SYSTEM_FOREGROUND, ...)`. The win-event hook is the responsive path; the timer is the cheap safety net.
- Consider `WS_EX_NOACTIVATE` + `WS_EX_TOOLWINDOW` so the overlay never steals focus from the game (matches BL-01 proposal).
- **Document the exclusive-fullscreen limitation** in README and the overlay docs; add a UAT step "validate over SWTOR in Fullscreen (Windowed)."

**Warning signs:**
Overlay visible in alt-tab but hidden behind the game during play; works on the desktop but vanishes once the game is focused; only reappears when you alt-tab.

**Phase to address:** Phase 2 (topmost / BL-01 fix) — depends on Phase 1's HWND access and CsWin32 being in place. Owns the `SetWinEventHook`/`SetWindowPos` re-assert pattern and the honest fullscreen-limitation docs.

---

### Pitfall 5: Cross-thread `COMException (RPC_E_WRONG_THREAD, 0x8001010E)` updating XAML from the Rx background thread

**What goes wrong:**
`_monitor.DpsHps.Subscribe(OnNext)` delivers `PlayerStats` on the monitor's **background reader thread**. WinForms tolerated `DataGridView`/`SlidingExpirationList` updates more loosely (and `DataSource` binding marshaled some of it). WinUI 3 is strict: touching any XAML object off the UI thread throws `COMException 0x8001010E` ("interface marshalled for a different thread") and crashes the overlay.

**Why it happens:**
XAML objects have thread affinity to the `DispatcherQueue` that created them. Rx's default scheduler keeps the subscription on the producing thread; there is no implicit marshaling.

**How to avoid:**
- Capture the UI `DispatcherQueue` on the UI thread (in the window/page constructor): `_dispatcher = DispatcherQueue.GetForCurrentThread();`.
- In `OnNext`, marshal every UI mutation: `_dispatcher.TryEnqueue(() => _list.AddOrUpdate(stats));` — OR use `.ObserveOn(...)` with a DispatcherQueue scheduler (Rx.NET lacks a built-in WinUI scheduler — issue dotnet/reactive #1651 — so either hand-roll a tiny `IScheduler` over `TryEnqueue` or marshal manually in `OnNext`). Manual `TryEnqueue` is the lowest-risk choice.
- Do **not** call `DispatcherQueue.GetForCurrentThread()` inside `OnNext` — on the background thread it returns null. Capture it once on the UI thread.

**Warning signs:**
First DPS update crashes the overlay; exception code `0x8001010E`; "marshalled for a different thread"; null `DispatcherQueue` inside the subscription handler.

**Phase to address:** Phase 1 (overlay shell — render path) — the moment the live stream is wired to XAML.

---

### Pitfall 6: Unpackaged Windows App SDK app "won't start outside Visual Studio" / runtime not found — and the CI build trap

**What goes wrong:**
The overlay runs under F5 in VS but, when launched from a built/published folder, fails with a Windows App Runtime "not found" / bootstrapper error and exits immediately. Separately and more dangerously for this project: adding a WinUI 3 project to the solution can **break `dotnet build SwtorLogParser.slnx` and the CI** if the build needs the Windows App SDK workload/runtime or MSIX tooling that the headless `windows-latest` runner doesn't have configured.

**Why it happens:**
This is an **unpackaged** (no MSIX) desktop app. Framework-dependent unpackaged WinUI 3 apps need the Windows App SDK runtime present *and* a bootstrapper auto-initializer to add it to the package graph. With `WindowsPackageType=None` but no self-contained flag, it relies on a dynamic-dependency polyfill that must find an installed runtime — absent on a clean machine and on CI.

**How to avoid:**
- Set `<WindowsPackageType>None</WindowsPackageType>` AND `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>` so the runtime ships with the app and the bootstrapper isn't required (larger output, but no "won't start" trap and no machine-state dependency). This is the right default for a locally-distributed gaming tool.
- Verify it launches from a **published folder on a clean profile**, not just F5.
- **CI:** confirm the GitHub Actions `windows-latest` runner can `dotnet restore`/`build` the WinUI 3 project. The Windows App SDK NuGet packages generally restore without a separate workload, but the build may pull MSIX/WinAppSDK build tasks. Mitigations: ensure the runner has the needed SDK; consider building the overlay project in a step that can tolerate failure initially, or gate it. The existing CI already runs on `windows-latest` and builds the full `.slnx` including the WinForms overlay — adding the WinUI 3 project must be validated to keep `dotnet build SwtorLogParser.slnx -c Release` green.
- Keep the overlay out of any AOT publish path (see Pitfall 7).

**Warning signs:**
App runs under F5 only; "The system cannot find the file specified"/Windows App Runtime errors on launch; CI build step fails at the overlay project with WinAppSDK/MSIX task errors; works on the dev machine (which has the runtime from VS) but not on CI/clean machines.

**Phase to address:** Phase 1 (project setup) for the self-contained/unpackaged config; Phase that touches CI (likely Phase 1 or a dedicated tooling phase) for the headless-build validation. Add a "launches from published folder on clean machine" gate.

---

### Pitfall 7: WinUI 3 Native AOT contaminating the AOT CLI / `IsAotCompatible` core (central package management version skew)

**What goes wrong:**
Two contamination risks:
- (a) Trying to AOT-publish the overlay. WinUI 3's `PublishAot` support (WinAppSDK 1.6+) is **partial and fragile**: it requires trimming, C#/WinRT rooting of XAML/binding types, `partial` classes, and emits trimming warnings from `WinRT.Runtime`/`System.Linq.Expressions`. For a transparent stats overlay this is high-effort, low-reward — and PROJECT.md explicitly puts "WinUI 3 overlay on Native AOT" **out of scope**. The overlay must stay a normal managed host.
- (b) Adding WinUI 3 / `Microsoft.WindowsAppSDK` / `Microsoft.Windows.CsWin32` / C#/WinRT package versions to the shared `Directory.Packages.props` (CPM) and accidentally pulling new transitive deps or analyzers into `SwtorLogParser` (the `IsAotCompatible=true` core) or `SwtorLogParser.Native.Cli` — breaking their AOT cleanliness, or worse, introducing reflection into the core.

**Why it happens:**
Central Package Management unifies versions solution-wide; a `PackageVersion` added for the overlay is visible to every project. CsWin32 is a source generator (compile-time, generally AOT-safe), but C#/WinRT and WinAppSDK runtime assemblies are not something you want referenced from the AOT CLI or the core library.

**How to avoid:**
- **Do not** add `PublishAot` to the overlay. Confirm overlay `.csproj` has no AOT/trim flags; keep it framework-dependent-but-self-contained managed.
- Reference WinUI 3 / WinAppSDK / C#/WinRT packages **only from the overlay project** via `<PackageReference>` (versions centralized in `Directory.Packages.props`, but the *reference* lives only in the overlay).
- Keep CsWin32 confined to the overlay project's `NativeMethods.txt`; do not add it to the core or the AOT CLI (they have no Win32 interop needs).
- Verify the AOT CLI still publishes clean: `dotnet publish SwtorLogParser.Native.Cli -c Release` (the existing CI `aot-publish` job) must remain warning-free after the overlay is added. Re-run it as a gate.
- Keep the core library `IsAotCompatible=true` and run the AOT publish as a regression check at the end of every overlay phase.

**Warning signs:**
New trimming/AOT analyzer warnings appearing in the `Native.Cli` publish after overlay work; CsWin32 or WinRT packages showing up as transitive deps of the core/AOT CLI; a `PublishAot` line creeping into the overlay csproj; `aot-publish` CI job (non-blocking) starting to emit warnings.

**Phase to address:** Phase 1 (overlay project setup — establish package isolation) and re-verified in every overlay phase via the AOT publish regression check.

---

### Pitfall 8: CsWin32 setup mistakes — `NativeMethods.txt` symbols, unsafe blocks, and the static-delegate `SetWinEventHook` trap

**What goes wrong:**
- Wrong/misspelled symbol names in `NativeMethods.txt` silently generate nothing → "PInvoke does not contain a definition for X."
- `AllowUnsafeBlocks` disabled (or explicitly `false`) → generated pointer code won't compile. CsWin32 sets it `true` by default, but an inherited `Directory.Build.props` or explicit `false` overrides it.
- Hand-written signatures (the old `NativeMethods.cs` used `int SendMessage(IntPtr,int,int,int)`) don't match CsWin32's generated signatures (`HWND`, `WPARAM`, `LPARAM`, `nint`), causing call-site compile errors when migrating.
- **`SetWinEventHook` (needed for the BL-01 foreground re-assert):** the `WinEventProc` callback must be a **static** method and the delegate must be **pinned/kept alive** for the hook's lifetime — an instance-method or GC-collected delegate causes random crashes/`AccessViolation` when the OS calls back.

**Why it happens:**
CsWin32 generates strongly-typed, often-`unsafe` signatures from the Win32 metadata; they intentionally differ from the loose hand-rolled `DllImport`s. Native callbacks have no `this` and the GC will move/collect a managed delegate the OS still holds.

**How to avoid:**
- List exact metadata names in `NativeMethods.txt`: `SendMessage`, `ReleaseCapture`, `SetWindowPos`, `SetWinEventHook`, `UnhookWinEvent`, plus needed constants/handles. Build immediately after each addition to confirm generation.
- Confirm `AllowUnsafeBlocks` is `true` for the overlay (don't fight CsWin32's default).
- Update all call sites to the generated types (`HWND`, `WPARAM`, `LPARAM`); use `(HWND)hwnd` casts. Expect a one-time signature-churn pass replacing the old `NativeMethods` usages.
- For `SetWinEventHook`: keep the callback `static`, store the delegate in a **static field** so it isn't GC'd, and call `UnhookWinEvent` on window close.
- Delete `NativeMethods.cs` only after the CsWin32 equivalents compile and the overlay runs (build-before-delete, mirroring the WinForms→WinUI sequencing decision).

**Warning signs:**
"PInvoke has no definition for…"; CS0227 unsafe-code errors; signature mismatch errors at SendMessage/SetWindowPos call sites; intermittent crash/AccessViolation when a foreground change fires the win-event callback.

**Phase to address:** Phase 1 (introduce CsWin32, replace `NativeMethods` for drag) and Phase 2 (`SetWinEventHook`/`SetWindowPos` for topmost). The static-delegate trap specifically belongs to the Phase that adds the win-event hook.

---

### Pitfall 9: MSTest migration breaks CI code coverage and the `dotnet test` command (MTP vs VSTest)

**What goes wrong:**
This is the **highest-impact migration footgun.** Adopting the modern **MSTest.Sdk** defaults to **Microsoft.Testing.Platform (MTP)**, not VSTest. Consequences:
- `coverlet.collector` (currently `6.0.4`, used by the CI `--collect:"XPlat Code Coverage"` step) **does not work with MTP** — both coverlet.collector and coverlet.msbuild rely on VSTest infrastructure.
- The CI command `dotnet test ... --collect:"XPlat Code Coverage" --results-directory ...` and the coverage artifact upload **silently produce no coverage or fail**, because MTP uses different CLI flags/runsettings.
- The xUnit→MSTest attribute/assertion rewrite is mechanical, but if combined with an unannounced runner change, CI goes red for confusing reasons.

**Why it happens:**
MSTest 3.2+ ships its own lightweight MTP runner and the SDK defaults to it. MTP is a different execution architecture from VSTest; the old `--collect` coverage collector and many `.runsettings` entries don't apply.

**How to avoid — choose one explicitly:**
- **Option A (keep current CI working, lowest churn):** set `<UseVSTest>true</UseVSTest>` in the test project so MSTest runs under VSTest. Then `coverlet.collector` + the existing `dotnet test --collect:"XPlat Code Coverage"` CI step keep working unchanged. Recommended for v1.1 to avoid CI churn.
- **Option B (go full MTP):** switch coverage to `coverlet.MTP` (the MTP-native coverlet extension) or set `<EnableMicrosoftTestingExtensionsCodeCoverage>` and update the CI command to the MTP invocation. Higher churn; do only if you specifically want MTP's speed.
- Decide A-vs-B **up front** and update `.github/workflows/*.yml` in the same phase as the migration. Run CI on the branch before merge.

**Warning signs:**
Coverage artifact empty/missing after migration; `dotnet test --collect:"XPlat Code Coverage"` no-ops or errors; CI green locally (VS) but coverage gone on the runner; "unknown option `--collect`" style errors under MTP.

**Phase to address:** The MSTest migration phase. CI workflow edits must land in the same phase. This is a "looks done but isn't" — the tests pass but coverage collection is silently broken.

---

### Pitfall 10: xUnit→MSTest attribute & assertion rewrite errors (argument order, data rows, lifecycle)

**What goes wrong:**
The 106-test suite (`SwtorLogParser.Tests`, all `[Fact]`, snake_case names, `Assert.Equal`/`Assert.Null`/`Assert.NotNull`/`Assert.True`/`Assert.Single`/`Assert.NotEmpty`) is mechanically rewritten and subtle bugs slip in:
- **`Assert.Equal(expected, actual)` → `Assert.AreEqual(expected, actual)`** keeps the same order, but it's easy to flip; worse, devs sometimes "fix" it to `Assert.AreEqual(actual, expected)` and reverse the semantics — failure messages become misleading.
- `Assert.Null/NotNull` → `Assert.IsNull/IsNotNull`; `Assert.True/False` → `Assert.IsTrue/IsFalse`.
- `Assert.Single(collection)` and `Assert.NotEmpty` have **no direct MSTest equivalent** → must become `Assert.AreEqual(1, collection.Count())` / `Assert.IsTrue(collection.Any())`. Easy to mistranslate.
- `Assert.Throws<T>` → `Assert.ThrowsException<T>` (and async `ThrowsExceptionAsync`).
- The suite has no `[Theory]/[InlineData]` today, but TESTING.md flags them as a gap; if added, use `[DataTestMethod]` + `[DataRow]`, not `[Theory]/[InlineData]`.
- **`GlobalUsings.cs` has `global using Xunit;`** → must become `global using Microsoft.VisualStudio.TestTools.UnitTesting;`. Missing this produces a wall of "Fact not found" errors.
- xUnit's per-test **constructor setup / `IDisposable` teardown** → MSTest `[TestInitialize]`/`[TestCleanup]`. The current suite uses inline literals and no fixtures, so low risk here — but watch the `CombatLogs`-static-ctor environment-dependent tests (TESTING.md): they remain non-hermetic regardless of framework.

**Why it happens:**
The two frameworks have different assertion names, no 1:1 mapping for `Single`/`NotEmpty`, and the global-using swap is silent until compile.

**How to avoid:**
- Do the global-using swap first; let the compiler enumerate every broken assertion, then fix mechanically.
- Build a small mapping cheat-sheet (Equal→AreEqual same order, Null→IsNull, True→IsTrue, Single→AreEqual(1,..Count()), NotEmpty→IsTrue(..Any()), Throws→ThrowsException) and apply consistently.
- Preserve `expected, actual` order in `AreEqual` to keep failure messages correct.
- Confirm `InternalsVisibleTo` still works — it's a C# attribute, framework-agnostic; the test project references only the core lib (TESTING.md), so no change needed, but verify any `internal` access still compiles.
- After migration, assert **all 106 tests still pass and count is unchanged** — a dropped/renamed test silently reducing the count is the classic regression.

**Warning signs:**
Test count drops below 106; `Assert.AreEqual` failures with backwards expected/actual messages; "type or namespace Fact not found"; `Single`/`NotEmpty` left as compile errors; internal-visibility compile errors.

**Phase to address:** The MSTest migration phase (same as Pitfall 9). Verification gate: 106/106 tests green under the new runner.

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Use whole-window `Opacity` instead of real per-region transparency | One property, "looks" transparent fast | Dims the stats text too; no click-through; not parity with WinForms | Never for final; OK only in throwaway spike |
| Keep hand-written `NativeMethods.cs` alongside CsWin32 "for now" | Avoids signature churn | Two interop styles, defeats issue #3, confusing | Only transiently during build-before-delete |
| Marshal Rx updates with `Task.Run`/`async void` instead of `DispatcherQueue` | Compiles | Still cross-thread COMException; intermittent crashes | Never |
| Adopt MTP without touching CI/coverage | "Modern" default | Silent loss of coverage; red/confusing CI | Never without coverage plan |
| Ship overlay framework-dependent (not self-contained) unpackaged | Smaller binary | "Won't start" on clean machines / CI | Only if you guarantee runtime install (not the case here) |
| Add WinAppSDK packages to CPM referenced broadly | One props file | AOT contamination of core/Native CLI | Never; isolate references to overlay |

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| Rx `DpsHps` → XAML | Update XAML directly in `OnNext` (background thread) | Capture UI `DispatcherQueue` once; `TryEnqueue` every mutation |
| CsWin32 `SetWinEventHook` | Instance callback / unpinned delegate | Static callback + static-field delegate; `UnhookWinEvent` on close |
| Windows App SDK runtime | Framework-dependent unpackaged, runtime absent | `WindowsPackageType=None` + `WindowsAppSDKSelfContained=true` |
| Game window topmost | Set `IsAlwaysOnTop` once | Re-assert via `SetWindowPos(HWND_TOPMOST)` on timer + `SetWinEventHook` foreground |
| coverlet + MTP | Keep `coverlet.collector` under MTP | `UseVSTest=true` (keep collector) OR switch to `coverlet.MTP` |
| Window close | Leak Rx subscription + win-event hook | Dispose `_hpsDpsSubscription`, `UnhookWinEvent`, stop timer on `Closed` |

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| `SetWindowPos` topmost re-assert on a tight timer | CPU spin / flicker | Use 1–2 s timer + event hook, not a high-frequency loop | Immediately if re-asserted every frame |
| Per-update full XAML rebuild from `OnNext` | UI jank under heavy combat | Reuse `SlidingExpirationList`/observable collection; update in place | High DPS log volume |
| Marshaling each line individually via `TryEnqueue` | Dispatcher flooded in big fights | Batch/throttle Rx (e.g. sample) before `TryEnqueue` | Dense AoE combat logs |

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| `SetWinEventHook` global hook scope too broad | Receives all foreground events, perf/privacy noise | Scope to `EVENT_SYSTEM_FOREGROUND` only; filter to game HWND |
| Self-contained WinAppSDK runtime never updated | Ships old runtime with fixed CVEs | Track WinAppSDK version in CPM; bump deliberately |

(Local-only tool, no network/auth/secrets — security surface is minimal by design.)

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| Overlay steals focus from the game | Clicks/keys go to overlay mid-fight | `WS_EX_NOACTIVATE` + `WS_EX_TOOLWINDOW`; never `SetForegroundWindow` |
| Silent disappearance over fullscreen | User thinks app is broken | Document "use Fullscreen (Windowed)"; consider a not-topmost warning |
| Transparent dead-zone eats game clicks | Can't play through the overlay | Tightly-sized window over stats only (Pitfall 2 Strategy A) |
| No visible drag handle in borderless mode | User can't move overlay | Keep a clearly hit-testable handle region |

## "Looks Done But Isn't" Checklist

- [ ] **Transparency:** Looks transparent in VS — verify the *game* shows through behind it on a real machine, and text stays crisp (not dimmed by whole-window opacity).
- [ ] **Click-through:** Verify clicks pass to the game in empty areas AND the drag handle + +/- buttons still respond.
- [ ] **Topmost:** Verify it stays above SWTOR in **Fullscreen (Windowed)** after the game takes foreground — not just on the desktop. Document exclusive-fullscreen as unsupported.
- [ ] **Launch:** Verify it starts from a **published folder on a clean machine**, not just F5 in VS.
- [ ] **CI:** Verify `dotnet build SwtorLogParser.slnx` and the AOT publish job stay green after the WinUI 3 project is added.
- [ ] **Coverage:** Verify the coverage artifact is still produced after the MSTest migration (the silent MTP break).
- [ ] **Test count:** Verify **106/106** tests still run and pass under the new runner.
- [ ] **Threading:** Run a real combat log through the overlay — first live update must not throw `0x8001010E`.
- [ ] **AOT cleanliness:** Re-run `dotnet publish SwtorLogParser.Native.Cli` — no new trim/AOT warnings.
- [ ] **Cleanup:** Window close disposes Rx subscription, unhooks win-event, stops the topmost timer (no leaks across overlay restarts).

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| WinUI 3 transparency dead-end | HIGH | Fall back to layered-HWND/DWM recipe; if truly infeasible, keep WinForms overlay (don't delete it until WinUI parity proven) |
| MTP broke CI coverage | LOW | Set `UseVSTest=true` and restore the `--collect` step |
| AOT CLI contaminated | MEDIUM | Move WinAppSDK/CsWin32 refs back into overlay-only; re-run AOT publish |
| Cross-thread COMException | LOW | Wrap all UI mutations in `DispatcherQueue.TryEnqueue` |
| Overlay won't start on clean machine | LOW | Add `WindowsAppSDKSelfContained=true`, republish |
| Lost drag input | LOW | Re-enable hit-testing on handle; switch to `AppWindow.Move` drag |

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| 1 Transparency model | Phase 1 (shell + transparency spike) | Game visible through overlay on real machine |
| 2 Click-through vs input loss | Phase 1 | Click-through to game + handle/buttons work |
| 3 Drag re-wire | Phase 1 | Overlay drags via pointer + CsWin32/AppWindow.Move |
| 4 Topmost / BL-01 / fullscreen | Phase 2 (topmost fix) | Stays over Fullscreen-Windowed; limitation documented |
| 5 Rx→Dispatcher threading | Phase 1 (render path) | Live update without 0x8001010E |
| 6 Unpackaged runtime + CI build | Phase 1 (project setup) + CI phase | Launches from published folder; CI build green |
| 7 AOT contamination | Phase 1 (package isolation) + every overlay phase | AOT publish warning-free |
| 8 CsWin32 setup + static delegate | Phase 1 (drag interop) + Phase 2 (win-event hook) | Generated PInvoke compiles; hook stable |
| 9 MTP coverage/CI break | MSTest migration phase | Coverage artifact present; CI green |
| 10 Assertion/attribute rewrite | MSTest migration phase | 106/106 tests pass, count unchanged |

## Sources

- [WinUI3 Semi Transparent + Click-through — Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/1418063/(winui3)-semi-transparent-window-click-through-win) — MEDIUM (community Q&A w/ MSFT replies)
- [How to make WinUI 3 window transparent — Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/1463839/how-to-make-winui-3-window-transparent) — MEDIUM
- [OverlappedPresenter.IsAlwaysOnTop — Windows App SDK API](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.overlappedpresenter.isalwaysontop) — HIGH (official API)
- [microsoft-ui-xaml #9990 — WinUI3 launch drops other apps' TOPMOST](https://github.com/microsoft/microsoft-ui-xaml/issues/9990) — MEDIUM (known issue)
- [SetWindowPos — Win32 API (HWND_TOPMOST semantics)](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos) — HIGH
- [Distribute an unpackaged WinUI 3 app — MS Learn](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app) — HIGH
- [WinAppSDK deployment guide (unpackaged / self-contained) — MS Learn](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps) — HIGH
- [Project properties / auto-initializers — MS Learn](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/project-properties) — HIGH
- [Windows App SDK 1.6 release notes (PublishAot support + limitations)](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-6) — HIGH
- [WinUI 3 Native AOT discussion #8082](https://github.com/microsoft/microsoft-ui-xaml/discussions/8082) — MEDIUM
- [CsWin32 — Getting Started](https://microsoft.github.io/CsWin32/docs/getting-started.html) — HIGH (official)
- [CsWin32 SetWinEventHook discussion #1162](https://github.com/microsoft/CsWin32/discussions/1162) — MEDIUM (static/pinned delegate)
- [WinUI 3 cross-thread COMException discussion #8410](https://github.com/microsoft/microsoft-ui-xaml/discussions/8410) — MEDIUM
- [dotnet/reactive #1651 — ObserveOn for DispatcherQueue](https://github.com/dotnet/reactive/issues/1651) — MEDIUM (Rx lacks built-in WinUI scheduler)
- [MSTest SDK configuration (MTP default, UseVSTest) — MS Learn](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-sdk) — HIGH
- [Microsoft.Testing.Platform code coverage (coverlet incompatibility) — MS Learn](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-code-coverage) — HIGH
- [coverlet — MTP integration / collector limitations](https://github.com/coverlet-coverage/coverlet) — HIGH
- Project files: `SwtorLogParser.Overlay/ParserForm.cs`, `NativeMethods.cs`, `.planning/BACKLOG.md` (BL-01), `.planning/codebase/TESTING.md`, `.github/workflows` CI, `Directory.Packages.props` — HIGH (direct inspection)

---
*Pitfalls research for: WinUI 3 overlay migration + CsWin32 + MSTest, .NET 10 SWTOR log parser*
*Researched: 2026-06-12*
