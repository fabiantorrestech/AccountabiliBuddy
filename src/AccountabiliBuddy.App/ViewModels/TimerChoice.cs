using AccountabiliBuddy.App.Services;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A profile's on-screen timer choice as a list index: follow the default, or always on or off.</summary>
public static class TimerChoice
{
    public const int UseDefault = 0;
    public const int On = 1;
    public const int Off = 2;

    /// <summary>In index order.</summary>
    public static IReadOnlyList<string> Names { get; } = new[] { "Use default", "On", "Off" };

    public static int IndexOf(ProfileOverlay overlay)
    {
        return overlay.UseDefault ? UseDefault : overlay.Enabled ? On : Off;
    }

    public static ProfileOverlay ToOverlay(int index, string? color)
    {
        return new ProfileOverlay(index == On, color, index == UseDefault);
    }
}
