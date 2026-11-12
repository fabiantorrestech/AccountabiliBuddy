namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// How the Tasks page and Today's task sections are laid out on this PC (document "tasks.layout", device-local, the same in both
/// modes): row density, each panel's Expand all / Collapse all, the Today sections pinned open and the order of the Shelf's columns
/// per grouping. Only how things look: nothing here loosens a session, so changes apply at once.
/// </summary>
/// <param name="Compact">Compact rows (32 DIP) instead of Normal (44+).</param>
/// <param name="TodaySplit">
/// The old Today / Shelf split in percent. Unused since round 2 R3 (the Tasks page has no Today column); kept so a stored document and a
/// backup read the same.
/// </param>
/// <param name="ExpandAll">Per panel ("today", "todaytab", "shelf"): true after Expand all, false after Collapse all; absent for neither yet.</param>
/// <param name="Pinned">The Today sections pinned open ("session", "queued", "reminders", "done"); null for the default (This session).</param>
/// <param name="ColumnOrder">
/// Round 2 R3: the Shelf's columns left to right, per grouping ("bins", "label", "tag"), as group keys ("bin:next", "label:…"). This PC
/// only, never in a view or a sync file (plan §3b item 3; a shared page order waits for ADR 83). Absent: the grouping's own order.
/// </param>
public sealed record TaskLayoutPreferences(
    bool Compact = false,
    int TodaySplit = TaskLayoutPreferences.DefaultSplit,
    IReadOnlyDictionary<string, bool>? ExpandAll = null,
    IReadOnlyList<string>? Pinned = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? ColumnOrder = null)
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

    /// <summary>The stored column order of a grouping ("bins", "label", "tag"); null when the user never moved a column there.</summary>
    public IReadOnlyList<string>? ColumnOrderOf(string grouping) =>
        ColumnOrder is not null && ColumnOrder.TryGetValue(grouping, out IReadOnlyList<string>? order) ? order : null;
}
