package com.accountabilibuddy.shared.session

import java.time.Duration
import java.time.Instant

/** What the phone shows or does when the night hands over from a session (the Android side turns each into a notification or a call). */
enum class NightHandoverEffect {
    /** "Your night routine starts when this session ends." (once per night, right away). */
    SHOW_REMINDER,
    CLEAR_REMINDER,

    /** "Night routine starts in 5 minutes" with Snooze (and Cancel in ProductaBuddy). */
    SHOW_COUNTDOWN,
    CLEAR_COUNTDOWN,

    /** The 5 minutes are over: the night lock and the bedtime screen start as usual (the once-per-night lock request is asked again). */
    NIGHT_STARTS,
}

/**
 * One night's hand-over state (owner design and answers, 2026-11-15): a real session (this phone's own, or a PC's; never the test session) takes
 * over the night, in either mode. [night] is the night's id (its start), [sawSession] that a real session ran during it, [startAt] when the night
 * starts after the first session ended ([NightHandoverRule.WAIT] later, moved only by Snooze): it is set ONCE per night, so a later session never
 * moves or restarts it, [done] that it started, [cancelled] that tonight was skipped (ProductaBuddy only), [snoozes] the Snoozes used tonight,
 * [countdownShown] that the "starts in 5 minutes" notice is up.
 */
data class NightHandoverState(
    val night: String? = null,
    val sawSession: Boolean = false,
    val reminderShown: Boolean = false,
    val startAt: Instant? = null,
    val done: Boolean = false,
    val cancelled: Boolean = false,
    val snoozes: Int = 0,
    val countdownShown: Boolean = false,
)

data class NightHandoverStep(val state: NightHandoverState, val effects: List<NightHandoverEffect>)

/** Pure, no clock: every decision takes the trusted time handed in. */
object NightHandoverRule {
    /** The night starts this long after the first session of the night ended. */
    val WAIT: Duration = Duration.ofMinutes(5)

    /** Snooze moves the start to this long from now: the bedtime reminders' snooze (5 minutes). */
    val SNOOZE: Duration = Duration.ofMinutes(5)

    /** AccountabiliBuddy: two Snoozes per night, then the night starts. ProductaBuddy has no cap. */
    const val AB_MAX_SNOOZES = 2

    /** Cancel tonight: ProductaBuddy only. No skip of the night exists in AccountabiliBuddy, which offers Snooze only (owner design; Q1198). */
    fun canCancel(mode: PhoneSessionMode): Boolean = mode == PhoneSessionMode.PRODUCTA_BUDDY

    /** Whether one more Snooze is offered tonight. */
    fun canSnooze(mode: PhoneSessionMode, state: NightHandoverState): Boolean =
        mode == PhoneSessionMode.PRODUCTA_BUDDY || state.snoozes < AB_MAX_SNOOZES

    /**
     * Looks at the night once. [nightId] is null outside the night (or when no night engages): everything resets. [sessionRunning] is a REAL
     * session (this phone's own or a PC's); the test session never counts.
     */
    fun step(state: NightHandoverState, nightId: String?, sessionRunning: Boolean, now: Instant): NightHandoverStep {
        if (nightId == null) return NightHandoverStep(NightHandoverState(), if (state.night != null) clearAll(state) else emptyList())
        val fresh = state.night != nightId
        var s = if (fresh) NightHandoverState(night = nightId) else state
        val effects = mutableListOf<NightHandoverEffect>()
        if (fresh && state.night != null) effects += clearAll(state)
        if (s.cancelled) return NightHandoverStep(s, effects)
        if (sessionRunning) {
            if (!s.reminderShown) effects += NightHandoverEffect.SHOW_REMINDER
            if (s.countdownShown) effects += NightHandoverEffect.CLEAR_COUNTDOWN
            // The start time stays: a later session never moves or restarts it.
            s = s.copy(sawSession = true, reminderShown = true, done = false, countdownShown = false)
        } else if (s.sawSession && !s.done) {
            val start = s.startAt
            if (start == null) {
                s = s.copy(startAt = now.plus(WAIT), countdownShown = true)
                effects += NightHandoverEffect.CLEAR_REMINDER
                effects += NightHandoverEffect.SHOW_COUNTDOWN
            } else if (!now.isBefore(start)) {
                // The start time has passed (also when a later session ends after it): the night starts at once.
                s = s.copy(done = true, countdownShown = false)
                effects += NightHandoverEffect.CLEAR_COUNTDOWN
                effects += NightHandoverEffect.CLEAR_REMINDER
                effects += NightHandoverEffect.NIGHT_STARTS
            } else if (!s.countdownShown) {
                // A later session ended inside the 5 minutes: the same start time, shown again.
                s = s.copy(countdownShown = true)
                effects += NightHandoverEffect.CLEAR_REMINDER
                effects += NightHandoverEffect.SHOW_COUNTDOWN
            }
        }
        return NightHandoverStep(s, effects)
    }

    /** True while the night is held back: a real session runs, tonight was cancelled, or the 5 minutes after the first session have not passed. */
    fun holds(state: NightHandoverState, nightId: String?, sessionRunning: Boolean, now: Instant): Boolean {
        if (nightId == null) return false
        if (sessionRunning) return true
        if (state.night != nightId) return false
        return state.cancelled || pendingStart(state, nightId, sessionRunning, now) != null
    }

    /** When the night starts, while the "starts in 5 minutes" card shows; null otherwise. */
    fun pendingStart(state: NightHandoverState, nightId: String?, sessionRunning: Boolean, now: Instant): Instant? =
        state.startAt?.takeIf { nightId != null && state.night == nightId && !state.done && !state.cancelled && !sessionRunning && now.isBefore(it) }

    /** Snooze: the start moves to [SNOOZE] from now. Null when no card shows, or AccountabiliBuddy's two Snoozes are used. */
    fun snooze(mode: PhoneSessionMode, state: NightHandoverState, nightId: String?, sessionRunning: Boolean, now: Instant): NightHandoverStep? {
        pendingStart(state, nightId, sessionRunning, now) ?: return null
        if (!canSnooze(mode, state)) return null
        return NightHandoverStep(state.copy(startAt = now.plus(SNOOZE), snoozes = state.snoozes + 1), listOf(NightHandoverEffect.SHOW_COUNTDOWN))
    }

    /** Cancel tonight: skips the night until its wake time. Null when the mode has no skip or no card shows. */
    fun cancel(mode: PhoneSessionMode, state: NightHandoverState, nightId: String?, sessionRunning: Boolean, now: Instant): NightHandoverStep? {
        if (!canCancel(mode)) return null
        pendingStart(state, nightId, sessionRunning, now) ?: return null
        return NightHandoverStep(state.copy(cancelled = true, startAt = null, countdownShown = false), listOf(NightHandoverEffect.CLEAR_COUNTDOWN, NightHandoverEffect.CLEAR_REMINDER))
    }

    private fun clearAll(state: NightHandoverState): List<NightHandoverEffect> =
        if (state.reminderShown || state.startAt != null) listOf(NightHandoverEffect.CLEAR_REMINDER, NightHandoverEffect.CLEAR_COUNTDOWN) else emptyList()
}
