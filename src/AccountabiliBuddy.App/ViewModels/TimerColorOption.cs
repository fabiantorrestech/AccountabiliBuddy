using AccountabiliBuddy.App.Services;
using Avalonia.Media;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A colour choice for a profile's on-screen timer.</summary>
/// <param name="Hex">"#RRGGBB"; null = the profile's own colour on the Schedule grid.</param>
public sealed record TimerColorOption(string Name, string? Hex, IBrush Swatch)
{
    private static readonly (string Name, string Hex)[] Fixed =
    {
        ("Teal", "#0F766E"),
        ("Blue", "#2563EB"),
        ("Purple", "#9333EA"),
        ("Pink", "#DB2777"),
        ("Red", "#DC2626"),
        ("Orange", "#C2410C"),
        ("Amber", "#D97706"),
        ("Green", "#4D7C0F"),
        ("Slate", "#475569"),
        ("White", "#F5F5F5"),
    };

    /// <summary>"Profile colour" first, then the fixed colours.</summary>
    public static IReadOnlyList<TimerColorOption> For(Guid profileId)
    {
        var options = new List<TimerColorOption> { new("Profile colour", null, new SolidColorBrush(ProfileColors.For(profileId))) };
        options.AddRange(Fixed.Select(color => new TimerColorOption(color.Name, color.Hex, new SolidColorBrush(Color.Parse(color.Hex)))));
        return options;
    }

    /// <summary>For the default timer: "Each profile's colour" first, then the fixed colours.</summary>
    public static IReadOnlyList<TimerColorOption> ForDefault()
    {
        var options = new List<TimerColorOption> { new("Each profile's colour", null, new SolidColorBrush(ProfileColors.For(Guid.Empty))) };
        options.AddRange(Fixed.Select(color => new TimerColorOption(color.Name, color.Hex, new SolidColorBrush(Color.Parse(color.Hex)))));
        return options;
    }

    /// <summary>The option with this colour, or "Profile colour" when none has it.</summary>
    public static TimerColorOption Match(IReadOnlyList<TimerColorOption> options, string? hex)
    {
        return options.FirstOrDefault(option => string.Equals(option.Hex, hex, StringComparison.OrdinalIgnoreCase)) ?? options[0];
    }
}
