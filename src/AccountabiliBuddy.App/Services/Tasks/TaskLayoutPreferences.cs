namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// How the Tasks page and Today's task sections are laid out on this PC (document "tasks.layout", device-local, the same in both
/// modes): row density, the Today / Shelf split, each panel's Expand all / Collapse all and the Today sections pinned open. Only how
/// things look: nothing here loosens a session, so changes apply at once.
/// </summary>
/// <param name="Compact">Compact rows (32 DIP) instead of Normal (44+).</param>
/// <param name="TodaySplit">Today's share of the page in percent while the Shelf shows (<see cref="MinSplit"/> to <see cref="MaxSplit"/>).</param>
/// <param name="ExpandAll">Per panel ("today", "todaytab", "shelf"): true after Expand all, false after Collapse all; absent for neither yet.</param>
/// <param name="Pinned">The Today sections pinned open ("session", "queued", "reminders", "done"); null for the default (This session).</param>
public sealed record TaskLayoutPreferences(
    bool Compact = false,
    int TodaySplit = TaskLayoutPreferences.DefaultSplit,
    IReadOnlyDictionary<string, bool>? ExpandAll = null,
    IReadOnlyList<string>? Pinned = null)
{
    public const int DefaultSplit = 46;
    public const int MinSplit = 30;
    public const int MaxSplit = 70;

    public static TaskLayoutPreferences Default { get; } = new();

    /// <summary>The sections pinned open now: This session unless the user changed the pins.</summary>
    public IReadOnlyList<string> PinnedNow => Pinned ?? ["session"];

    public int SplitNow => Math.Clamp(TodaySplit, MinSplit, MaxSplit);

    /// <summary>The panel's last Expand all (true) or Collapse all (false); null when neither was used.</summary>
    public bool? ExpandAllOf(string panel) => ExpandAll is not null && ExpandAll.TryGetValue(panel, out bool expanded) ? expanded : null;
}
