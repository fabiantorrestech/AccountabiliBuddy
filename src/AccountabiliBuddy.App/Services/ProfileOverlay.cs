namespace AccountabiliBuddy.App.Services;

/// <summary>A profile's on-screen timer: its own choice (shown or not, and its colour), or the default's.</summary>
/// <param name="Color">"#RRGGBB"; null = the profile's colour on the Schedule grid.</param>
/// <param name="UseDefault">Follows <see cref="OverlaySettings.DefaultTimer"/>; <paramref name="Enabled"/> and
/// <paramref name="Color"/> are then only kept for when the profile gets its own choice again.</param>
public sealed record ProfileOverlay(bool Enabled, string? Color, bool UseDefault = false)
{
    public static ProfileOverlay Off { get; } = new(false, null);

    public static ProfileOverlay FollowDefault { get; } = new(false, null, UseDefault: true);
}
