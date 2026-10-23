package com.accountabilibuddy.companion.nudge

import java.time.Duration
import java.time.Instant
import java.time.LocalDate

/**
 * The phone's full-screen nudge (the "take over the screen" switch): what the user chose last and when it may come back.
 *
 * @property lastShown When the card last took over the screen.
 * @property snoozedUntil "Snooze 5 min": not before this.
 * @property startingUntil "I'm starting on my PC": not before this (enough to sit down and start a session).
 * @property snoozeDay The day [snoozes] counts; @property snoozes how many snoozes that day.
 * @property dayOffAskedAt "Not working today" was chosen: its wait counts from here.
 * @property dayOff A day with no nudges on the phone, once that wait was over.
 */
data class NudgeState(
    val takeoverOn: Boolean = true,
    val lastShown: Instant? = null,
    val snoozedUntil: Instant? = null,
    val startingUntil: Instant? = null,
    val snoozeDay: LocalDate? = null,
    val snoozes: Int = 0,
    val dayOffAskedAt: Instant? = null,
    val dayOff: LocalDate? = null,
)

/** The card's rules, plain Kotlin so the checks run them. */
object NudgeRules {
    val SNOOZE: Duration = Duration.ofMinutes(5)
    val STARTING: Duration = Duration.ofMinutes(10)
    val DAY_OFF_WAIT: Duration = Duration.ofMinutes(10)
    const val FREE_SNOOZES = 2
    const val REASON_LENGTH = 15

    /**
     * The card may take over now: it's on, [inWork] (a work stretch, no session, not paused), not a day off, not
     * snoozed or "starting", and at least [repeatMinutes] since it last showed (5 by default; 0: once a stretch, from
     * [stretchStart]).
     */
    fun due(state: NudgeState, inWork: Boolean, stretchStart: Instant?, now: Instant, today: LocalDate, repeatMinutes: Int): Boolean {
        if (!state.takeoverOn || !inWork || state.dayOff == today) return false
        if (state.snoozedUntil != null && now < state.snoozedUntil) return false
        if (state.startingUntil != null && now < state.startingUntil) return false
        val last = state.lastShown ?: return true
        if (stretchStart != null && last < stretchStart) return true
        return repeatMinutes > 0 && Duration.between(last, now) >= Duration.ofMinutes(repeatMinutes.toLong())
    }

    /** Snoozes taken today. */
    fun snoozesToday(state: NudgeState, today: LocalDate): Int = if (state.snoozeDay == today) state.snoozes else 0

    /** True once the free snoozes are used: another one needs a reason. */
    fun snoozeNeedsReason(state: NudgeState, today: LocalDate): Boolean = snoozesToday(state, today) >= FREE_SNOOZES

    fun snooze(state: NudgeState, now: Instant, today: LocalDate): NudgeState =
        state.copy(snoozedUntil = now.plus(SNOOZE), snoozeDay = today, snoozes = snoozesToday(state, today) + 1)

    fun starting(state: NudgeState, now: Instant): NudgeState = state.copy(startingUntil = now.plus(STARTING))

    /** Time left on the "Not working today" wait; null when not asked today. */
    fun dayOffLeft(state: NudgeState, now: Instant, today: LocalDate, zone: java.time.ZoneId): Duration? {
        val asked = state.dayOffAskedAt?.takeIf { it.atZone(zone).toLocalDate() == today } ?: return null
        return Duration.between(now, asked.plus(DAY_OFF_WAIT)).coerceAtLeast(Duration.ZERO)
    }
}
