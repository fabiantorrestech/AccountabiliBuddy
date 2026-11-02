package com.accountabilibuddy.companion.stats

import java.time.DayOfWeek

/**
 * How steadily the daily deep work goal was met over a range of days (the Consistency view); the PC's ConsistencySummary.
 *
 * @property currentStreak Workdays in a row the goal was met, up to the latest one counted.
 * @property bestStreak The longest run of workdays met in the range.
 * @property workdaysCounted Workdays that count: not off days, and not today while its goal isn't met yet.
 * @property workdaysMet Of those, the ones that reached the goal.
 * @property mostReliableDay The weekday whose workdays met the goal most often (at least two of them); null for none.
 * @property mostReliablePercent How often that weekday met it, 0 to 100.
 * @property hasOffDays The range has off days, which never count against the user.
 */
data class ConsistencySummary(
    val currentStreak: Int,
    val bestStreak: Int,
    val workdaysCounted: Int,
    val workdaysMet: Int,
    val mostReliableDay: DayOfWeek?,
    val mostReliablePercent: Int,
    val hasOffDays: Boolean,
) {
    /** Share of the counted workdays that met the goal, 0 to 100 (half to even, as the PC rounds); 0 with none counted. */
    val metPercent: Int get() = if (workdaysCounted == 0) 0 else Math.rint(100.0 * workdaysMet / workdaysCounted).toInt()
}
