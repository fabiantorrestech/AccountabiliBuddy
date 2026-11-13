namespace AccountabiliBuddy.App.Services.Today;

/// <summary>
/// How the PC's Today page is arranged on this PC (document "today.layout", device-local, never synced, the same in both modes; a
/// device setting in backups). Only what is drawn: nothing here is a guard or a lock, so it is not locked during a session and changes
/// apply at once. Every part can be hidden (TD1), the session card and This session through <see cref="HiddenOutside"/> /
/// <see cref="HiddenInSession"/> (ending or stepping away stays in the title bar's session control); outside ProductaBuddy the lunch
/// part still can't be off (<see cref="TodayLayoutRules.Effective"/>).
/// </summary>
/// <param name="OutsideSession">The sidebar's parts in order with their places while no session runs; null for the defaults.</param>
/// <param name="InSession">The same while a session runs; null for the defaults.</param>
/// <param name="SidebarInSession">The sidebar during a session (default: hidden to the rail).</param>
/// <param name="SidebarOutside">The sidebar outside a session (default: remembered until the app restarts).</param>
/// <param name="ShowFinishedToday">Today's list shows the folded "Finished today" section (Options › Cards).</param>
/// <param name="ShowMoreToday">
/// The folded "More today" card under the session card (round 2 design fix: the boards draw nothing there, so it is off by default).
/// Off, the card is gone (TD1) and nothing else moves; only a profile's unlock offer still brings it back (a way out is never hidden).
/// Chosen in Today's Options and Settings › Today, on this PC only.
/// </param>
/// <param name="HiddenOutside">The main-column parts hidden while no session runs (TD1); none by default.</param>
/// <param name="HiddenInSession">The main-column parts hidden while a session runs (TD1); none by default.</param>
/// <param name="StepsMovedNote">The one-time "Steps now live in Goals" card (TD1); see <c>StepsMovedNote</c>.</param>
/// <param name="SidebarMinWidth">
/// Outside a session the sidebar rests on its rail while the window is narrower than this many DIP (board PC-Settings-Today, "Show it
/// when the window is at least 1280 px"); 0 = at any width.
/// </param>
public sealed record TodayLayoutPreferences(
    IReadOnlyList<TodayPartChoice>? OutsideSession = null,
    IReadOnlyList<TodayPartChoice>? InSession = null,
    TodaySidebarInSession SidebarInSession = TodaySidebarInSession.Hidden,
    TodaySidebarOutside SidebarOutside = TodaySidebarOutside.Remember,
    bool ShowFinishedToday = true,
    bool ShowMoreToday = false,
    int SidebarMinWidth = TodayLayoutPreferences.DefaultSidebarMinWidth,
    TodayMainParts HiddenOutside = TodayMainParts.None,
    TodayMainParts HiddenInSession = TodayMainParts.None,
    StepsMovedNote StepsMovedNote = StepsMovedNote.NotChecked)
{
    public const int DefaultSidebarMinWidth = 1280;

    /// <summary>The widths Settings › Today offers (0 = any width).</summary>
    public static IReadOnlyList<int> SidebarMinWidths { get; } = [0, 1024, 1280, 1440, 1600];

    public static TodayLayoutPreferences Default { get; } = new();

    /// <summary>The layout for a state of the day, as stored (null for the defaults).</summary>
    public IReadOnlyList<TodayPartChoice>? StoredFor(bool inSession) => inSession ? InSession : OutsideSession;

    /// <summary>The main-column parts hidden in a state of the day.</summary>
    public TodayMainParts HiddenFor(bool inSession) => inSession ? HiddenInSession : HiddenOutside;

    /// <summary>A copy with <paramref name="parts"/> as the layout of that state of the day.</summary>
    public TodayLayoutPreferences WithParts(bool inSession, IReadOnlyList<TodayPartChoice> parts) =>
        inSession ? this with { InSession = parts } : this with { OutsideSession = parts };
}
