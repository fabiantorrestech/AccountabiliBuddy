namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// The five goal lists as they travel (ADR 79) and merge (<see cref="GoalSyncMerge"/>): areas, goals, entries, reminders and weekly
/// reviews, tombstones included. Each list may carry items of a newer app kept as written (<see cref="GoalNewerJson.NewerOf{T}"/>), which
/// take part in the merge by id and stamps and are written back.
/// </summary>
public sealed record GoalSyncLists(
    IReadOnlyList<GoalArea> Areas,
    IReadOnlyList<Goal> Goals,
    IReadOnlyList<GoalEntry> Entries,
    IReadOnlyList<GoalReminder> Reminders,
    IReadOnlyList<GoalReviewRecord> Reviews)
{
    public static GoalSyncLists Empty { get; } = new([], [], [], [], []);
}
