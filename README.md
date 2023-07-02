# SWTOR Log Parser

A fast and efficient SWTOR log file parser using .NET 8.0 and the latest C# features.

## Features

- [x] Parses log files into a usable format
- [x] Memory efficient (no unnecessary allocations)
- [x] Fast (uses ReadOnlySpan\<T> and ReadOnlyMemory\<T> to avoid copying data)
- [x] AOT compatible (no reflection)
- [x] DPS/HPS/APM calculations powered by Rx.NET
- [x] Compiles to native code (no .NET runtime required)

## Usage

### Console

To monitor logs in real-time, run the following command:
```bash
swtorlogparser.console.exe monitor
```

To list SWTOR log files, run the following command:

```bash
swtorlogparser.console.exe list
```