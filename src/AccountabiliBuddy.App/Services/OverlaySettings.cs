namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The on-screen session timer and all-day timeline, which share one small window. App-owned (not part of the
/// shared profile schema) and display only, so changing it never loosens anything. Profiles not listed in
/// <see cref="Profiles"/> follow <see cref="DefaultTimer"/>.
/// </summary>
/// <param name="Opacity">0.2–1.</param>
/// <param name="X">Saved left edge on the virtual desktop in pixels; null = the chosen corner (<paramref name="Corner"/>).</param>
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
/// <param name="MoveForMaximizedApps">In its default corner, slide in from the corner while a maximized app (title bar,
/// not F11) is in front on that screen, so the app's buttons and bars stay clickable.</param>
/// <param name="MaximizedAppDrop">How far it slides in vertically (down from a top corner, up from a bottom one), in
/// display pixels at 100% scaling, for apps without their own distance.</param>
/// <param name="AppDrops">Each app's own vertical distance in from the corner (process name, lowercase → display
/// pixels; 0 = don't move), set by Ctrl+Alt-dragging the timer while that app is maximized, or in Settings.</param>
/// <param name="AppShifts">Each app's own sideways distance in from the corner (process name, lowercase → display
/// pixels), e.g. to clear a browser's side panel; apps without one stay at the screen edge.</param>
/// <param name="Corner">The corner it sits in by default; bottom corners stay above the taskbar while it shows.</param>
/// <param name="Quiet">At rest it's muted (60%, slate colours, red marks kept); full colour while Ctrl+Alt is held and for
/// loud moments (the last 2 minutes, time up). On by default; settings saved before it existed read as on.</param>
/// <param name="QuietMarks">In quiet mode, the timeline's red (drift marks, the now line) and task marks are dulled at
/// rest too, instead of staying in full colour. On by default.</param>
/// <param name="TaskOnlyWithCtrlAlt">The current task line shows only while Ctrl+Alt is held. Off by default.</param>
/// <param name="HeldOpacity">How opaque the whole window (background, task line and all) is while Ctrl+Alt is held (0.2 to 1).</param>
/// <param name="TimelineOpacity">The timeline's opacity while no session runs (0.2 to 1); in a session <paramref name="Opacity"/> applies.</param>
/// <param name="TimelineHeldOpacity">The same while Ctrl+Alt is held (0.2 to 1); in a session <paramref name="HeldOpacity"/> applies.</param>
/// <param name="Placement">Where the overlay lives (ADR 51); null in settings saved before it existed, which <see cref="OverlaySettingsService"/> fills in from the old corner.</param>
/// <param name="AutoHide">What it does while the user's taskbar hides itself.</param>
/// <param name="LeftAligned">Where it goes on a taskbar whose icons start at the left.</param>
/// <param name="CornerClock">The small clock in the top right corner while an app is full screen.</param>
/// <param name="CornerClockParts">What the corner clock shows.</param>
/// <param name="Shape">Rounded rectangle or pill.</param>
/// <param name="Progress">A ring or a timeline.</param>
/// <param name="ContentOrder">What it shows and in what order (null = the default order).</param>
/// <param name="PanelSize">The Ctrl+Alt panel's width.</param>
/// <param name="HoldToPeek">Holding Ctrl+Alt for 300 ms opens the panel while held (off for layouts where AltGr is Ctrl+Alt).</param>
/// <param name="PanelSticky">Ctrl+Alt+Space keeps the panel open until Esc or a click elsewhere.</param>
/// <param name="ShowSeconds">The "18:42" style: seconds in the overlay's countdown (whole minutes otherwise).</param>
/// <param name="AppRules">The per-app rules (null = the built-in list).</param>
/// <param name="SideTileY">Where the vertical tile was dragged to along the screen edge (physical pixels from the top); null = the middle.</param>
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
    int QuickHideSeconds = 15,
    bool MoveForMaximizedApps = true,
    int MaximizedAppDrop = OverlaySettings.BottomCornerDrop,
    IReadOnlyDictionary<string, int>? AppDrops = null,
    IReadOnlyDictionary<string, int>? AppShifts = null,
    OverlayCorner Corner = OverlayCorner.BottomRight,
    bool Quiet = true,
    bool QuietMarks = true,
    bool TaskOnlyWithCtrlAlt = false,
    double HeldOpacity = 1.0,
    double TimelineOpacity = OverlaySettings.DefaultOpacity,
    double TimelineHeldOpacity = 1.0,
    OverlayPlacement? Placement = null,
    AutoHideMode AutoHide = AutoHideMode.Float,
    LeftAlignedMode LeftAligned = LeftAlignedMode.BesideTray,
    bool CornerClock = true,
    CornerClockParts CornerClockParts = CornerClockParts.Session | CornerClockParts.Time,
    OverlayShape Shape = OverlayShape.Rounded,
    OverlayProgress Progress = OverlayProgress.Ring,
    IReadOnlyList<OverlayItem>? ContentOrder = null,
    PanelSize PanelSize = PanelSize.Standard,
    bool HoldToPeek = true,
    bool PanelSticky = true,
    bool ShowSeconds = false,
    IReadOnlyList<OverlayAppRule>? AppRules = null,
    int? SideTileY = null)
{
    /// <summary>Default vertical distance for maximized apps from a top corner (clears tabs, toolbars, window buttons).</summary>
    public const int TopCornerDrop = 110;

    /// <summary>
    /// Default vertical distance from a bottom corner: none. Apps rarely have buttons there, so a maximized app only moves
    /// it once that app has its own spot.
    /// </summary>
    public const int BottomCornerDrop = 0;

    public bool IsTopCorner => Corner is OverlayCorner.TopRight or OverlayCorner.TopLeft;

    public const int MaxAppShift = 2000;
    public const int MaxMaximizedAppDrop = 600;
    public const double MinOpacity = 0.2;
    public const double DefaultOpacity = 0.85;
    public const int MinQuickHideSeconds = 3;
    public const int MaxQuickHideSeconds = 600;

    /// <summary>Where the overlay lives: the saved choice, else over the taskbar.</summary>
    public OverlayPlacement EffectivePlacement => Placement ?? OverlayPlacement.OverTaskbar;

    /// <summary>What the overlay shows and in what order, with every part listed once.</summary>
    public IReadOnlyList<OverlayItem> Items => OverlayContentOrder.Complete(ContentOrder);

    /// <summary>The per-app rules in force: the user's list, else the built-in one.</summary>
    public IReadOnlyList<OverlayAppRule> Rules => AppRules ?? OverlayAppRules.Defaults;

    public static OverlaySettings Default { get; } = new(
        OverlayStyle.BarWithEndTime, DefaultOpacity, OverlaySize.Medium, null, null, new Dictionary<Guid, ProfileOverlay>(),
        DefaultTimer: new ProfileOverlay(true, null));

    /// <summary>How far to slide down for this maximized app: its own distance, or the default.</summary>
    public int DropFor(string processName)
    {
        return AppDrops is not null && AppDrops.TryGetValue(processName.ToLowerInvariant(), out int drop) ? drop : MaximizedAppDrop;
    }

    /// <summary>True when this app has its own distances (set by dragging or in Settings).</summary>
    public bool HasOwnOffset(string processName)
    {
        string app = processName.ToLowerInvariant();
        return (AppDrops?.ContainsKey(app) ?? false) || (AppShifts?.ContainsKey(app) ?? false);
    }

    /// <summary>How far to slide left for this maximized app (0 unless it has its own distance).</summary>
    public int ShiftFor(string processName)
    {
        return AppShifts is not null && AppShifts.TryGetValue(processName.ToLowerInvariant(), out int shift) ? shift : 0;
    }

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
