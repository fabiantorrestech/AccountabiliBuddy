package com.accountabilibuddy.shared.goals

import java.time.Duration
import java.time.LocalDateTime

/**
 * A stretch of local wall-clock time, start included, end not (the PC's `LocalWindow`). Lengths are wall-clock lengths, as the
 * PC's Deep work card and guards count them (Q384: a session across a clock change reads as its wall-clock length).
 */
data class GoalWindow(val start: LocalDateTime, val end: LocalDateTime) {
    /** Its wall-clock length in minutes (fractions kept). */
    val minutes: Double get() = Duration.between(start, end).toNanos() / 60_000_000_000.0
}
