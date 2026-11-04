package com.accountabilibuddy.companion.stats

import java.time.LocalDate

/**
 * A span of whole local dates a Stats view shows, with the kind it was made as (the PC's StatsPeriod). Everything is plain dates,
 * never clock times, so a daylight-saving change cannot shift a boundary. Calendar kinds (Day, Week, Month) step to the
 * neighbouring calendar period; the others (Year, Last 7/30/90, Custom) step a whole window of the same length.
 */
data class StatsPeriod(val kind: StatsRange, val from: LocalDate, val to: LocalDate) {
    /** How many dates the period covers. */
    val days: Int get() = (to.toEpochDay() - from.toEpochDay()).toInt() + 1

    /** The period just before this one: the previous day, week or month, or a window of the same length ending the day before. */
    fun previous(): StatsPeriod = when (kind) {
        StatsRange.Day, StatsRange.Week, StatsRange.Month -> of(kind, from.minusDays(1))
        else -> StatsPeriod(kind, from.minusDays(days.toLong()), from.minusDays(1))
    }

    /** The period just after this one; a window that would run past [today] ends on today instead. */
    fun next(today: LocalDate): StatsPeriod = when (kind) {
        StatsRange.Day, StatsRange.Week, StatsRange.Month -> of(kind, to.plusDays(1))
        else -> {
            val end = minOf(to.plusDays(days.toLong()), today)
            StatsPeriod(kind, end.plusDays(1L - days), end)
        }
    }

    /** True when a later period has begun: this one ends before [today]. */
    fun hasNext(today: LocalDate): Boolean = to.isBefore(today)

    /** True when an earlier period touches the data: this one starts after [firstData], the first date with history. */
    fun hasPrevious(firstData: LocalDate): Boolean = from.isAfter(firstData)

    companion object {
        /**
         * The period of a kind. For Week and Month [anchor] is any date inside it; for the rolling kinds, Year and Day it is the
         * last (or only) date. Custom has no rule of its own (it opens on the last 7 days): use [custom] for picked dates.
         */
        fun of(kind: StatsRange, anchor: LocalDate): StatsPeriod = when (kind) {
            StatsRange.Week -> StatsFormat.mondayOf(anchor).let { StatsPeriod(kind, it, it.plusDays(6)) }
            StatsRange.Month -> anchor.withDayOfMonth(1).let { StatsPeriod(kind, it, it.plusMonths(1).minusDays(1)) }
            StatsRange.Year -> StatsPeriod(kind, anchor.minusDays(364), anchor)
            StatsRange.Last7 -> StatsPeriod(kind, anchor.minusDays(6), anchor)
            StatsRange.Last30 -> StatsPeriod(kind, anchor.minusDays(29), anchor)
            StatsRange.Last90 -> StatsPeriod(kind, anchor.minusDays(89), anchor)
            StatsRange.Custom -> custom(anchor.minusDays(6), anchor)
            StatsRange.Day -> StatsPeriod(StatsRange.Day, anchor, anchor)
        }

        /** The period a kind opens on: the current one. */
        fun current(kind: StatsRange, today: LocalDate): StatsPeriod = of(kind, today)

        /** Two picked dates, in either order. */
        fun custom(a: LocalDate, b: LocalDate): StatsPeriod =
            if (a <= b) StatsPeriod(StatsRange.Custom, a, b) else StatsPeriod(StatsRange.Custom, b, a)
    }
}
