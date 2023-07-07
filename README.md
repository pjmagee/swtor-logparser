# SWTOR Log Parser

A fast and efficient SWTOR log file parser using .NET 8.0 and the latest C# features.

## Features

- [x] Parses log files into an easily usable format
- [x] Memory efficient (no unnecessary allocations)
- [x] Fast (uses ReadOnlySpan\<T> and ReadOnlyMemory\<T> to avoid copying data)
- [x] Core library is AOT compatible (no reflection)
- [x] DPS & HPS calculations powered by Rx.NET
- [x] Native CLI compiles to native code (no .NET runtime required)
- [x] Minimal Overlay UI

## Usage

### Console (.NET)

To monitor logs in real-time, run the following command:
```bash
swtorlogparser.cli.exe monitor
```

To list SWTOR log files, run the following command:

```bash
swtorlogparser.cli.exe list
```

### Console (Native)

```bash
swtorlogparser.native.cli.exe monitor
```

### Overlay UI

```bash
SwtorLogParser.Overlay.exe
```
