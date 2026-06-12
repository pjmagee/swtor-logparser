# Phase 09 — Deferred Items

Out-of-scope discoveries logged during execution. NOT fixed (per executor SCOPE BOUNDARY:
only auto-fix issues directly caused by the current task's changes).

## Pre-existing WinForms overlay warnings (SwtorLogParser.Overlay/ParserForm.cs)

Surfaced by `dotnet build SwtorLogParser.slnx -c Release`. These are in the legacy WinForms
overlay (the parity baseline that must NOT be modified this milestone until the Phase 10/11
parity gate), unrelated to Plan 09-01's WinUI changes:

- `ParserForm.cs(140,18): CS0108` — `MouseDown` hides inherited `Control.MouseDown`.
- `ParserForm.cs(120,68) / (121,55) / (126,68) / (127,55): CS8602` — possible null dereference
  in the font +/- button handlers.

Disposition: leave untouched. The WinForms overlay is deleted at the Phase 10/11 parity gate
(per ARCHITECTURE.md Anti-Pattern 5 / build-before-delete), at which point these vanish.

## AOT publish native-link step fails in this shell (environment, not a code regression)

`dotnet publish SwtorLogParser.Native.Cli -c Release` reaches the native link step and fails:
`MSB3073 ... 'vswhere.exe' is not recognized ... link.exe ... exited with code 123`.

This is a toolchain/PATH issue (the MSVC C++ linker / vswhere are not on this shell's PATH),
NOT an AOT-graph contamination. The managed IL-compilation phase produced **zero IL2xxx/IL3xxx
warnings**, which is exactly what the Plan 09-01 AOT regression gate checks — confirming the new
overlay→core ProjectReference did not contaminate the core's AOT/trim graph. Re-run from a
Visual Studio Developer prompt (or with the C++ build tools on PATH) to complete the native link.

Disposition: the AOT-contamination gate is satisfied (no IL warnings). The native-link failure
is an environment limitation to confirm in a Developer Command Prompt; not a Plan 09-01 regression.
