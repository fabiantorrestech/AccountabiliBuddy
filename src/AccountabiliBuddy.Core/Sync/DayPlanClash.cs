namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The clash rule for a day plan from another device (ADR 19, 22): each plan says which version its last edit started
/// from, so a plain edit on one side is taken or kept without asking, and only edits on both sides ask the user.
/// </summary>
public static class DayPlanClash
{
    public static DayPlanSyncDecision Decide(SyncDayPlan local, SyncDayPlan incoming)
    {
        if (string.Equals(local.Version, incoming.Version, StringComparison.Ordinal))
        {
            return DayPlanSyncDecision.Same;
        }

        if (string.Equals(incoming.BasedOn, local.Version, StringComparison.Ordinal))
        {
            return DayPlanSyncDecision.TakeIncoming;
        }

        if (string.Equals(local.BasedOn, incoming.Version, StringComparison.Ordinal))
        {
            return DayPlanSyncDecision.KeepLocal;
        }

        return DayPlanSyncDecision.Ask;
    }

    /// <returns>The plan edited last; the incoming one when both were edited at the same time.</returns>
    public static SyncDayPlan Latest(SyncDayPlan local, SyncDayPlan incoming)
    {
        return incoming.EditedAt >= local.EditedAt ? incoming : local;
    }
}
