# SWTOR Log Parser

A fast, memory-efficient parser for *Star Wars: The Old Republic* combat logs, built on **.NET 10** and modern C#. A shared core library tails the game's log files, parses each line with zero-allocation span parsing, and exposes a live reactive stream of per-player DPS/HPS/APM stats. A transparent in-game overlay and command-line hosts render that stream.

## Features

- [x] Zero-allocation span parsing of combat-log lines into a typed model (`ReadOnlySpan<T>` / `ReadOnlyMemory<T>`)
- [x] Core library is **AOT-compatible** (no reflection)
- [x] Live **DPS / HPS / APM** calculations powered by Rx.NET
- [x] **Transparent in-game overlay** (Dear ImGui) — clear see-through, always-on-top, auto-pins over the SWTOR window
- [x] Built-in **mini combat log** in the overlay — human-readable ability/damage feed (great for streamers showing their rotation)
- [x] Command-line hosts for monitoring and listing logs

## The Overlay

`SwtorLogParser.Overlay.ImGui` is an immediate-mode (Dear ImGui + Silk.NET/OpenGL) overlay with a genuinely transparent framebuffer:

- **Clear transparency** — the game shows through; an opacity slider controls the panel tint.
- **Pins to the game** — finds the running `swtor.exe` window, snaps over its top-right, and follows it if you move the game window. Polls and shows *"Waiting for SWTOR…"* until the game is up (resilient to launch order / relaunch).
- **Always-on-top** over windowed/borderless SWTOR; never steals focus and stays out of Alt-Tab.
- **Draggable** via the `☰` grip; adjustable font size; remembers position/opacity/font between runs.
- **Combat log** — tick **Log** to expand a rolling, plain-language feed of ability events (`time  player  ability -> target  amount (crit)`), with the raw GUIDs/syntax stripped.

**Fullscreen:** run SWTOR in **Fullscreen (Windowed)** or **Borderless**. Exclusive fullscreen cannot be overlaid by any normal window.

**Combat logging:** the game must be writing combat logs (enable in-game), which land in `Documents\Star Wars - The Old Republic\CombatLogs`. The overlay/CLIs read the newest file.

## Usage

### Overlay

```bash
dotnet publish SwtorLogParser.Overlay.ImGui -c Release -r win-x64 --self-contained
# then run the produced SwtorLogParser.Overlay.ImGui.exe with SWTOR running (windowed/borderless)
```

### Command line

```bash
dotnet run --project SwtorLogParser.Cli -- monitor   # live per-player stats
dotnet run --project SwtorLogParser.Cli -- list       # list combat-log files
```

## Building & developing

- **Requirements:** Windows, the **.NET 10 SDK**, and SWTOR with combat logging enabled.
- **VS Code:** press `F5` for the *Overlay (ImGui)* or *Managed CLI* launch configs; `tasks.json` provides `build`, `test`, and `publish-overlay` tasks.
- **CLI:**

  ```bash
  dotnet build SwtorLogParser.slnx          # build the solution
  dotnet test SwtorLogParser.Tests          # run the test suite
  ```

## Architecture

- **`SwtorLogParser`** — the AOT-compatible core: span parser, model types, and `CombatLogsMonitor` (file tailing + the `IObservable<PlayerStats>` DPS/HPS stream, plus a per-line `CombatLogChanged` event).
- **Hosts are pure consumers** — the overlay and CLIs subscribe to the core stream and render; no parsing logic lives in a host.
