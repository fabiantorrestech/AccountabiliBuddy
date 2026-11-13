package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.FieldStamp

/**
 * Deep work's weekly target on the phone (plan §3b 6, brief §5.7, §5.10; ADR 79): the phone's own, none until set, kept in step with the
 * other devices' (the PC's `DeepWorkGoalSettings`). Its daily target is the PC's and read only here ([GoalPhoneRules.canEditDailyTarget]).
 *
 * @property minutes The weekly minutes (30 min to 56h), null = none.
 * @property pending A lowering (or removal) of the weekly target waiting for its delay; null = none.
 * @property trackTimeOnly "Track time only, no target" as the PC set it: the phone only carries it (no weekly target is shown while it is on).
 * @property pendingTrackTimeOnly "Track time only" received and waiting for its delay.
 * @property stamp When and where the weekly target and Track time only in effect here were last changed (the part stamp they travel under);
 * never the stamp of a change still waiting. Null before the first one: the stored record's own stamp counts then.
 * @property pendingStamp The stamp a waiting change received from another device takes when it lands (nothing then echoes); null for one made here.
 * @property pendingAfterSession A looser value received during a focus session: held, its wait counted from the session's end.
 */
data class GoalDeepWorkWeekly(
    val minutes: Int? = null,
    val pending: GoalPendingChange? = null,
    val trackTimeOnly: Boolean = false,
    val pendingTrackTimeOnly: GoalPendingChange? = null,
    val stamp: FieldStamp? = null,
    val pendingStamp: FieldStamp? = null,
    val pendingAfterSession: Boolean = false,
) {
    /** The weekly target in effect (none while Track time only is on). */
    val effectiveMinutes: Int? get() = if (trackTimeOnly) null else minutes

    /** Whether any change is waiting. */
    val hasPending: Boolean get() = pending != null || pendingTrackTimeOnly != null

    /** The weekly target as the goals maths reads it ([Goal.secondTarget]); null = none. */
    fun asTarget(): GoalTarget? = effectiveMinutes?.let { GoalTarget(GoalPeriod.Week, it.toDouble(), GoalUnit.Minutes) }
}
