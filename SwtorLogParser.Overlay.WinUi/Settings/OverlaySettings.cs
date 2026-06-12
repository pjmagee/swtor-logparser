namespace SwtorLogParser.Overlay.WinUi.Settings;

/// <summary>
/// Serializable, WinUI-free settings model persisted to
/// <c>%LocalAppData%\SwtorLogParser\settings.json</c> (D-05).
///
/// Carries the overlay window placement (position + size) and the chosen row/header font size.
/// Per D-04 this phase persists <b>position + size + font only</b> — opacity persistence is Phase 10.
/// An <see cref="Opacity"/> field is reserved for forward-compatibility but is NOT applied this phase.
///
/// Window placement values are nullable: <c>null</c> means "no saved value yet → use the window's
/// default placement" (first run, or a partially-written file). Coordinates are plain integers matching
/// WinUI's <c>AppWindow</c> <c>PointInt32</c> / <c>SizeInt32</c>; they are NEVER used to build a file path
/// (T-09-05).
/// </summary>
public sealed class OverlaySettings
{
    /// <summary>Sensible default font size used when no value has been persisted.</summary>
    public const double DefaultFontSize = 14d;

    /// <summary>Saved window left (X), in screen pixels. Null → use default placement.</summary>
    public int? WindowX { get; set; }

    /// <summary>Saved window top (Y), in screen pixels. Null → use default placement.</summary>
    public int? WindowY { get; set; }

    /// <summary>Saved window width, in pixels. Null → use default placement.</summary>
    public int? WindowWidth { get; set; }

    /// <summary>Saved window height, in pixels. Null → use default placement.</summary>
    public int? WindowHeight { get; set; }

    /// <summary>Persisted row + header font size (OVL-08). Defaults to <see cref="DefaultFontSize"/>.</summary>
    public double FontSize { get; set; } = DefaultFontSize;

    /// <summary>
    /// Reserved for Phase 10 (opacity control + persistence, D-04). Carried for forward-compat so an
    /// older settings file round-trips cleanly once Phase 10 starts writing it; NOT applied this phase.
    /// </summary>
    public double? Opacity { get; set; }
}
