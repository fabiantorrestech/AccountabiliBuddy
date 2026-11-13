using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// Stamps a local change to a goal (ADR 79, owner 2026-11-14) the way <see cref="GoalSyncMerge"/> expects: each changed part
/// (<see cref="GoalField"/>) gets a new stamp (now, or just past the goal's newest stamp, so a change right after a faster clock's edit
/// still wins) by this device, and the goal's edit time moves on. The goals' twin of <see cref="TaskEdits"/>; every change to a goal must
/// go through it (or <see cref="Restamp"/>), or the change loses to an older one from another device.
/// </summary>
public static class GoalEdits
{
    /// <summary>A goal just made on this device: every part stamped at its edit time by <paramref name="by"/>.</summary>
    public static Goal New(Goal goal, Guid by)
    {
        return goal with { Stamps = GoalFieldStamps.AllAt(new GoalFieldStamp(goal.EditedAt, by)) };
    }

    /// <returns><paramref name="goal"/> with <paramref name="field"/> taken from <paramref name="changed"/> and stamped; the same goal when the value didn't change.</returns>
    public static Goal Edit(Goal goal, GoalField field, Goal changed, DateTimeOffset now, Guid by)
    {
        if (string.Equals(GoalFieldValues.Canonical(goal, field), GoalFieldValues.Canonical(changed, field), StringComparison.Ordinal))
        {
            return goal;
        }

        return Stamped(goal, [field], GoalFieldValues.With(goal, changed, field), now, by);
    }

    /// <summary>
    /// <paramref name="after"/> (a goal a screen or a rule rebuilt from <paramref name="before"/>) with every part whose value changed
    /// stamped by <paramref name="by"/>, the others keeping <paramref name="before"/>'s stamps. When no part changed: <paramref name="after"/>
    /// with <paramref name="before"/>'s stamps (an unstamped goal's taken as they read, <see cref="GoalFieldStamps.For"/>, so they stay at
    /// the old edit time) and its edit time, never a newer one (a save that changed nothing must not beat another device's earlier edit).
    /// A waiting change alone changes no part (<see cref="GoalFieldValues"/>). One place for the stores to call on every change.
    /// </summary>
    public static Goal Restamp(Goal before, Goal after, DateTimeOffset now, Guid by)
    {
        Dictionary<GoalField, string> old = GoalFieldValues.All(before);
        Dictionary<GoalField, string> afterValues = GoalFieldValues.All(after);
        List<GoalField> changed = GoalFieldStamps.Fields.Where(field => !string.Equals(old[field], afterValues[field], StringComparison.Ordinal)).ToList();
        if (changed.Count == 0)
        {
            return after with { Stamps = before.Stamps ?? GoalFieldStamps.For(before), EditedAt = before.EditedAt };
        }

        return Stamped(before, changed, after, now, by);
    }

    private static Goal Stamped(Goal before, IReadOnlyList<GoalField> fields, Goal changed, DateTimeOffset now, Guid by)
    {
        GoalFieldStamps stamps = GoalFieldStamps.For(before);
        DateTimeOffset at = SharedListMerge.NextStamp(now, before);
        if (stamps.Newest is DateTimeOffset newest && newest >= at)
        {
            at = newest.AddTicks(1);
        }

        var stamp = new GoalFieldStamp(at, by);
        foreach (GoalField field in fields)
        {
            stamps = stamps.With(field, stamp);
        }

        return changed with { EditedAt = at > changed.EditedAt ? at : changed.EditedAt, Stamps = stamps };
    }
}
