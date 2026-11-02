package com.accountabilibuddy.companion.stats

import java.time.Duration
import java.time.LocalDate

/**
 * Streaks and rates over days of deep work, a port of the PC's ConsistencyStats. Only workdays count: an off day never
 * counts against the user, and today doesn't until its goal is met (it isn't over yet).
 */
object ConsistencyStats {
    /** The heat levels a day can have, 0 (nothing) to this. */
    const val MAX_LEVEL = 4

    /**
     * @param days One entry per date, oldest first.
     * @param isWorkday Whether a date is a workday in the day plan.
     */
    fun summarize(days: List<StatsDay>, goal: Duration, isWorkday: (LocalDate) -> Boolean, today: LocalDate): ConsistencySummary {
        val counted = mutableListOf<Pair<StatsDay, Boolean>>()
        var hasOffDays = false
        for (day in days) {
            if (!isWorkday(day.date)) {
                hasOffDays = true
                continue
            }

            val met = day.focused >= goal
            if (day.date.isAfter(today) || (day.date == today && !met)) continue
            counted += day to met
        }

        // The run left after the loop is the streak that ends at the latest counted workday.
        var best = 0
        var run = 0
        for ((_, met) in counted) {
            run = if (met) run + 1 else 0
            best = maxOf(best, run)
        }

        val perWeekday = counted
            .groupBy { it.first.date.dayOfWeek }
            .map { (day, entries) -> Triple(day, entries.size, entries.count { it.second }) }
            .filter { it.second >= 2 }
            .sortedWith(compareByDescending<Triple<java.time.DayOfWeek, Int, Int>> { it.third.toDouble() / it.second }.thenByDescending { it.second })
        val reliable = perWeekday.firstOrNull()
        return ConsistencySummary(
            run,
            best,
            counted.size,
            counted.count { it.second },
            reliable?.first,
            if (reliable == null) 0 else Math.rint(100.0 * reliable.third / reliable.second).toInt(),
            hasOffDays,
        )
    }

    /** 0 for no focus, 1 under half the goal, 2 under the goal, 3 for the goal met, 4 for half as much again. */
    fun level(focused: Duration, goal: Duration): Int {
        if (focused.isZero || focused.isNegative) return 0
        val share = focused.toNanos().toDouble() / goal.toNanos()
        return if (share < 0.5) 1 else if (share < 1) 2 else if (share < 1.5) 3 else MAX_LEVEL
    }
}
