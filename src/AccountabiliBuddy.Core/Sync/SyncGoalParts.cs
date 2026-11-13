using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The five goal parts of a sync file (ADR 79): what goes in (<see cref="Into"/>) and what is taken from one (<see cref="From"/>). A
/// waiting change never travels: left out on the way out and ignored on the way in. A tombstone goes without text or unknown fields.
/// Deep work's record goes composed (<see cref="GoalDeepWorkSync.Compose"/>) by the caller before it gets here.
/// </summary>
public static class SyncGoalParts
{
    /// <summary><paramref name="file"/> carrying <paramref name="lists"/> as its goal parts (newer items written back at their places).</summary>
    public static SyncFile Into(SyncFile file, GoalSyncLists lists)
    {
        GoalSyncLists outgoing = Cleaned(lists);
        return file with
        {
            Goals = outgoing.Goals,
            GoalAreas = outgoing.Areas,
            GoalEntries = outgoing.Entries,
            GoalReminders = outgoing.Reminders,
            GoalReviews = outgoing.Reviews,
        };
    }

    /// <summary>
    /// The goal lists <paramref name="file"/> carries, without waiting changes; null when it carries no goal part at all (an older app, a
    /// QR file, task lists not shared): the receiver then keeps its own goals unchanged. A missing part of a file that has others is empty.
    /// </summary>
    public static GoalSyncLists? From(SyncFile file)
    {
        if (file.Goals is null && file.GoalAreas is null && file.GoalEntries is null && file.GoalReminders is null && file.GoalReviews is null)
        {
            return null;
        }

        return Cleaned(new GoalSyncLists(file.GoalAreas ?? [], file.Goals ?? [], file.GoalEntries ?? [], file.GoalReminders ?? [], file.GoalReviews ?? []));
    }

    // Only usable items (GoalSyncClean); no waiting change anywhere; tombstones without text or unknown fields; the lists' newer items kept.
    private static GoalSyncLists Cleaned(GoalSyncLists given)
    {
        GoalSyncLists lists = GoalSyncClean.Lists(given);
        return new GoalSyncLists(
            Each(lists.Areas, GoalTombstones.Clean),
            Each(lists.Goals, goal => GoalTombstones.Clean(GoalSyncMerge.WithoutPending(goal))),
            Each(lists.Entries, GoalTombstones.Clean),
            Each(lists.Reminders, GoalTombstones.Clean),
            Each(lists.Reviews, GoalTombstones.Clean));
    }

    private static IReadOnlyList<T> Each<T>(IReadOnlyList<T> list, Func<T, T> clean)
    {
        return GoalNewerJson.WithNewer(list.Select(clean).ToList(), GoalNewerJson.NewerOf(list));
    }
}
