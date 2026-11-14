using Avalonia.Controls;
using Avalonia;
using Avalonia.Media;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Brushes for calendar event cards: the calendar's own colour, with white or near-black text, whichever has the higher WCAG contrast.</summary>
internal static class AgendaCardBrushes
{
    private static readonly Color Dark = Color.Parse("#1F1F1F");

    /// <summary>The card's background and text brushes for a calendar colour ("#RRGGBB"); the theme's surface and ink when the colour is not valid.</summary>
    public static (IBrush Background, IBrush Foreground) For(string? hex)
    {
        if (hex is not null && Color.TryParse(hex, out Color color))
        {
            Color opaque = Color.FromRgb(color.R, color.G, color.B);
            Color text = Contrast(opaque, Colors.White) >= Contrast(opaque, Dark) ? Colors.White : Dark;
            return (new SolidColorBrush(opaque), new SolidColorBrush(text));
        }

        return (ThemeBrush("Ab.Surface2", Brushes.LightGray), ThemeBrush("Ab.Ink", Brushes.Black));
    }

    private static IBrush ThemeBrush(string key, IBrush fallback)
    {
        Application? app = Application.Current;
        object? found = null;
        bool ok = app is not null && app.TryFindResource(key, app.ActualThemeVariant, out found);
        return ok && found is IBrush brush ? brush : fallback;
    }

    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a);
        double lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color color) =>
        0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

    private static double Linear(byte channel)
    {
        double value = channel / 255.0;
        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
