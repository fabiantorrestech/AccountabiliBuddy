package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * Period maths (brief §5.5), the PC's `GoalPeriods`: weeks start on the user's week start; two-week periods are counted in
 * pairs from the week the goal was created in; months are calendar months.
 */
object GoalPeriods {
    /** The first day of the week as .NET numbers it (Sunday 0 to Saturday 6). */
    fun firstDay(weekStart: GoalWeekStart): Int = when (weekStart) {
        GoalWeekStart.Monday -> 1
        GoalWeekStart.Saturday -> 6
        GoalWeekStart.Sunday -> 0
    }

    /** The first day of the week [day] is in. */
    fun weekStartOf(day: LocalDate, weekStart: GoalWeekStart): LocalDate {
        val offset = (dayNumber(day) - firstDay(weekStart) + 7) % 7
        return day.minusDays(offset.toLong())
    }

    /** The week [day] is in. */
    fun weekOf(day: LocalDate, weekStart: GoalWeekStart): GoalPeriodSpan {
        val start = weekStartOf(day, weekStart)
        return GoalPeriodSpan(start, start.plusDays(6))
    }

    /** The period of [period] that [day] is in; [createdOn] is the day the goal was created (two-week periods count from its week). */
    fun periodOf(period: GoalPeriod, day: LocalDate, weekStart: GoalWeekStart, createdOn: LocalDate): GoalPeriodSpan = when (period) {
        GoalPeriod.Day -> GoalPeriodSpan(day, day)
        GoalPeriod.Week -> weekOf(day, weekStart)
        GoalPeriod.TwoWeeks -> {
            val anchor = weekStartOf(createdOn, weekStart)
            val weeks = ((weekStartOf(day, weekStart).toEpochDay() - anchor.toEpochDay()) / 7).toInt()
            val pairs = Math.floorDiv(weeks, 2)
            val start = anchor.plusDays(pairs * 14L)
            GoalPeriodSpan(start, start.plusDays(13))
        }
        GoalPeriod.Month -> {
            val start = day.withDayOfMonth(1)
            GoalPeriodSpan(start, start.plusMonths(1).minusDays(1))
        }
    }

    /** The period just before [span]. */
    fun previous(period: GoalPeriod, span: GoalPeriodSpan, weekStart: GoalWeekStart, createdOn: LocalDate): GoalPeriodSpan =
        periodOf(period, span.start.minusDays(1), weekStart, createdOn)

    // java.time numbers Monday 1 to Sunday 7; .NET Sunday 0 to Saturday 6.
    private fun dayNumber(day: LocalDate): Int = day.dayOfWeek.value % 7
}
