namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// Deleting goal items and what a tombstone keeps (ADR 79): a tombstone carries no text and nothing a newer app added (plan check
/// GS0 carry a, Q578), only what the merge needs (id, stamps, place) and harmless values. Deleting a goal does not delete its entries or
/// reminders: they stay, hidden while the goal is gone (<see cref="GoalSyncMerge.IsShown(GoalEntry, IReadOnlySet{Guid})"/>), come back
/// with the goal when an edit newer than the deletion revives it, and are dropped once the goal's tombstone is pruned (30 days). The
/// phone's twin keeps the same.
/// </summary>
public static class GoalTombstones
{
    /// <summary>
    /// <paramref name="goal"/> deleted at <paramref name="at"/>; its entries and reminders are left alone. Replaces the PC's old rule
    /// (<c>GoalsService.DeleteGoalAsync</c> also deleted every entry and reminder; the App moves to this one in GS3).
    /// </summary>
    public static Goal Delete(Goal goal, DateTimeOffset at)
    {
        return Clean(goal with { DeletedAt = at, EditedAt = at });
    }

    /// <summary>
    /// What a tombstone written without a target holds as its target (an odd or minimal writer): a daily count without an amount, so
    /// every tombstone written back is a complete goal on both platforms (JSON <c>{"period":"Day","unit":"Count","direction":"AtLeast"}</c>).
    /// </summary>
    public static GoalTarget PlaceholderTarget { get; } = new(GoalPeriod.Day, null, GoalUnit.Count);

    /// <summary>
    /// A goal tombstone as it is kept and sent: no name, notes, sources, second target, pauses, tiles, stamps or unknown fields; one
    /// written without a target, icon or colour gets <see cref="PlaceholderTarget"/> and empty texts.
    /// </summary>
    public static Goal Clean(Goal goal)
    {
        if (goal.IsLive)
        {
            return goal;
        }

        return goal with
        {
            Name = string.Empty,
            Notes = null,
            Sources = [],
            Icon = goal.Icon ?? string.Empty,
            Color = goal.Color ?? string.Empty,
            Target = goal.Target is null ? PlaceholderTarget : goal.Target with { PendingChange = null, Extra = null },
            SecondTarget = null,
            TrackTimeOnly = false,
            Pauses = null,
            ExtraTiles = null,
            Stamps = null,
            Extra = null,
        };
    }

    public static GoalArea Clean(GoalArea area)
    {
        return area.IsLive ? area : area with { Name = string.Empty, Icon = area.Icon ?? string.Empty, Color = area.Color ?? string.Empty, Extra = null };
    }

    public static GoalEntry Clean(GoalEntry entry)
    {
        return entry.IsLive ? entry : entry with { Extra = null };
    }

    public static GoalReminder Clean(GoalReminder reminder)
    {
        return reminder.IsLive ? reminder : reminder with { Reason = null, Extra = null };
    }

    public static GoalReviewRecord Clean(GoalReviewRecord review)
    {
        return review.IsLive ? review : review with { Decisions = [], Notes = null, SnoozedUntil = null, Extra = null };
    }
}
