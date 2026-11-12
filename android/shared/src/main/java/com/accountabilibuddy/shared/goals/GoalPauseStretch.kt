package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * A stretch a goal was paused (brief §5.4): neither hit nor missed, skipped by streaks, left out of "This week" and the goal's
 * age. Pausing applies at once, so the day it was paused is paused; turning it back on resumes from that day.
 *
 * @property from The first paused day.
 * @property resumedOn The day it was turned back on (that day counts again); null while it is still paused.
 */
data class GoalPauseStretch(val from: LocalDate, val resumedOn: LocalDate? = null) {
    /** Whether [day] is inside the stretch. */
    fun covers(day: LocalDate): Boolean = day >= from && (resumedOn == null || day < resumedOn)
}
