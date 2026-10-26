using Avalonia.Media;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>The brush for a task category's colour ("#RRGGBB"), one per colour; a missing or bad colour is transparent.</summary>
public static class TaskCategoryBrushes
{
    private static readonly Dictionary<string, IBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static IBrush Of(string? hex)
    {
        if (hex is null)
        {
            return Brushes.Transparent;
        }

        if (!Cache.TryGetValue(hex, out IBrush? brush))
        {
            brush = Color.TryParse(hex, out Color color) ? new SolidColorBrush(color) : Brushes.Transparent;
            Cache[hex] = brush;
        }

        return brush;
    }
}
