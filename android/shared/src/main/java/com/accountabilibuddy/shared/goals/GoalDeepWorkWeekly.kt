package com.accountabilibuddy.shared.goals

/**
 * Deep work's weekly target on the phone (plan §3b 6, brief §5.7, §5.10): the phone's own, none until set. Its daily target is the PC's
 * and read only here (ADR 29, [GoalPhoneRules.canEditDailyTarget]).
 *
 * @property minutes The weekly minutes (30 to 56h), null = none.
 * @property pending A lowering (or removal) waiting for its delay; null = none.
 */
data class GoalDeepWorkWeekly(val minutes: Int? = null, val pending: GoalPendingChange? = null) {
    /** The weekly target as the goals maths reads it ([Goal.secondTarget]); null = none. */
    fun asTarget(): GoalTarget? = minutes?.let { GoalTarget(GoalPeriod.Week, it.toDouble(), GoalUnit.Minutes) }
}
