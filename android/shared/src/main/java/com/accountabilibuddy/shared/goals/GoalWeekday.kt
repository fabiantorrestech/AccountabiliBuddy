package com.accountabilibuddy.shared.goals

import java.time.DayOfWeek

/**
 * A day of the week as goal reminders store it: written by name ("Monday"), never as a number, the same text as the PC's
 * `GoalWeekday`. The order is .NET's `DayOfWeek` (Sunday first), so an older numbered value (0 = Sunday) still reads.
 */
enum class GoalWeekday {
    Sunday,
    Monday,
    Tuesday,
    Wednesday,
    Thursday,
    Friday,
    Saturday;

    /** The same day as a `java.time` day (where Monday is 1 and Sunday 7). */
    val dayOfWeek: DayOfWeek get() = if (this == Sunday) DayOfWeek.SUNDAY else DayOfWeek.of(ordinal)

    companion object {
        fun of(day: DayOfWeek): GoalWeekday = entries[day.value % 7]
    }
}
