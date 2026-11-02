package com.accountabilibuddy.companion.stats

import java.time.Duration
import java.time.LocalDate

/**
 * Deep work and drift as a daily average per week over the last weeks (the Focus graph), a port of the PC's FocusTrend. Off
 * days never pull an average down, and a week that hasn't started counting yet is a gap, not a zero.
 */
class FocusTrend private constructor(val weeks: List<TrendWeek>) {
    private val focus: List<Duration> = weeks.mapNotNull { it.dailyFocus }

    /** At least one week has deep work in it. */
    val hasData: Boolean = focus.any { it > Duration.ZERO }

    /** How many weeks have a value. */
    val trendCount: Int = focus.size

    /** The last weeks' daily average less the first weeks'; null until there are six weeks to compare. */
    val change: Duration? = if (focus.size >= EDGE_WEEKS * 2) average(focus.takeLast(EDGE_WEEKS)).minus(average(focus.take(EDGE_WEEKS))) else null

    /** The newest week with a value. */
    val latest: TrendWeek? get() = weeks.lastOrNull { it.dailyFocus != null }

    /** The week before [latest], when it has a value. */
    val previous: TrendWeek?
        get() {
            val index = latest?.let { weeks.indexOf(it) - 1 } ?: -1
            return if (index >= 0 && weeks[index].dailyFocus != null) weeks[index] else null
        }

    /** The average daily drift over every week with a value; null with none. */
    val driftAverage: Duration?
        get() {
            val drift = weeks.mapNotNull { it.dailyDrift }
            return if (drift.isEmpty()) null else average(drift)
        }

    companion object {
        private const val EDGE_WEEKS = 3

        /**
         * @param days One entry per date from the first with history up to today, oldest first.
         * @param driftPerDay Drift time per date; a date missing from it has none.
         * @param goal The daily goal: today, a workday not yet at it, isn't over and isn't counted (as in [ConsistencyStats]).
         * @param weeks How many weeks to cover, the current one last.
         */
        fun build(
            days: List<StatsDay>,
            driftPerDay: Map<LocalDate, Duration>,
            isWorkday: (LocalDate) -> Boolean,
            today: LocalDate,
            goal: Duration,
            weeks: Int,
        ): FocusTrend {
            val thisMonday = today.minusDays((today.dayOfWeek.value - 1).toLong())
            val firstDay = if (days.isEmpty()) today else days[0].date
            val result = mutableListOf<TrendWeek>()
            for (i in weeks - 1 downTo 0) {
                val monday = thisMonday.minusDays(7L * i)
                val counted = days
                    .filter { it.date >= monday && it.date < monday.plusDays(7) && !it.date.isAfter(today) && it.date >= firstDay }
                    .filter { isWorkday(it.date) || it.focused > Duration.ZERO }
                    .filter { !(it.date == today && isWorkday(it.date) && it.focused < goal) }
                if (counted.isEmpty()) {
                    result += TrendWeek(monday, null, null)
                    continue
                }

                val focus = counted.fold(Duration.ZERO) { sum, day -> sum.plus(day.focused) }
                val drift = counted.fold(Duration.ZERO) { sum, day -> sum.plus(driftPerDay[day.date] ?: Duration.ZERO) }
                result += TrendWeek(monday, focus.dividedBy(counted.size.toLong()), drift.dividedBy(counted.size.toLong()))
            }

            return FocusTrend(result)
        }

        private fun average(values: List<Duration>): Duration =
            if (values.isEmpty()) Duration.ZERO else Duration.ofNanos(values.map { it.toNanos() }.average().toLong())
    }
}
