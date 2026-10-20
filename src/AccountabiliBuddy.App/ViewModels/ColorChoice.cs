using Avalonia.Media;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A colour a day-plan category can have, with its swatch.</summary>
/// <param name="Hex">"#RRGGBB".</param>
public sealed record ColorChoice(string Hex)
{
    /// <summary>The starting categories' colours and a few more, distinct in light and dark themes.</summary>
    public static IReadOnlyList<ColorChoice> Palette { get; } = new[]
    {
        "#2563EB", "#7C3AED", "#CA8A04", "#0D9488", "#16A34A", "#334155",
        "#DB2777", "#EA580C", "#0891B2", "#65A30D", "#9333EA", "#DC2626",
    }.Select(hex => new ColorChoice(hex)).ToList();

    public IBrush Brush { get; } = new SolidColorBrush(Color.Parse(Hex));
}
