using System.Diagnostics.CodeAnalysis;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// The goal parts with every item the merge can't use left out (ADR 79; the goals' twin of <c>SyncFileCodec</c>'s task-part clean). A
/// sealed file is trusted for its key, not its shape: a hand-made, damaged or odd writer's item must never throw in the merge. Run on
/// a file as it opens (<c>SyncFileCodec.Open</c>), on the parts taken from or put in a file (<c>SyncGoalParts</c>) and on both inputs
/// of <see cref="GoalSyncMerge.Merge"/>. Leaving an item out never deletes it: the merge keeps what the receiver has. The phone's twin
/// (GS2) applies the same rules:
/// <list type="bullet">
/// <item>Every list: an item without an id (or the empty id) is left out; so is a newer app's item (kept as written) whose id reads
/// as the empty id.</item>
/// <item>Areas: a live one without a name is left out; a missing icon or colour reads as empty.</item>
/// <item>Goals: a live one without a name, a target or a sources list is left out (even when it carries unknown fields: a goal of a
/// kind this app knows that lacks them is damaged; a newer app's goal of an unknown kind is kept as written before this rule); a missing
/// icon or colour reads as empty. A tombstone needs only its id and stamps (<see cref="GoalTombstones.Clean(Goal)"/> fills the rest).
/// A newer app's Deep work tombstone is left out (Deep work is never deleted).</item>
/// <item>Entries and reminders: one without a goal id is left out.</item>
/// <item>Reviews: missing decisions read as none.</item>
/// </list>
/// </summary>
public static class GoalSyncClean
{
    /// <summary>All five lists cleaned (a missing list reads as empty).</summary>
    public static GoalSyncLists Lists(GoalSyncLists lists)
    {
        return new GoalSyncLists(
            Areas(lists.Areas ?? []),
            Goals(lists.Goals ?? []),
            Entries(lists.Entries ?? []),
            Reminders(lists.Reminders ?? []),
            Reviews(lists.Reviews ?? []));
    }

    [return: NotNullIfNotNull(nameof(list))]
    public static IReadOnlyList<GoalArea>? Areas(IReadOnlyList<GoalArea>? list)
    {
        return Each(list, area =>
        {
            if (area.Id == Guid.Empty || (area.IsLive && area.Name is null))
            {
                return null;
            }

            GoalArea filled = area.Icon is null || area.Color is null ? area with { Icon = area.Icon ?? string.Empty, Color = area.Color ?? string.Empty } : area;
            return GoalTombstones.Clean(filled);
        });
    }

    [return: NotNullIfNotNull(nameof(list))]
    public static IReadOnlyList<Goal>? Goals(IReadOnlyList<Goal>? list)
    {
        return Each(list, goal =>
        {
            if (goal.Id == Guid.Empty)
            {
                return null;
            }

            if (!goal.IsLive)
            {
                return GoalTombstones.Clean(goal);
            }

            if (goal.Name is null || goal.Target is null || goal.Sources is null)
            {
                return null;
            }

            return goal.Icon is null || goal.Color is null ? goal with { Icon = goal.Icon ?? string.Empty, Color = goal.Color ?? string.Empty } : goal;
        }, raw => !(raw.Id == Goal.DeepWorkId && raw.DeletedAt is not null));
    }

    [return: NotNullIfNotNull(nameof(list))]
    public static IReadOnlyList<GoalEntry>? Entries(IReadOnlyList<GoalEntry>? list)
    {
        return Each(list, entry => entry.Id == Guid.Empty || entry.GoalId == Guid.Empty ? null : entry);
    }

    [return: NotNullIfNotNull(nameof(list))]
    public static IReadOnlyList<GoalReminder>? Reminders(IReadOnlyList<GoalReminder>? list)
    {
        return Each(list, reminder => reminder.Id == Guid.Empty || reminder.GoalId == Guid.Empty ? null : reminder);
    }

    [return: NotNullIfNotNull(nameof(list))]
    public static IReadOnlyList<GoalReviewRecord>? Reviews(IReadOnlyList<GoalReviewRecord>? list)
    {
        return Each(list, review =>
        {
            if (review.Id == Guid.Empty)
            {
                return null;
            }

            return review.Decisions is null ? review with { Decisions = [] } : review;
        });
    }

    // The known items cleaned (null = left out), the newer ones kept unless their id reads as the empty id or the rule drops them.
    private static IReadOnlyList<T>? Each<T>(IReadOnlyList<T>? list, Func<T, T?> clean, Func<GoalRawItem, bool>? keepRaw = null)
        where T : class
    {
        if (list is null)
        {
            return null;
        }

        List<T> known = list.Where(item => item is not null).Select(clean).OfType<T>().ToList();
        List<GoalNewerItem> newer = GoalNewerJson.NewerOf(list)
            .Where(item => GoalRawItem.TryRead(item.Json) is not { } raw || (raw.Id != Guid.Empty && (keepRaw?.Invoke(raw) ?? true)))
            .ToList();
        return GoalNewerJson.WithNewer(known, newer);
    }
}
