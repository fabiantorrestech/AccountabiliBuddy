package com.accountabilibuddy.companion.stats

import com.accountabilibuddy.companion.model.LocalWindow
import java.time.Duration
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.temporal.ChronoUnit

/**
 * Focus minutes by weekday and hour of the day over a range (the Intensity view), with the hours the user focuses best in,
 * a second stretch that works, and the weakest hour in the working day. A port of the PC's IntensityGrid: the same rules
 * (a 2-hour peak, the second wind needs 30% of the peak, no windows under 60 minutes of focus in all).
 */
class IntensityGrid private constructor(
    /** Focus minutes, [Monday = 0 .. Sunday = 6][hour 0 .. 23]. */
    val minutes: Array<DoubleArray>,
    /** The first hour the grid shows (6 AM unless focus began earlier). */
    val firstHour: Int,
    /** The last hour the grid shows: 11 PM. */
    val lastHour: Int,
    driftPerHour: IntArray,
) {
    val totalMinutes: Double
    val maxCell: Double
    val peak: IntensityWindow?
    val secondWind: IntensityWindow?
    val weakest: IntensityWindow?

    init {
        val perHour = DoubleArray(24)
        var total = 0.0
        var max = 0.0
        for (row in 0 until ROWS) {
            for (hour in 0 until 24) {
                perHour[hour] += minutes[row][hour]
                total += minutes[row][hour]
                max = maxOf(max, minutes[row][hour])
            }
        }

        totalMinutes = total
        maxCell = max
        if (total < MIN_TOTAL_MINUTES) {
            peak = null
            secondWind = null
            weakest = null
        } else {
            peak = bestWindow(perHour, driftPerHour, excludeFrom = -1, excludeTo = -1)
            secondWind = peak?.let { first ->
                bestWindow(perHour, driftPerHour, first.startHour, first.endHour)?.takeIf { it.focusMinutes >= first.focusMinutes * 0.3 }
            }
            weakest = weakestHour(perHour, driftPerHour)
        }
    }

    /** 0 for no focus in the cell, up to 4 for the busiest. */
    fun level(row: Int, hour: Int): Int {
        val value = minutes[row][hour]
        if (value <= 0 || maxCell <= 0) return 0
        val share = value / maxCell
        return if (share <= 0.25) 1 else if (share <= 0.5) 2 else if (share <= 0.8) 3 else 4
    }

    private fun bestWindow(perHour: DoubleArray, driftPerHour: IntArray, excludeFrom: Int, excludeTo: Int): IntensityWindow? {
        var bestStart = -1
        var best = 0.0
        var hour = 0
        while (hour + WINDOW_HOURS <= 24) {
            if (excludeFrom >= 0 && hour < excludeTo && hour + WINDOW_HOURS > excludeFrom) {
                hour++
                continue
            }

            var sum = 0.0
            for (i in hour until hour + WINDOW_HOURS) sum += perHour[i]
            if (sum > best) {
                best = sum
                bestStart = hour
            }

            hour++
        }

        return if (bestStart < 0) null else window(bestStart, WINDOW_HOURS, perHour, driftPerHour)
    }

    // The quietest hour inside the stretch where the user usually focuses (not its first or last hour, which only mark its edges).
    private fun weakestHour(perHour: DoubleArray, driftPerHour: IntArray): IntensityWindow? {
        val busiest = perHour.max()
        val active = (0 until 24).filter { perHour[it] >= busiest * 0.2 }
        if (active.isEmpty() || active.last() - active.first() < 3) return null

        var weakest = -1
        var least = Double.MAX_VALUE
        for (hour in active.first() + 1 until active.last()) {
            if (perHour[hour] < least) {
                least = perHour[hour]
                weakest = hour
            }
        }

        return if (weakest < 0 || least >= busiest * 0.6) null else window(weakest, 1, perHour, driftPerHour)
    }

    private fun window(start: Int, hours: Int, perHour: DoubleArray, driftPerHour: IntArray): IntensityWindow {
        var focus = 0.0
        var drift = 0
        for (i in start until start + hours) {
            focus += perHour[i]
            drift += driftPerHour[i]
        }

        return IntensityWindow(start, start + hours, focus / totalMinutes, focus, drift)
    }

    companion object {
        /** Monday to Sunday. */
        const val ROWS = 7

        private const val WINDOW_HOURS = 2
        private const val MIN_TOTAL_MINUTES = 60.0

        /**
         * Builds the grid from the focus [windows] inside the dates [from] to [to].
         * @param driftStarts When each drift visit began (local time), to count them per hour.
         */
        fun build(windows: List<LocalWindow>, from: LocalDate, to: LocalDate, driftStarts: List<LocalDateTime>): IntensityGrid {
            val minutes = Array(ROWS) { DoubleArray(24) }
            val start = from.atStartOfDay()
            val end = to.plusDays(1).atStartOfDay()
            for (window in windows) {
                var at = if (window.start.isBefore(start)) start else window.start
                val until = if (window.end.isAfter(end)) end else window.end
                while (at.isBefore(until)) {
                    val hourEnd = at.truncatedTo(ChronoUnit.HOURS).plusHours(1)
                    val pieceEnd = if (hourEnd.isBefore(until)) hourEnd else until
                    minutes[at.dayOfWeek.value - 1][at.hour] += Duration.between(at, pieceEnd).toNanos() / NANOS_PER_MINUTE
                    at = pieceEnd
                }
            }

            val driftPerHour = IntArray(24)
            for (drift in driftStarts) {
                if (!drift.isBefore(start) && drift.isBefore(end)) driftPerHour[drift.hour]++
            }

            // The earliest hour with focus, when it is before 6 AM (the bound moves down as one is found).
            var first = 6
            for (row in 0 until ROWS) {
                var hour = 0
                while (hour < first) {
                    if (minutes[row][hour] > 0) first = hour
                    hour++
                }
            }

            return IntensityGrid(minutes, first, 23, driftPerHour)
        }

        private const val NANOS_PER_MINUTE = 60_000_000_000.0
    }
}
