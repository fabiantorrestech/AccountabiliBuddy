package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/** One period of a goal: [start] to [end], both days included. */
data class GoalPeriodSpan(val start: LocalDate, val end: LocalDate) {
    /** How many days it has. */
    val length: Int get() = (end.toEpochDay() - start.toEpochDay() + 1).toInt()

    fun contains(day: LocalDate): Boolean = day >= start && day <= end

    /** Its days, first to last. */
    fun days(): List<LocalDate> {
        val days = mutableListOf<LocalDate>()
        var day = start
        while (day <= end) {
            days += day
            day = day.plusDays(1)
        }

        return days
    }
}
