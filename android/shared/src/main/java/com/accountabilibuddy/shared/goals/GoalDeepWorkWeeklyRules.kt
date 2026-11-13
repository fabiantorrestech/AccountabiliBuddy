package com.accountabilibuddy.shared.goals

import java.time.Duration
import java.time.OffsetDateTime

/**
 * The looser-change rule for Deep work's weekly target on the phone, a twin of the PC's `GoalsService` (`SetWeeklyGatedAsync`,
 * `ApplyPendingGatedAsync`, `RestoreDeepWorkAsync`) built on the same [GoalLowering] decisions:
 * - raising, or adding a target, applies at once, also during a session, and drops a waiting change;
 * - lowering or removing it waits the looser-change delay (outside ProductaBuddy mode), is refused during a session (outside
 *   ProductaBuddy mode), never applies while a session runs or the work-hours rule holds it, and has no "apply now";
 * - in ProductaBuddy mode it skips the delay and the session rule but keeps the work-hours rule (Q362); such a wait restarts its full
 *   delay once AccountabiliBuddy mode sees it (Q377);
 * - a wait already running for the same amount keeps its time; Cancel drops it.
 * Goal counts never feed a guard, the goal unlock or `focusDay`: nothing here is read by any of them.
 */
object GoalDeepWorkWeeklyRules {
    const val MIN_MINUTES = 30
    const val MAX_MINUTES = 480 * 7

    /** Asks for [minutes] (null = none), clamped to 30 min to 56h. */
    fun set(
        state: GoalDeepWorkWeekly,
        minutes: Int?,
        productaBuddy: Boolean,
        sessionRunning: Boolean,
        lowerWaitsNow: Boolean,
        now: OffsetDateTime,
        looserDelay: Duration,
    ): GoalDeepWorkWeeklyChange {
        val wanted = minutes?.coerceIn(MIN_MINUTES, MAX_MINUTES)
        val current = GoalTarget(GoalPeriod.Week, state.minutes?.toDouble(), GoalUnit.Minutes)
        val decision = GoalLowering.decide(deepWork(now), current, wanted?.toDouble(), productaBuddy, lowerWaitsNow, now, looserDelay)
        return when (decision.outcome) {
            GoalChangeOutcome.Unchanged -> GoalDeepWorkWeeklyChange(state.copy(pending = null), GoalDeepWorkWeeklyResult.Unchanged)
            GoalChangeOutcome.ApplyNow -> GoalDeepWorkWeeklyChange(state.copy(minutes = wanted, pending = null), GoalDeepWorkWeeklyResult.Applied)
            GoalChangeOutcome.Wait -> {
                if (!productaBuddy && sessionRunning) {
                    return GoalDeepWorkWeeklyChange(state, GoalDeepWorkWeeklyResult.RefusedDuringSession)
                }

                val running = state.pending
                if (running != null && sameAmount(running.amount, wanted?.toDouble())) {
                    return GoalDeepWorkWeeklyChange(state, GoalDeepWorkWeeklyResult.Waiting)
                }

                GoalDeepWorkWeeklyChange(state.copy(pending = decision.pending(wanted?.toDouble())), GoalDeepWorkWeeklyResult.Waiting)
            }
        }
    }

    /**
     * Applies a waiting change whose time has come ([GoalLowering.canApply]): no session runs (outside ProductaBuddy mode) and the
     * work-hours rule doesn't hold it. In AccountabiliBuddy mode a wait made in ProductaBuddy mode restarts its full delay first.
     */
    fun settle(
        state: GoalDeepWorkWeekly,
        productaBuddy: Boolean,
        sessionRunning: Boolean,
        lowerWaitsNow: Boolean,
        now: OffsetDateTime,
        looserDelay: Duration,
    ): GoalDeepWorkWeekly {
        val held = state.pending ?: return state
        val pending = if (productaBuddy) held else GoalLowering.intoAccountabiliBuddy(held, now, looserDelay)
        if (!GoalLowering.canApply(pending, now, sessionRunning, productaBuddy, lowerWaitsNow)) {
            return state.copy(pending = pending)
        }

        return state.copy(minutes = pending.amount?.toInt(), pending = null)
    }

    /** Cancel on a waiting change. */
    fun cancel(state: GoalDeepWorkWeekly): GoalDeepWorkWeekly = state.copy(pending = null)

    /**
     * A backup's weekly target (plan §3b 6): through the same rules as a change made here, so a lower weekly target, or none, waits
     * (or is refused during a session) and a raise applies at once. A waiting change inside the backup is never taken over.
     */
    fun restore(
        state: GoalDeepWorkWeekly,
        backupMinutes: Int?,
        productaBuddy: Boolean,
        sessionRunning: Boolean,
        lowerWaitsNow: Boolean,
        now: OffsetDateTime,
        looserDelay: Duration,
    ): GoalDeepWorkWeeklyChange = set(state, backupMinutes, productaBuddy, sessionRunning, lowerWaitsNow, now, looserDelay)

    /** Whether [minutes] is a weekly target the phone accepts (30 min to 56h). */
    fun isValid(minutes: Int): Boolean = minutes in MIN_MINUTES..MAX_MINUTES

    private fun deepWork(now: OffsetDateTime): Goal = GoalDefaults.newDeepWork(GoalDefaults.WORK_AREA_ID, now)

    private fun sameAmount(a: Double?, b: Double?): Boolean = if (a == null || b == null) a == null && b == null else a == b
}
