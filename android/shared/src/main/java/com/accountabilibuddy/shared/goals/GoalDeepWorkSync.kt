package com.accountabilibuddy.shared.goals

/**
 * How Deep work travels (ADR 79; the PC's `GoalDeepWorkSync`): its record carries the device's effective weekly target as the second
 * target and "Track time only", under the second-target part's own stamp. The stored goal keeps neither.
 */
object GoalDeepWorkSync {
    /** The record to send: [stored] with the effective [targets] put in. */
    fun compose(stored: Goal, targets: GoalDeepWorkTargets): Goal {
        val weekly = targets.weeklyMinutes?.let { GoalTarget(GoalPeriod.Week, it, GoalUnit.Minutes) }
        return stored.copy(
            target = GoalSeeds.DEEP_WORK_STORED_TARGET,
            secondTarget = weekly,
            trackTimeOnly = targets.trackTimeOnly,
            stamps = GoalFieldStamps.forGoal(stored).with(GoalField.SecondTarget, targets.stamp),
        )
    }

    /** A received record split into the goal to store and the targets it carried; only Week + Minutes + a positive finite amount is a weekly target. */
    fun split(record: Goal): Pair<Goal, GoalDeepWorkTargets> {
        val stamp = GoalFieldStamps.forGoal(record).of(GoalField.SecondTarget)!!
        val second = record.secondTarget
        val amount = second?.amount
        val weekly = if (second != null && second.period == GoalPeriod.Week && second.unit == GoalUnit.Minutes && amount != null && amount > 0 && amount.isFinite()) amount else null
        val stored = record.copy(target = GoalSeeds.DEEP_WORK_STORED_TARGET, secondTarget = null, trackTimeOnly = false)
        return stored to GoalDeepWorkTargets(weekly, record.trackTimeOnly, stamp)
    }
}
