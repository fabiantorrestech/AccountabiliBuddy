package com.accountabilibuddy.shared.sync

/** What to do with a day plan that arrives from another device ([DayPlanClash.decide]), as on the PC. */
enum class DayPlanSyncDecision {
    /** Both devices hold the same plan. */
    Same,

    /** The other device edited the plan this device holds, or this device didn't edit and the other's is newer: take it. */
    TakeIncoming,

    /** This device edited the plan the other device holds, or the other's is an older one: keep this one. */
    KeepLocal,

    /**
     * Both changed it before they synced: ask which to keep ("Keep the one I was using" / "Use the latest"); closed or
     * ignored, the latest edit wins ([DayPlanClash.latest]).
     */
    Ask,
}

/**
 * The clash rule for a day plan from another device (ADR 19, 22), as on the PC (Core's DayPlanClash): each plan says
 * the newest version its device shares with the others ([SyncDayPlan.basedOn]), so an edit on one side, however many
 * steps, is taken or kept without asking, a device that didn't edit takes the newer plan, and only edits on both sides
 * ask the user.
 */
object DayPlanClash {
    fun decide(local: SyncDayPlan, incoming: SyncDayPlan): DayPlanSyncDecision = when {
        local.version == incoming.version -> DayPlanSyncDecision.Same
        incoming.basedOn == local.version -> DayPlanSyncDecision.TakeIncoming
        local.basedOn == incoming.version -> DayPlanSyncDecision.KeepLocal
        // Nothing edited here to lose: the newer plan wins (one that missed a version in between, or an old one).
        !local.hasLocalEdits() -> if (isNewer(incoming, local)) DayPlanSyncDecision.TakeIncoming else DayPlanSyncDecision.KeepLocal
        // An older plan nobody edited is one this device moved on from.
        !incoming.hasLocalEdits() && !isNewer(incoming, local) -> DayPlanSyncDecision.KeepLocal
        else -> DayPlanSyncDecision.Ask
    }

    /** The plan edited last; the incoming one when both were edited at the same time. */
    fun latest(local: SyncDayPlan, incoming: SyncDayPlan): SyncDayPlan =
        if (!incoming.editedAt.isBefore(local.editedAt)) incoming else local

    /** Edited later; on the same time, the higher version (ordinal, as on the PC), so two devices pick the same plan. */
    private fun isNewer(plan: SyncDayPlan, than: SyncDayPlan): Boolean =
        if (plan.editedAt != than.editedAt) plan.editedAt.isAfter(than.editedAt) else plan.version > than.version
}
