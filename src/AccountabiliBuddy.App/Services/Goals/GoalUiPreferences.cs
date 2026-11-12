namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// The Goals page's own UI state on this PC (document "goals.ui", device-local, never synced, a device setting in backups): which
/// areas are folded, the list pane's width and the list view. It only changes what the page draws, so it applies at once.
/// </summary>
/// <param name="CollapsedAreas">Ids of the folded areas (an area's <c>collapsed</c> is per device, Q369).</param>
/// <param name="ListWidth">The list pane's width in px (360 to 560, default 470; brief §4.4).</param>
/// <param name="ListView">By area, Current or All.</param>
/// <param name="Sort">Your order (default) or Behind first, inside each group.</param>
public sealed record GoalUiPreferences(IReadOnlyList<Guid>? CollapsedAreas, int ListWidth, GoalListView ListView, GoalListSort Sort = GoalListSort.YourOrder)
{
    public const int MinListWidth = 360;
    public const int MaxListWidth = 560;
    public const int DefaultListWidth = 470;

    public static GoalUiPreferences Default { get; } = new([], DefaultListWidth, GoalListView.ByArea);

    /// <summary>The same preferences with a width in range, a known view and no repeated area.</summary>
    public GoalUiPreferences Clamped()
    {
        return this with
        {
            CollapsedAreas = (CollapsedAreas ?? []).Distinct().ToList(),
            ListWidth = ListWidth <= 0 ? DefaultListWidth : Math.Clamp(ListWidth, MinListWidth, MaxListWidth),
            ListView = Enum.IsDefined(ListView) ? ListView : GoalListView.ByArea,
            Sort = Enum.IsDefined(Sort) ? Sort : GoalListSort.YourOrder,
        };
    }
}
