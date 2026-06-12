using System.Text.Json.Serialization;

namespace SwtorLogParser.Overlay.WinUi.Settings;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for <see cref="OverlaySettings"/> (D-05).
///
/// Using the System.Text.Json source generator keeps serialization reflection-free. The overlay host
/// is JIT (not AOT), but the reflection-free path is the project-wide convention (the core library is
/// <c>IsAotCompatible</c>) and avoids pulling the reflection-based serializer into the overlay graph.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(OverlaySettings))]
internal partial class OverlaySettingsContext : JsonSerializerContext
{
}
