using Avalonia.Media;

namespace AccountabiliBuddy.App.Services;

/// <summary>Stable color per profile, used by the on-screen timer. Mid-tone colors so white text stays readable.</summary>
public static class ProfileColors
{
    private static readonly Color[] Palette =
    {
        Color.FromRgb(15, 118, 110),  // teal
        Color.FromRgb(37, 99, 235),   // blue
        Color.FromRgb(147, 51, 234),  // purple
        Color.FromRgb(219, 39, 119),  // pink
        Color.FromRgb(194, 65, 12),   // orange
        Color.FromRgb(77, 124, 15),   // green
        Color.FromRgb(161, 98, 7),    // amber
        Color.FromRgb(71, 85, 105),   // slate
    };

    public static Color For(Guid profileId)
    {
        int index = (int)((uint)profileId.GetHashCode() % (uint)Palette.Length);
        return Palette[index];
    }
}
