package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import java.time.LocalTime

/**
 * One line of an amount goal's readings list (brief §4.3 "Weight detail"): an entry with the day and time it carries, where it came
 * from, whether it is the one the chart uses for its day ([charted]) and why not when it isn't ([note]).
 *
 * @property time The reading's own time, else the time it was made (a hand entry has none of its own).
 * @property source "Typed in", "Health Connect".
 * @property note "charted, first of the day", "charted, your edit wins", "not charted, a later edit wins" and so on.
 * @property replaced For the hand edit the chart uses: the value the day would have had without it (board Goals-Phone-Weight:
 * "Edited by you · was 182.8 · your edit wins"); null when it replaced nothing or the same value.
 */
data class GoalReading(
    val entry: GoalEntry,
    val day: LocalDate,
    val time: LocalTime,
    val value: Double,
    val source: String,
    val charted: Boolean,
    val note: String,
    val replaced: Double? = null,
)
