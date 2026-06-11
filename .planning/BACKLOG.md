# Backlog

Items discovered outside the current milestone scope. Promote with `/gsd:review-backlog`.

## Open

### BL-01 — Overlay does not stay on top of the (windowed/borderless) game window
**Discovered:** 2026-06-11, during Phase 2 UAT (live overlay validation)
**Severity:** Medium (overlay's core purpose is to sit over the game)
**Scope:** Out of v1.0 hardening milestone (parser/monitor correctness, deps, CI) — this is overlay UX.

**Symptom:** With SWTOR in **Fullscreen (Windowed)/Borderless**, `SwtorLogParser.Overlay` drops behind the game window even though `ParserForm` sets `TopMost = true` ([SwtorLogParser.Overlay/ParserForm.cs:27](SwtorLogParser.Overlay/ParserForm.cs#L27)). (Exclusive fullscreen is unwinnable for a WinForms overlay and is a separate, expected limitation.)

**Likely cause:** A borderless `TopMost` WinForms form can lose top-of-z-order when another maximized/borderless window (the game) takes foreground; `TopMost=true` alone is not re-asserted.

**Proposed fix (windowed/borderless case):**
- Add `WS_EX_TOPMOST` (and consider `WS_EX_NOACTIVATE` / `WS_EX_TOOLWINDOW`) via overridden `CreateParams`.
- Re-assert with `SetWindowPos(Handle, HWND_TOPMOST, 0,0,0,0, SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE)` on a low-frequency timer and/or on a foreground-change hook (`SetWinEventHook` for `EVENT_SYSTEM_FOREGROUND`).
- Verify against SWTOR in Fullscreen (Windowed). Document that exclusive fullscreen is unsupported (use windowed/borderless).

**Note:** Phase 3 (RFCT-01) already touches the overlay's `View/` code (dedup) — this fix could ride alongside if promoted before/with Phase 3, but is not required by any v1.0 requirement.

## Promoted

(none yet)
