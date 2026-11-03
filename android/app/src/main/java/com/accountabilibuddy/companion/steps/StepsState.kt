package com.accountabilibuddy.companion.steps

import com.accountabilibuddy.shared.sync.SyncStepDay
import com.accountabilibuddy.shared.sync.SyncSteps
import java.time.Instant
import java.time.LocalDate

/**
 * Daily steps on this phone: whether "Track daily steps" is on, the goal, and what the last Health Connect read gave.
 *
 * @property readAt When Health Connect was last read; null before the first read.
 * @property days The last read's days (up to [StepsStore.KEPT_DAYS]), oldest first; a day without data is not listed.
 * @property problem Why nothing can be read right now (switch on, but no permission or no Health Connect); [StepsProblem.None] otherwise.
 */
data class StepsState(
    val enabled: Boolean = false,
    val goal: Int = SyncSteps.DEFAULT_GOAL,
    val readAt: Instant? = null,
    val days: List<SyncStepDay> = emptyList(),
    val problem: StepsProblem = StepsProblem.None,
) {
    /** Today's count; null when Health Connect had none for today at the last read. */
    fun countOn(date: LocalDate): Int? = days.firstOrNull { it.date == date }?.count
}
