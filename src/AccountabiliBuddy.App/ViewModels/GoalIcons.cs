using Avalonia.Media;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// A goal's or area's stored icon name (Material Symbols style, "fitness_center", as the phone keeps it) as a PC icon from the
/// Material icon library (ADR 32), and its user-picked colour as a brush. Goal colours tint the icon chip and the heatmap ramp, never
/// text on its own (brief §3.9). An unknown name falls back to the goals icon; an unreadable colour to the theme's accent.
/// </summary>
public static class GoalIcons
{
    /// <summary>The Goals rail item and page (brief §3.10: track_changes).</summary>
    public const MaterialIconKind Goals = MaterialIconKind.TrackChanges;

    private static readonly Dictionary<string, MaterialIconKind> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["work"] = MaterialIconKind.BriefcaseOutline,
        ["center_focus_strong"] = MaterialIconKind.TimerOutline,
        ["timer"] = MaterialIconKind.TimerOutline,
        ["flag"] = MaterialIconKind.FlagOutline,
        ["fitness_center"] = MaterialIconKind.Dumbbell,
        ["monitor_weight"] = MaterialIconKind.ScaleBathroom,
        ["scale"] = MaterialIconKind.ScaleBathroom,
        ["directions_run"] = MaterialIconKind.Run,
        ["footprint"] = MaterialIconKind.ShoePrint,
        ["steps"] = MaterialIconKind.ShoePrint,
        ["school"] = MaterialIconKind.SchoolOutline,
        ["menu_book"] = MaterialIconKind.BookOpenVariant,
        ["book"] = MaterialIconKind.BookOutline,
        ["translate"] = MaterialIconKind.Translate,
        ["home"] = MaterialIconKind.HomeOutline,
        ["self_improvement"] = MaterialIconKind.Meditation,
        ["water_drop"] = MaterialIconKind.Water,
        ["bedtime"] = MaterialIconKind.BedOutline,
        ["code"] = MaterialIconKind.CodeTags,
        ["music_note"] = MaterialIconKind.Music,
        ["palette"] = MaterialIconKind.Palette,
        ["savings"] = MaterialIconKind.CashMultiple,
        ["track_changes"] = MaterialIconKind.TrackChanges,
    };

    /// <summary>The PC icon for a stored icon name.</summary>
    public static MaterialIconKind Of(string? name)
    {
        if (name is null)
        {
            return Goals;
        }

        if (Known.TryGetValue(name, out MaterialIconKind known))
        {
            return known;
        }

        // A Material name the library also has ("dumbbell", "book_outline"): PascalCase it.
        string pascal = string.Concat(name.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
        return Enum.TryParse(pascal, ignoreCase: true, out MaterialIconKind parsed) ? parsed : Goals;
    }

    /// <summary>The theme's accent (the fallback for a colour that can't be read).</summary>
    public static IBrush ThemeAccent()
    {
        Avalonia.Application? app = Avalonia.Application.Current;
        return app is not null && app.TryGetResource("Ab.Accent", app.ActualThemeVariant, out object? value) && value is IBrush brush
            ? brush
            : Brushes.Teal;
    }

    /// <summary>The goal's colour, or the theme's accent when it can't be read.</summary>
    public static IBrush Brush(string? colour) => Brush(colour, ThemeAccent());

    /// <summary>The goal's colour, or <paramref name="fallback"/> when it can't be read.</summary>
    public static IBrush Brush(string? colour, IBrush fallback)
    {
        return colour is not null && Color.TryParse(colour, out Color parsed) ? new SolidColorBrush(parsed) : fallback;
    }

    /// <summary>The goal's colour at a low opacity: the icon chip's tint.</summary>
    public static IBrush SoftBrush(string? colour, IBrush fallback)
    {
        return colour is not null && Color.TryParse(colour, out Color parsed) ? new SolidColorBrush(parsed, 0.14) : fallback;
    }
}
