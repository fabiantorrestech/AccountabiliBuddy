package com.accountabilibuddy.companion.stats

import java.time.LocalDate
import java.time.LocalDateTime
import java.time.temporal.ChronoUnit
import kotlin.math.ceil

/**
 * The Intensity view (board 19f): focus minutes by weekday and hour, the peak window, the second wind and the weakest hour,
 * and a line that says where deep work belongs. Null when the range has no focus.
 */
object IntensityPanels {
    private const val HOUR_STEP = 3

    fun build(data: StatsData, range: StatsRange): HeatPanel? {
        val to = data.today
        val wanted = to.minusDays(rangeDays(range) - 1L)
        val from = if (wanted.isBefore(data.firstData)) data.firstData else wanted
        val driftStarts = data.driftBetween(from, to).map { LocalDateTime.ofInstant(it.start, data.zone) }
        val grid = IntensityGrid.build(data.windows, from, to, driftStarts)
        if (grid.totalMinutes <= 0) return null

        val cells = mutableListOf<HeatCell>()
        for (row in 0 until IntensityGrid.ROWS) {
            for (hour in grid.firstHour..grid.lastHour) {
                val minutes = grid.minutes[row][hour]
                val focus = if (minutes <= 0) "no focus" else "${StatsFormat.duration(java.time.Duration.ofMinutes(Math.rint(minutes).toLong()))} of focus"
                cells += HeatCell(row, hour - grid.firstHour, grid.level(row, hour), "${StatsFormat.weekdayShort(row)} ${StatsFormat.hourRange(hour, hour + 1)} · $focus")
            }
        }

        val cards = mutableListOf<StatCard>()
        grid.peak?.let { cards += StatCard("Peak", StatsFormat.hourRange(it.startHour, it.endHour), "${percent(it.share)} of deep work") }
        grid.secondWind?.let { cards += StatCard("Second wind", StatsFormat.hourRange(it.startHour, it.endHour), "${percent(it.share)} of deep work") }
        grid.weakest?.let {
            val detail = if (it.driftCount > 0) "least focus · drift ×${it.driftCount}" else "least focus in your day"
            cards += StatCard("Weakest", StatsFormat.hourRange(it.startHour, it.endHour), detail)
        }

        val weeks = weeksIn(from, to)
        val helper = grid.peak?.let { "Your best hours are ${StatsFormat.hourRange(it.startHour, it.endHour)}: keep deep work there." }
        val labels = (grid.firstHour..grid.lastHour).map { if (it % HOUR_STEP == 0) StatsFormat.hourShort(it) else null }
        return HeatPanel(
            "Intensity", "When you focus best · $weeks ${StatsFormat.plural(weeks, "week")}", cells, IntensityGrid.ROWS, grid.lastHour - grid.firstHour + 1,
            (0 until IntensityGrid.ROWS).map { StatsFormat.weekdayShort(it) }, labels, cards,
            "Focus time added up over the last $weeks ${StatsFormat.plural(weeks, "week")}. Brighter means more.",
            helper, StatsDepth.caption(data, rangeDays(range)),
        )
    }

    private fun rangeDays(range: StatsRange): Int = when (range) {
        StatsRange.Week -> 7
        StatsRange.Month -> 56
        else -> 365
    }

    private fun weeksIn(from: LocalDate, to: LocalDate): Int = ceil((ChronoUnit.DAYS.between(from, to) + 1) / 7.0).toInt()

    private fun percent(share: Double): String = "${Math.rint(share * 100).toInt()}%"
}
