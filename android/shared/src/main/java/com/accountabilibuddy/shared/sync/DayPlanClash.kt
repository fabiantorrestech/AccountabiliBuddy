package com.accountabilibuddy.shared.sync

/** What to do with a day plan that arrives from another device ([DayPlanClash.decide]), as on the PC. */
enum class DayPlanSyncDecision {
    /** Both devices hold the same plan. */
    Same,

    /** The other device edited the plan this device holds: take its plan. */
    TakeIncoming,

    /** This device edited the plan the other device holds: keep this one. */
    KeepLocal,

    /**
     * Both changed it before they synced: ask which to keep ("Keep the one I was using" / "Use the latest"); closed or
     * ignored, the latest edit wins ([DayPlanClash.latest]).
     */
    Ask,
}

/**
 * The clash rule for a day plan from another device (ADR 19, 22), as on the PC (Core's DayPlanClash): each plan says
 * which version its last edit started from, so a plain edit on one side is taken or kept without asking, and only
 * edits on both sides ask the user.
 */
object DayPlanClash {
    fun decide(local: SyncDayPlan, incoming: SyncDayPlan): DayPlanSyncDecision = when {
        local.version == incoming.version -> DayPlanSyncDecision.Same
        incoming.basedOn == local.version -> DayPlanSyncDecision.TakeIncoming
        local.basedOn == incoming.version -> DayPlanSyncDecision.KeepLocal
        else -> DayPlanSyncDecision.Ask
    }

    /** The plan edited last; the incoming one when both were edited at the same time. */
    fun latest(local: SyncDayPlan, incoming: SyncDayPlan): SyncDayPlan =
        if (!incoming.editedAt.isBefore(local.editedAt)) incoming else local
}
