package com.accountabilibuddy.shared.goals

import java.time.DayOfWeek
import java.time.LocalTime

/**
 * Settings › Goals › Weekly review (the PC's `GoalReviewSettings`, round 2 R5.6; device-local): the day and time the review comes,
 * how long a snooze lasts, and whether the weekly tidy-up runs straight after it. Suggestions only: nothing here locks, waits or
 * feeds a guard. The phone keeps the choices now; the review itself arrives with the reminders (R6b3).
 *
 * @property day The day it comes; null = the week's first day (Options > Charts > Week starts on).
 * @property time The time it comes from (6:00 PM by default).
 * @property snoozeDays A snooze's length: 1 or 2 days.
 * @property tidyUpAfter Run the tidy-up straight after the review (off by default).
 */
data class GoalReviewSettings(
    val day: DayOfWeek? = null,
    val time: LocalTime = LocalTime.of(18, 0),
    val snoozeDays: Int = 1,
    val tidyUpAfter: Boolean = false,
) {
    /** The same settings with known values only. */
    fun clamped(): GoalReviewSettings = copy(snoozeDays = snoozeDays.coerceIn(MIN_SNOOZE_DAYS, MAX_SNOOZE_DAYS), time = time.withSecond(0).withNano(0))

    companion object {
        const val MIN_SNOOZE_DAYS = 1
        const val MAX_SNOOZE_DAYS = 2
    }
}
