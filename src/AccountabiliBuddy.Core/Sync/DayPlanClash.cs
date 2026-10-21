namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The clash rule for a day plan from another device (ADR 19, 22): each plan says the newest version its device shares
/// with the others (<see cref="SyncDayPlan.BasedOn"/>), so an edit on one side, however many steps, is taken or kept
/// without asking, a device that didn't edit takes the newer plan, and only edits on both sides ask the user.
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

        // Nothing edited here to lose: the newer plan wins (one that missed a version in between, or an old one).
        if (!local.HasLocalEdits())
        {
            return IsNewer(incoming, local) ? DayPlanSyncDecision.TakeIncoming : DayPlanSyncDecision.KeepLocal;
        }

        // An older plan nobody edited is one this device moved on from.
        if (!incoming.HasLocalEdits() && !IsNewer(incoming, local))
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

    /// <summary>Edited later; on the same time, the higher version, so two devices pick the same plan.</summary>
    private static bool IsNewer(SyncDayPlan plan, SyncDayPlan than)
    {
        return plan.EditedAt != than.EditedAt
            ? plan.EditedAt > than.EditedAt
            : string.CompareOrdinal(plan.Version, than.Version) > 0;
    }
}
