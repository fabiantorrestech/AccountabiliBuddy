package com.accountabilibuddy.shared.goals

import java.time.Duration
import java.time.OffsetDateTime

/**
 * Deep work's daily target when no PC is in the phone's group (NG1, "Works alone, syncs by choice"; amends ADR 29 for groups without a PC):
 * the phone owns it, none until set. It is this phone's alone: it travels in no file and carries no stamp. While a PC is in the group
 * the PC's daily target is the one in effect, always (ADR 29 as before); the phone's own value is kept unused and comes back if the
 * PCs go ([GoalDeepWorkDailyRules.effective]).
 *
 * @property minutes The daily minutes ([GoalDeepWorkDailyRules.MIN_MINUTES]..[GoalDeepWorkDailyRules.MAX_MINUTES]), null = none.
 * @property pending A lowering (or removal) waiting for its delay; null = none.
 */
data class GoalDeepWorkDaily(val minutes: Int? = null, val pending: GoalPendingChange? = null)

/**
 * The looser-change rule for the phone-owned daily target: the same decisions as the weekly target ([GoalDeepWorkWeeklyRules], on
 * [GoalLowering]) for the daily period. Raising or adding applies at once, also in a session; lowering or removing waits the delay,
 * is refused during a session and never applies while one runs (outside ProductaBuddy mode, where it applies at once unless the
 * work-hours rule holds it); no "apply now". The time is the trusted time, read by the caller.
 */
object GoalDeepWorkDailyRules {
    const val MIN_MINUTES = 15
    const val MAX_MINUTES = 480

    /** Asks for [minutes] (null = none), clamped to 15 min to 8h. */
    fun set(
        state: GoalDeepWorkDaily,
        minutes: Int?,
        productaBuddy: Boolean,
        sessionRunning: Boolean,
        lowerWaitsNow: Boolean,
        now: OffsetDateTime,
        looserDelay: Duration,
    ): GoalDeepWorkDailyChange {
        val wanted = minutes?.coerceIn(MIN_MINUTES, MAX_MINUTES)
        val current = GoalTarget(GoalPeriod.Day, state.minutes?.toDouble(), GoalUnit.Minutes)
        val decision = GoalLowering.decide(deepWork(now), current, wanted?.toDouble(), productaBuddy, lowerWaitsNow, now, looserDelay)
        return when (decision.outcome) {
            GoalChangeOutcome.Unchanged -> GoalDeepWorkDailyChange(state.copy(pending = null), GoalDeepWorkWeeklyResult.Unchanged)
            GoalChangeOutcome.ApplyNow -> GoalDeepWorkDailyChange(state.copy(minutes = wanted, pending = null), GoalDeepWorkWeeklyResult.Applied)
            GoalChangeOutcome.Wait -> {
                if (!productaBuddy && sessionRunning) {
                    return GoalDeepWorkDailyChange(state, GoalDeepWorkWeeklyResult.RefusedDuringSession)
                }

                val running = state.pending
                if (running != null && running.amount == wanted?.toDouble()) {
                    return GoalDeepWorkDailyChange(state, GoalDeepWorkWeeklyResult.Waiting)
                }

                GoalDeepWorkDailyChange(state.copy(pending = decision.pending(wanted?.toDouble())), GoalDeepWorkWeeklyResult.Waiting)
            }
        }
    }

    /** A waiting change whose time has come applies: no session runs (outside ProductaBuddy mode) and the work-hours rule doesn't hold it. */
    fun settle(
        state: GoalDeepWorkDaily,
        productaBuddy: Boolean,
        sessionRunning: Boolean,
        lowerWaitsNow: Boolean,
        now: OffsetDateTime,
        looserDelay: Duration,
    ): GoalDeepWorkDaily {
        val held = state.pending ?: return state
        val pending = if (productaBuddy) held else GoalLowering.intoAccountabiliBuddy(held, now, looserDelay)
        if (!GoalLowering.canApply(pending, now, sessionRunning, productaBuddy, lowerWaitsNow)) {
            return state.copy(pending = pending)
        }

        return state.copy(minutes = pending.amount?.toInt(), pending = null)
    }

    /** Cancel on a waiting change. */
    fun cancel(state: GoalDeepWorkDaily): GoalDeepWorkDaily = state.copy(pending = null)

    /**
     * The daily target in effect: while a PC is in the group the PC's ([pcMinutes], null when its file carries none: "not set yet"
     * from the PC, the phone's own value is not used then); with no PC, the phone's own.
     */
    fun effective(hasPc: Boolean, pcMinutes: Int?, own: GoalDeepWorkDaily): Int? = if (hasPc) pcMinutes else own.minutes

    /** Whether [minutes] is a daily target the phone accepts. */
    fun isValid(minutes: Int): Boolean = minutes in MIN_MINUTES..MAX_MINUTES

    private fun deepWork(now: OffsetDateTime): Goal = GoalDefaults.newDeepWork(GoalDefaults.WORK_AREA_ID, now)
}

/** What asking for a daily target did: the new state and the result. */
data class GoalDeepWorkDailyChange(val state: GoalDeepWorkDaily, val result: GoalDeepWorkWeeklyResult)
