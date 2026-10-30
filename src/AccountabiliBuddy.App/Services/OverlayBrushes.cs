using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The theme's brushes (<c>Ab.*</c>, ADR 49) as the overlay's always-dark windows see them. A window's own resources can't be read from a
/// view model, so this looks them up in the application's, in the Dark variant; a theme change shows at the next refresh.
/// </summary>
public static class OverlayBrushes
{
    public static IBrush Get(string name)
    {
        if (Application.Current is { } app && app.TryGetResource("Ab." + name, ThemeVariant.Dark, out object? value) && value is IBrush brush)
        {
            return brush;
        }

        return Brushes.Gray;
    }

    public static Color GetColor(string name)
    {
        return Get(name) is ISolidColorBrush solid ? solid.Color : Colors.Gray;
    }
}
