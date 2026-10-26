namespace AccountabiliBuddy.Core.Domain;

/// <summary>A named group of a guided session's tasks (e.g. Work, Personal), with its colour; the checklist is grouped under them.</summary>
/// <param name="Color">"#RRGGBB", one of <see cref="Palette"/>.</param>
public sealed record TaskCategory(Guid Id, string Name, string Color)
{
    /// <summary>The 8 colours a category can have, distinct in light and dark themes and on the on-screen window.</summary>
    public static IReadOnlyList<string> Palette { get; } =
    [
        "#2563EB", "#16A34A", "#DB2777", "#EA580C", "#7C3AED", "#0D9488", "#CA8A04", "#DC2626",
    ];
}
