# Phase 3: Monitor Refactor + Coverage - Pattern Map

**Mapped:** 2026-06-11
**Files analyzed:** 11 (4 new, 7 modified)
**Analogs found:** 9 / 11 (2 have NO in-repo analog — see "No Analog Found")

This phase is a refactor of existing code, so the "analog" for most changes is the
*current version of the same file*. The excerpts below are the exact current code with
line numbers; planner should reference these as the baseline to transform.

---

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `SwtorLogParser/Monitor/CombatLogsMonitor.cs` (modify) | service/singleton | event-driven / Rx | itself (current ctors + `#if` block) | exact (self) |
| `SwtorLogParser/Monitor/CombatLogs.cs` (modify) | service | file-I/O + cache | itself + `Model/*.cs` cache sites | exact (self) |
| `SwtorLogParser/Model/GameObject.cs` (modify) | model | cache lookup | itself (`Parse`/`TryAdd`) | exact (self) |
| `SwtorLogParser/Model/Ability.cs` (modify) | model | cache lookup | `GameObject.Parse` (shared cache — the bug) | exact (self) |
| `SwtorLogParser/Model/Action.cs` (modify) | model | cache lookup | `GameObject.Parse` pattern | exact (self) |
| `SwtorLogParser/View/Entry.cs` (NEW, core lib) | view-model (UI-free) | transform | `*.Cli/View/Entry.cs` (identical pair) | exact |
| `SwtorLogParser/View/SlidingExpirationList.cs` (NEW, core lib) | store/list | event-driven | `*.Cli/View/SlidingExpirationList.cs` (identical pair) | exact |
| `SwtorLogParser.Overlay/View/SlidingExpirationList.cs` (modify → binding adapter) | view adapter | event-driven | current Overlay variant (keep WinForms part) | exact (self) |
| Bounded LRU cache type (NEW, core lib) | utility | cache | **none** — see canonical example | none |
| Filesystem abstraction seam (NEW, core lib) | service/interface | file-I/O | `CombatLogs` static members | partial (extract from static) |
| `SwtorLogParser.Tests/MonitorTests.cs` + `DpsHpsTests.cs` (NEW) | test | — | existing `*Tests.cs` (`[Fact]` xUnit) | role-match |

---

## Pattern Assignments

### RFCT-02 — `CombatLogsMonitor.cs` (service/singleton, Rx)

**Analog:** itself. The `Instance` is only defined for `RELEASE` and `DEBUG` configs; any
other config (e.g. test build, custom config) leaves `Instance` undefined → won't compile.
Both constructors are currently `private`.

**Current `#if` Instance block — `CombatLogsMonitor.cs:14-23`:**
```csharp
#if RELEASE
    public static CombatLogsMonitor Instance { get; } = new(NullLogger<CombatLogsMonitor>.Instance);
#elif DEBUG
    public static CombatLogsMonitor Instance { get; } =
        new(
            LoggerFactory
                .Create(x => x.ClearProviders().AddConsole().AddDebug())
                .CreateLogger<CombatLogsMonitor>()
        );
#endif
```
Transform target (per CONTEXT decision): collapse so `Instance` is defined in **all**
configs. Decision §25 says the default singleton uses `NullLogger<CombatLogsMonitor>.Instance`
(console/debug providers move host-side). Simplest: drop the `#if` entirely and use the
`NullLogger` form unconditionally — or add `#else` → `NullLogger`.

**Current private ctors — `CombatLogsMonitor.cs:43-46` and `110-114`:**
```csharp
    private CombatLogsMonitor()
    {
        ConfigureObservables();
    }
    // ...
    private CombatLogsMonitor(ILogger<CombatLogsMonitor> logger)
        : this()
    {
        _logger = logger;
    }
```
Transform target: make the `ILogger<CombatLogsMonitor>` ctor **public** (constructor
injection for DI/tests). Note `_logger` is assigned only in that ctor, so the
parameterless `Instance` path currently leaves `_logger` null — `Stop()` already guards
with `_logger?.` (line 139) but `ReadAsync`/`MonitorAsync` call `_logger.LogDebug` (lines
175, 233) unguarded. The default ctor should route through the logger ctor with
`NullLogger` so `_logger` is never null. **Do not break** the `: this()` chaining that
calls `ConfigureObservables()`.

**Testable seam (TEST-01):** the internal `Subject<CombatLogLine> CombatLogLines` at
`CombatLogsMonitor.cs:35` is `private`. `InternalsVisibleTo(SwtorLogParser.Tests)` is
already present (csproj line 11). Add an `internal` push method, e.g.
`internal void PublishForTest(CombatLogLine line) => CombatLogLines.OnNext(line);`, or make
the Subject `internal`. `DpsHps` (line 39) is already public and is what tests subscribe to.

**DPS/HPS math under test (TEST-02):** `Accumulator` (`64-75`) and
`CalculateDpsHpsStats` (`77-108`). The 10s window appears in two places that tests must
pin: the Rx `.Where(x => x.TimeStamp > DateTime.Now.AddSeconds(-10))` (line 51) and the
accumulator's `state.RemoveWhere(line => line.TimeStamp < combatLog.TimeStamp.AddSeconds(-10))`
(line 71). Crit% formula is `damage.Count(IsCritical) / state.Count * 100` (line 91-92).
**Do-not-break:** this is the live product behavior — tests lock it, refactor must not alter it.

---

### RFCT-03 — Cache redesign across `CombatLogs.cs` + `Model/*.cs`

**Cache fields — `CombatLogs.cs:9-10`:**
```csharp
    internal static readonly ConcurrentDictionary<int, Action> ActionCache = new();
    internal static readonly ConcurrentDictionary<int, GameObject> GameObjectCache = new();
```
Two problems per CONTEXT §28-31:
1. Key is `int` from `Rom.GetHashCode()` (reference+index+length, NOT content → ME-02). Switch to content key `rom.ToString()` (accept the allocation).
2. `GameObjectCache` is **shared** between `GameObject` and `Ability` (a subclass) — an `Ability` lookup can return a base `GameObject` and vice-versa (the type bug). Fix via separate caches per type, or a type-correct key.
3. Unbounded growth → add a size cap + simple/LRU eviction.

**The bug, concretely — three `Parse` sites all hit `GameObjectCache`:**

`GameObject.cs:101-115`:
```csharp
    public static GameObject? Parse(ReadOnlyMemory<char> rom)
    {
        if (CombatLogs.GameObjectCache.TryGetValue(rom.GetHashCode(), out var value))
            return (GameObject?)value;

        var gameObject = new GameObject(rom);
        if (gameObject.Id == null) return null;

        var key = gameObject.GetHashCode();
        if (CombatLogs.GameObjectCache.TryAdd(key, gameObject))
            return gameObject;

        // Another thread won the race for this key — return the cached instance.
        return CombatLogs.GameObjectCache.TryGetValue(key, out var existing) ? existing : gameObject;
    }
```

`Ability.cs:10-26` — **same `GameObjectCache`, casts result to `Ability?`:**
```csharp
    public static new Ability? Parse(ReadOnlyMemory<char> rom)
    {
        if (rom.Length == 0 || rom.IsEmpty)
            return null;

        if (CombatLogs.GameObjectCache.TryGetValue(rom.GetHashCode(), out var value))
            return (Ability?)value;          // <-- can return base GameObject cast to Ability => null/bug

        var ability = new Ability(rom);

        var key = ability.GetHashCode();
        if (CombatLogs.GameObjectCache.TryAdd(key, ability))
            return ability;

        return CombatLogs.GameObjectCache.TryGetValue(key, out var existing) ? (Ability?)existing : ability;
    }
```
> The `(Ability?)value` cast (line 16) is the latent bug: if a `GameObject` with the same
> hash was cached first, the `as`-style cast pattern here is a hard `(Ability?)` cast that
> throws or mis-returns. A `GameObject` Parse can likewise return an `Ability` instance.
> **Fix:** separate `AbilityCache` from `GameObjectCache`, or key by `(type, content)`.

`Action.cs:45-67` — uses `ActionCache` (correctly typed already), same `GetHashCode` key
pattern that must move to content key:
```csharp
    public static Action? Parse(ReadOnlyMemory<char> rom)
    {
        if (CombatLogs.ActionCache.TryGetValue(rom.GetHashCode(), out var value)) return (Action?)value;
        // ... new Action(rom); key = action.GetHashCode(); ActionCache.TryAdd(key, action); ...
    }
```
All four model `GetHashCode()` overrides return `Rom.GetHashCode()`
(`GameObject.cs:51-54`, `Action.cs:69-72`), which is exactly the non-content hash being
replaced. **Do-not-break:** `Action`'s static singletons `ApplyEffectDamage` /
`ApplyEffectHeal` / `EventAbilityActivate` (`Action.cs:16-23`) are built via `Parse` at
type-init — they must still resolve and be cached after the key change.

---

### RFCT-01 — View dedup into core lib

**Identical pair (CLI == Native.Cli).** `SwtorLogParser.Cli/View/SlidingExpirationList.cs`
and `SwtorLogParser.Native.Cli/View/SlidingExpirationList.cs` are byte-for-byte identical
except the namespace line (`SwtorLogParser.Cli.View` vs `SwtorLogParser.Native.Cli.View`).
Same for the two `Entry.cs`. These are the **UI-agnostic** version → promote verbatim into
the core lib.

**Core `SlidingExpirationList` to create (from `Cli/View/SlidingExpirationList.cs:1-70`):**
```csharp
using System.Collections.Immutable;
using SwtorLogParser.Monitor;

namespace SwtorLogParser.View;   // <-- new core namespace (Claude's discretion)

public class SlidingExpirationList
{
    private readonly TimeSpan _expirationTime;
    private readonly Timer _expirationTimer;
    private readonly SortedList<long, Entry> _items;

    public SlidingExpirationList(TimeSpan expirationTime) { /* ...as-is... */ }

    public IReadOnlyList<CombatLogsMonitor.PlayerStats> Items { get { /* lock + ImmutableList */ } }
    public void AddOrUpdate(CombatLogsMonitor.PlayerStats item) { /* lock; TryGetValue/Add; expiry */ }
    private void RemoveExpiredItems(object? state) { /* lock; remove expired */ }
}
```
This contains **no WinForms types** → AOT-safe. Uses only `SortedList`, `Timer`,
`ImmutableList`, `CombatLogsMonitor.PlayerStats`.

**Core `Entry` to create (from `Cli/View/Entry.cs:1-9`):**
```csharp
using SwtorLogParser.Monitor;

namespace SwtorLogParser.View;

public class Entry
{
    public CombatLogsMonitor.PlayerStats Stats { get; set; }
    public DateTime Expiration { get; set; }
}
```
> Note the Native.Cli `Entry` adds `= null!;` / `= default;` initializers (cosmetic).
> Pick the nullable-clean form (`= null!;`).

**What STAYS host-specific (Overlay only).** `SwtorLogParser.Overlay/View/SlidingExpirationList.cs`
is a **different beast** — it inherits `BindingList<Entry>` and drives a WinForms `Control`:

`Overlay/View/SlidingExpirationList.cs:1-26` (the WinForms surface that must NOT enter the core lib):
```csharp
using System.ComponentModel;
using SwtorLogParser.Monitor;
using Timer = System.Threading.Timer;

namespace SwtorLogParser.Overlay.View;

public class SlidingExpirationList : BindingList<Entry>     // <-- WinForms BindingList
{
    private readonly Control _control;                       // <-- WinForms Control
    private readonly Timer _renderTimer;
    private readonly List<Entry> _list = new();

    public SlidingExpirationList(Control control, TimeSpan expirationTime) { /* ... */ }
    private void Redraw(object? state) => _control.Invoke(Refresh);
    // AddOrUpdate / Refresh (ClearItems/InsertItem) / RemoveExpiredItems ...
}
```
And the Overlay `Entry` is **richer** (`Overlay/View/Entry.cs:5-21`): it implements
`IComparable<Entry>, IEquatable<Entry>` and adds formatted display props
(`Name`, `DPS`, `DCrit`, `HPS`, `HCrit`) for grid columns. This formatting/comparison is a
presentation concern.

**Recommended split for the Overlay (decision §35):**
- The core lib gets the plain `Entry` + UI-free `SlidingExpirationList` (the CLI shape).
- The Overlay keeps its own `BindingList<Entry>`-based adapter that *wraps/uses* the core
  expiry logic, plus its display-formatted `Entry`. The Overlay `Entry`'s display props +
  `IComparable`/`IEquatable` may stay an Overlay type (it's WinForms grid binding), OR the
  core `Entry` exposes raw `PlayerStats` and the Overlay derives display strings. Keep the
  WinForms `BindingList`, `Control`, `_renderTimer`, `Invoke(Refresh)` in the Overlay.

**Call sites that must keep compiling (RFCT-01 consumers):**
- `Overlay/ParserForm.cs:115`: `dataGridView.DataSource = _list = new SlidingExpirationList(dataGridView, TimeSpan.FromSeconds(10));` → still uses the Overlay binding type.
- `ParserForm.cs:13-16` fields `_list` / `_monitor`, and `ParserForm.cs:20-24` ctor already takes `CombatLogsMonitor monitor` (DI-ready — good, mirrors RFCT-02 public ctor intent).
- CLI/Native hosts construct the (now core) `SlidingExpirationList(TimeSpan)` — update their `using` to the new core namespace and delete their `View/` copies.

---

### TEST-01 / TEST-02 — filesystem seam + monitor/Rx/math tests

**Filesystem access to abstract — `CombatLogs.cs`:**

Path resolution (`CombatLogs.cs:12-20`):
```csharp
    private static readonly string LogsPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, Environment.SpecialFolderOption.None),
            "Star Wars - The Old Republic", "CombatLogs");

    private static readonly string SettingsPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.None), "SWTOR", "swtor", "settings");
```

`PlayerNames` static ctor (`CombatLogs.cs:22-29`) — runs at type-init, reads the settings dir:
```csharp
    static CombatLogs()
    {
        PlayerNames = SettingsDirectory.EnumerateFiles("*PlayerGUIState.ini")
            .Select(x => SecondSegmentOrNull(x.Name))
            .Where(n => n is not null).Select(n => n!).ToHashSet();
    }
```

Enumeration (`CombatLogs.cs:56-65`):
```csharp
    public static IEnumerable<CombatLog> EnumerateCombatLogs()
    {
        foreach (var fi in CombatLogsDirectory.EnumerateFiles("*.txt")) yield return new CombatLog(fi);
    }
    public static CombatLog? GetLatestCombatLog() { /* EnumerateFiles("*.txt").MaxBy(LastWriteTimeUtc) */ }
```
> `CombatLogsDirectory`/`SettingsDirectory` are `internal static DirectoryInfo` (lines 37-38),
> initialized from the hardcoded paths. The static ctor throws if the settings dir is
> missing — this is exactly why the two tests below flake on CI.

**The two non-hermetic tests that the seam must make deterministic:**

`Tests/CombatLogLineTests.cs:9-44` `All_Logs_Are_Not_Null` — iterates
`CombatLogs.EnumerateCombatLogs()` over the **real** `%Documents%` folder.

`Tests/ActorTests.cs:26-37` `Player_Is_Local_Is_True` — iterates `CombatLogs.PlayerNames`
(empty/throws when no real settings present):
```csharp
    public void Player_Is_Local_Is_True()
    {
        foreach (var name in CombatLogs.PlayerNames)
        {
            var actor = Actor.Parse($"@{name}#{Random.Shared.Next(1000000000)}|...".AsMemory());
            Assert.NotNull(actor);
            Assert.True(actor.IsPlayer);
            Assert.True(actor.IsLocalPlayer);
        }
    }
```
Seam target: introduce an interface (e.g. `ICombatLogSource` / `ILogFileSystem`) supplying
the logs directory, the `*.txt` enumeration, and the player-name set; have a default impl
wrapping the current `Environment.GetFolderPath` paths, and an in-memory/temp impl for
tests. Keep the static `CombatLogs` facade for hosts (behavior unchanged) but allow tests
to inject a fixture. **AOT note:** the interface + constructor-injected default keeps the
core lib reflection-free; do NOT pull in a DI container.

**Test file shape to copy — existing `*Tests.cs`:** xUnit `[Fact]`, namespace
`SwtorLogParser.Tests`, `using SwtorLogParser.Model; using SwtorLogParser.Monitor;`,
plain `Assert.*` (see `CombatLogLineTests.cs:1-7`). New `MonitorTests`/`DpsHpsTests` follow
the same structure, constructing the monitor via the new **public `ILogger` ctor** and
pushing lines through the internal seam, then asserting on `DpsHps`.

---

## Shared Patterns

### Cache-with-race pattern (apply to all `Parse` methods after RFCT-03)
**Source:** `GameObject.cs:101-115`. The `TryAdd` + "another thread won the race →
re-`TryGetValue`" idiom (Phase 2) is correct and must be preserved when swapping the key to
content and splitting caches. Replicated in `Ability.cs:21-25` and `Action.cs:54-59`.

### Null-return parsing (apply to all model + seam code)
**Source:** CONVENTIONS.md §62; e.g. `GameObject.Parse` returns `null` on invalid input
(`if (gameObject.Id == null) return null;`, line 107). Keep this for the seam impls too.

### Lock-around-mutable-collection (apply to SlidingExpirationList core + overlay)
**Source:** `Cli/View/SlidingExpirationList.cs` (`lock (_items)`) and
`Overlay/View/SlidingExpirationList.cs:13,35,55,70` (`lock (Lock)`). Preserve when promoting.

---

## No Analog Found

| File / type | Role | Data Flow | Reason / canonical example |
|-------------|------|-----------|----------------------------|
| Bounded LRU cache type | utility | cache | No bounded/evicting cache exists; current caches are plain unbounded `ConcurrentDictionary`. See minimal example below. |
| Filesystem abstraction interface | service/interface | file-I/O | No interfaces exist in the repo at all — everything is concrete static. Extract from `CombatLogs` static members. See minimal example below. |

### Canonical minimal example — bounded, content-keyed, thread-safe cache (AOT-safe, no reflection)
```csharp
// SwtorLogParser/Monitor/BoundedCache.cs  (illustrative; cap + simple eviction)
internal sealed class BoundedCache<TValue>
{
    private readonly int _capacity;
    private readonly ConcurrentDictionary<string, TValue> _map = new();
    private readonly ConcurrentQueue<string> _order = new(); // FIFO eviction (simplest bound)

    public BoundedCache(int capacity) => _capacity = capacity;

    public TValue GetOrAdd(string key, Func<string, TValue> factory)
    {
        if (_map.TryGetValue(key, out var existing)) return existing;
        var created = factory(key);
        if (_map.TryAdd(key, created))
        {
            _order.Enqueue(key);
            while (_map.Count > _capacity && _order.TryDequeue(out var oldest))
                _map.TryRemove(oldest, out _);
        }
        return _map.TryGetValue(key, out var v) ? v : created; // race winner
    }
}
```
> Key is the **content string** (`rom.ToString()`), satisfying the ME-02 fix. Use one
> `BoundedCache<GameObject>`, one `BoundedCache<Ability>`, one `BoundedCache<Action>` —
> separate instances fix the shared-cache type bug. FIFO is acceptable per discretion
> (§29); true LRU would require touch-on-read bookkeeping. Pick a cap (e.g. 4096–16384).

### Canonical minimal example — filesystem seam (constructor injection, no DI container)
```csharp
// SwtorLogParser/Monitor/ILogFileSystem.cs
public interface ILogFileSystem
{
    IEnumerable<FileInfo> EnumerateCombatLogs();   // *.txt in logs dir
    FileInfo? GetLatestCombatLog();
    ISet<string> PlayerNames;                      // from *PlayerGUIState.ini
}

// Default impl wraps today's Environment.GetFolderPath paths (behavior unchanged for hosts).
// Tests pass a fake/in-memory impl built over a temp dir or hardcoded fixtures, making
// All_Logs_Are_Not_Null and Player_Is_Local_Is_True hermetic.
```
> Keep the public static `CombatLogs` facade for hosts so existing call sites
> (`CombatLogs.EnumerateCombatLogs()`, `CombatLogs.PlayerNames`, `CombatLogs.CombatLogsDirectory`
> used in `CombatLogsMonitor.MonitorAsync` lines 237-248) keep working; route them through a
> default `ILogFileSystem`.

---

## Do-Not-Break List

1. **AOT core lib (`IsAotCompatible=true`):** no WinForms (`BindingList`, `Control`,
   `DataGridView`), no reflection, no DI container in `SwtorLogParser`. WinForms binding
   stays in `SwtorLogParser.Overlay` only.
2. **Live `DpsHps` behavior:** the Rx pipeline (`ConfigureObservables` 48-60), `Accumulator`
   (64-75), and `CalculateDpsHpsStats` (77-108) must produce identical output. The two 10s
   windows (lines 51, 71) and crit% formula (91-92) are the product's core value.
3. **`Instance` semantics:** the default singleton must keep behaving as today's intent
   (now defined in every config, `NullLogger`); hosts keep using `CombatLogsMonitor.Instance`.
4. **Phase 2 cancellation wiring:** `Start` linked-CTS dispose/recreate (118-129) and
   `Stop` null-safe teardown (131-150) — RFCT-02 must not regress these (TEST-01 covers them).
5. **`Action` static singletons** (`ApplyEffectDamage/Heal`, `EventAbilityActivate`,
   `Action.cs:16-23`) must still resolve after the cache key/type change.
6. **77-test regression suite** stays green every commit; the new filesystem seam must make
   `All_Logs_Are_Not_Null` + `Player_Is_Local_Is_True` deterministic (not delete them).
7. **Host call sites:** `ParserForm.cs:115` (Overlay binding ctor), `:24` (`DpsHps.Subscribe`),
   `:132` (`Start`); CLI/Native `SlidingExpirationList(TimeSpan)` usage — update namespaces,
   keep behavior.
8. **BL-01 (overlay topmost)** is NOT in scope — do not fold it in while editing Overlay View.

---

## Metadata

**Analog search scope:** `SwtorLogParser/Monitor/`, `SwtorLogParser/Model/`, the three
`*/View/` folders, `SwtorLogParser.Tests/`, `SwtorLogParser.Overlay/ParserForm.cs`.
**Files scanned:** ~14
**Pattern extraction date:** 2026-06-11
