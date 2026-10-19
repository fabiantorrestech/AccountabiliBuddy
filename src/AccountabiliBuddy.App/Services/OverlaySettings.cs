namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The on-screen session timer and all-day timeline, which share one small window. App-owned (not part of the
/// shared profile schema) and display only, so changing it never loosens anything. Profiles not listed in
/// <see cref="Profiles"/> follow <see cref="DefaultTimer"/>.
/// </summary>
/// <param name="Opacity">0.2–1.</param>
/// <param name="X">Saved left edge on the virtual desktop in pixels; null = top-right of the main monitor.</param>
/// <param name="Y">Saved top edge in pixels; null with <paramref name="X"/>.</param>
/// <param name="Divisions">Marks splitting the bar or ring into equal parts (4 = quarters); 0 = none.</param>
/// <param name="WrapUpColor">Turns amber near the end of a focus phase (last 5 minutes, or last fifth if shorter).</param>
/// <param name="PulseAtMarks">One soft brighten when a mark is passed.</param>
/// <param name="HideInFullscreen">Hidden while a fullscreen app (video, presentation, game) is in front.</param>
/// <param name="FillUp">The bar and ring fill up with time passed instead of emptying from full.</param>
/// <param name="DefaultTimer">The timer for profiles set to "Use default" (its UseDefault is ignored); null only
/// in settings saved before there was a default.</param>
/// <param name="ShowTimeline">The all-day timeline shows on days with focus blocks.</param>
/// <param name="TimelineInFocus">Whether the timeline makes room for the session timer.</param>
/// <param name="TimelineShowsTime">The timeline shows the current time next to the next block.</param>
/// <param name="QuickHideSeconds">How long the Ctrl+Alt "Hide" button hides the window.</param>
public sealed record OverlaySettings(
    OverlayStyle Style,
    double Opacity,
    OverlaySize Size,
    int? X,
    int? Y,
    IReadOnlyDictionary<Guid, ProfileOverlay> Profiles,
    int Divisions = 0,
    bool WrapUpColor = true,
    bool PulseAtMarks = true,
    bool HideInFullscreen = true,
    bool FillUp = false,
    ProfileOverlay? DefaultTimer = null,
    bool ShowTimeline = true,
    TimelineInFocus TimelineInFocus = TimelineInFocus.SwapForTimer,
    bool TimelineShowsTime = false,
    int QuickHideSeconds = 15)
{
    public const double MinOpacity = 0.2;
    public const int MinQuickHideSeconds = 3;
    public const int MaxQuickHideSeconds = 600;

    public static OverlaySettings Default { get; } = new(
        OverlayStyle.BarWithEndTime, 0.85, OverlaySize.Medium, null, null, new Dictionary<Guid, ProfileOverlay>(),
        DefaultTimer: ProfileOverlay.Off);

    /// <summary>The profile's own choice, or "Use default" when it has none.</summary>
    public ProfileOverlay SavedFor(Guid profileId)
    {
        return Profiles.TryGetValue(profileId, out ProfileOverlay? overlay) ? overlay : ProfileOverlay.FollowDefault;
    }

    /// <summary>What the profile's timer actually does: its own choice, or the default's.</summary>
    public ProfileOverlay For(Guid? profileId)
    {
        if (profileId is not Guid id)
        {
            return ProfileOverlay.Off;
        }

        ProfileOverlay saved = SavedFor(id);
        return saved.UseDefault ? (DefaultTimer ?? ProfileOverlay.Off) with { UseDefault = false } : saved;
    }
}
