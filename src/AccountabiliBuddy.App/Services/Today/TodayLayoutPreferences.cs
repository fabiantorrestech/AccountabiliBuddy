namespace AccountabiliBuddy.App.Services.Today;

/// <summary>
/// How the PC's Today page is arranged on this PC (document "today.layout", device-local, never synced, the same in both modes; a
/// device setting in backups). Only what is drawn: the session card (or start card) and This session are not in it and always show,
/// and nothing here is a guard or a lock, so it is not locked during a session and changes apply at once. Outside ProductaBuddy the
/// lunch part can't be off (<see cref="TodayLayoutRules.Effective"/>).
/// </summary>
/// <param name="OutsideSession">The sidebar's parts in order with their places while no session runs; null for the defaults.</param>
/// <param name="InSession">The same while a session runs; null for the defaults.</param>
/// <param name="SidebarInSession">The sidebar during a session (default: hidden to the rail).</param>
/// <param name="SidebarOutside">The sidebar outside a session (default: remembered until the app restarts).</param>
/// <param name="ShowFinishedToday">Today's list shows the folded "Finished today" section (Options › Cards).</param>
public sealed record TodayLayoutPreferences(
    IReadOnlyList<TodayPartChoice>? OutsideSession = null,
    IReadOnlyList<TodayPartChoice>? InSession = null,
    TodaySidebarInSession SidebarInSession = TodaySidebarInSession.Hidden,
    TodaySidebarOutside SidebarOutside = TodaySidebarOutside.Remember,
    bool ShowFinishedToday = true)
{
    public static TodayLayoutPreferences Default { get; } = new();

    /// <summary>The layout for a state of the day, as stored (null for the defaults).</summary>
    public IReadOnlyList<TodayPartChoice>? StoredFor(bool inSession) => inSession ? InSession : OutsideSession;

    /// <summary>A copy with <paramref name="parts"/> as the layout of that state of the day.</summary>
    public TodayLayoutPreferences WithParts(bool inSession, IReadOnlyList<TodayPartChoice> parts) =>
        inSession ? this with { InSession = parts } : this with { OutsideSession = parts };
}
