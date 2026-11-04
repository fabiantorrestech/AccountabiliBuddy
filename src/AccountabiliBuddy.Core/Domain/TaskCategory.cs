using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>A named group of a guided session's tasks (e.g. Work, Personal), with its colour; the checklist is grouped under them.</summary>
/// <param name="Color">"#RRGGBB": any colour since the colour editor; new categories start on one of <see cref="Palette"/>.</param>
/// <param name="ColorEditedAt">When the user last chose its colour; the newest choice wins on every device (ADR 62). Null: not recoloured since that came in.</param>
public sealed record TaskCategory(
    Guid Id,
    string Name,
    string Color,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ColorEditedAt = null)
{
    /// <summary>The 8 colours a new category starts on, distinct in light and dark themes and on the on-screen window.</summary>
    public static IReadOnlyList<string> Palette { get; } =
    [
        "#2563EB", "#16A34A", "#DB2777", "#EA580C", "#7C3AED", "#0D9488", "#CA8A04", "#DC2626",
    ];

    /// <summary>The colour editor's 24 presets, starting with <see cref="Palette"/>; the phone's list is the same.</summary>
    public static IReadOnlyList<string> Presets { get; } =
    [
        .. Palette,
        "#0EA5E9", "#06B6D4", "#22C55E", "#84CC16", "#EAB308", "#F97316", "#EF4444", "#F43F5E",
        "#EC4899", "#D946EF", "#A855F7", "#6366F1", "#14B8A6", "#92400E", "#64748B", "#334155",
    ];
}
