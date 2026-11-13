package com.accountabilibuddy.companion.goals

import com.accountabilibuddy.shared.goals.GoalDeepWorkWeekly
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeeklySync
import com.accountabilibuddy.shared.sync.FieldStamp
import java.time.Duration
import java.time.OffsetDateTime

/**
 * The facts Deep work's weekly rule needs, read when a change is asked or settled (pure: no Android API, so the checks compile it).
 *
 * @property sessionRunning A focus session the user started runs (the check the removal lock uses).
 * @property lowerWaitsNow The work-hours rule holds a lowering: the phone has no such rule, so false (Q504).
 * @property known False for the placeholder used before the app has said what the mode and the session are (GS4 review, BLOCKER 1):
 * with an unknown session state nothing waiting may be applied or re-timed ([settle] leaves the state as it is).
 */
data class PhoneWeeklyEnvironment(
    val productaBuddy: Boolean,
    val sessionRunning: Boolean,
    val now: OffsetDateTime,
    val looserDelay: Duration,
    val lowerWaitsNow: Boolean = false,
    val known: Boolean = true,
) {
    /** The rule's own facts. */
    fun toSync(): GoalDeepWorkWeeklySync.Env = GoalDeepWorkWeeklySync.Env(productaBuddy, sessionRunning, now, looserDelay, lowerWaitsNow)

    companion object {
        /**
         * The environment before the app has set one: fail safe. A session is assumed to be running (a lowering asked for is refused, a
         * raise still applies at once) and [known] is false, so a waiting change is neither applied nor moved to "after the session".
         */
        fun unknown(now: OffsetDateTime, looserDelay: Duration = Duration.ofMinutes(10)): PhoneWeeklyEnvironment =
            PhoneWeeklyEnvironment(productaBuddy = false, sessionRunning = true, now = now, looserDelay = looserDelay, known = false)
    }
}

/** The settle step of [PhoneDeepWorkWeekly.current], apart from the preferences so a check can drive it. */
object PhoneWeeklySettle {
    /** The state with a due waiting change applied; **unchanged** while the environment is [PhoneWeeklyEnvironment.known] to be unknown. */
    fun settle(state: GoalDeepWorkWeekly, recordStamp: FieldStamp, env: PhoneWeeklyEnvironment, selfId: String): GoalDeepWorkWeekly =
        if (!env.known) state else GoalDeepWorkWeeklySync.settle(state, recordStamp, env.toSync(), selfId)
}
