namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// The built-in Deep work goal's record in a sync file (ADR 79, plan check 1.5). The stored record holds a placeholder target and no
/// second target (the daily target is the "goal" document, ADR 29; the weekly one and "Track time only" are device settings), so the
/// record is composed on the way out and split on the way in. Pure; applying a received lower or removed weekly target (the receiver's
/// own looser-change wait, never during a session outside ProductaBuddy mode) is the App's (GS3). The phone's twin does the same.
/// </summary>
public static class GoalDeepWorkSync
{
    /// <summary>
    /// The record as this device writes it: <paramref name="stored"/> with this device's effective weekly target as the second target
    /// (a Week target in minutes, none when null), "Track time only" and their own stamp; the stored placeholder target, no waiting change.
    /// </summary>
    public static Goal Compose(Goal stored, GoalDeepWorkTargets effective)
    {
        GoalTarget? weekly = effective.WeeklyMinutes is double minutes ? new GoalTarget(GoalPeriod.Week, minutes, GoalUnit.Minutes) : null;
        return stored with
        {
            Target = GoalSeeds.DeepWorkStoredTarget,
            SecondTarget = weekly,
            TrackTimeOnly = effective.TrackTimeOnly,
            Stamps = GoalFieldStamps.For(stored).With(GoalField.SecondTarget, effective.Stamp),
        };
    }

    /// <summary>
    /// A received (or merged) Deep work record split into what is stored (placeholder target, no second target, "Track time only" off;
    /// its stamps as they came) and the weekly target and "Track time only" it carried, with their stamp, for the receiver's rule. Only
    /// a second target of period Week, unit Minutes and a positive amount is a weekly target; any other (an odd writer) reads as none.
    /// For the receiver's rule (GS3, the phone's in GS4): a removed (or unreadable) weekly target, a lower one and "Track time only"
    /// turning on are loosenings (the looser-change wait, never during a session outside ProductaBuddy mode); a higher or new weekly
    /// target and "Track time only" turning off are stricter (at once).
    /// </summary>
    public static (Goal Stored, GoalDeepWorkTargets Received) Split(Goal record)
    {
        GoalFieldStamp stamp = GoalFieldStamps.For(record).Of(GoalField.SecondTarget)!;
        double? weekly = record.SecondTarget is { Period: GoalPeriod.Week, Unit: GoalUnit.Minutes, Amount: double amount } && amount > 0 && double.IsFinite(amount) ? amount : null;
        var received = new GoalDeepWorkTargets(weekly, record.TrackTimeOnly, stamp);
        Goal stored = record with { Target = GoalSeeds.DeepWorkStoredTarget, SecondTarget = null, TrackTimeOnly = false };
        return (stored, received);
    }
}
