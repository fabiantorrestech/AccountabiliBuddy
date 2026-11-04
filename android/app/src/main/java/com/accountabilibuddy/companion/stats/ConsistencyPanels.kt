package com.accountabilibuddy.companion.stats

import java.time.Duration
import java.time.LocalDate

/**
 * The Consistency view (board 19e): a cell per day, weeks across and Monday to Sunday down, brighter for more deep work, with
 * the streak, the share of workdays that met the goal and the most reliable weekday. Weekends and off days never count against
 * the user. Null when the range has no focus or no PC has sent its daily goal (the cells are levels of that goal).
 */
object ConsistencyPanels {
    fun build(data: StatsData, period: StatsPeriod): HeatPanel? {
        val goal = data.goal ?: return null
        val (rangeFrom, to) = period.from to period.to
        val days = data.daysBetween(rangeFrom, to)
        if (days.all { it.focused <= Duration.ZERO }) return null

        // The grid starts at the first week with history, so the cells fill the width, and a day before the first one is "no data",
        // never a missed day.
        val from = if (rangeFrom.isBefore(data.firstData)) data.firstData else rangeFrom
        val gridStart = StatsFormat.mondayOf(from)
        val columns = ((StatsFormat.mondayOf(to).toEpochDay() - gridStart.toEpochDay()) / 7).toInt() + 1
        val cells = mutableListOf<HeatCell>()
        for (offset in 0 until columns * 7) {
            val date = gridStart.plusDays(offset.toLong())
            val day = if (!date.isBefore(from) && !date.isAfter(to) && !date.isAfter(data.today)) data.dayOf(date) else null
            val description = if (day == null) "" else "${StatsFormat.dayText(date)} · ${StatsFormat.duration(day.focused)}${if (day.focused >= goal) " · goal met" else ""}"
            cells += HeatCell(offset % 7, offset / 7, if (day == null) -1 else ConsistencyStats.level(day.focused, goal), description, date.takeIf { day != null })
        }

        val summary = ConsistencyStats.summarize(days, goal, data.isWorkday, data.today)
        // On a past period the run is the one that ended with it, not the streak the user has now.
        val current = !to.isBefore(data.today)
        val cards = mutableListOf(
            StatCard(
                if (current) "Streak" else "Run at period end", "${summary.currentStreak} ${StatsFormat.plural(summary.currentStreak, "day")}",
                "workdays in a row${if (current) "" else " up to ${StatsFormat.shortDate(to)}"} · best ${summary.bestStreak}",
            ),
            StatCard("Goal met", "${summary.metPercent}%", "${summary.workdaysMet} of ${summary.workdaysCounted} workdays"),
        )
        summary.mostReliableDay?.let { day ->
            val name = StatsFormat.weekdayName(day)
            cards += StatCard("Most reliable", name, "${summary.mostReliablePercent}% of ${name}s")
        }

        if (summary.hasOffDays) cards += StatCard("Off days", "skipped", "not counted against you")
        val subtitle = when {
            period.kind == StatsRange.Year && current -> "Goal met · last $columns ${StatsFormat.plural(columns, "week")}"
            else -> "Goal met · ${StatsFormat.periodWords(period, data.today)}"
        }
        return HeatPanel(
            "Consistency", subtitle, cells, 7, columns, null, monthLabels(gridStart, columns, period.kind), cards,
            "Rows are Monday to Sunday. Brighter means more deep work that day.", null, StatsDepth.caption(data, period),
        )
    }

    // A month's name over the column its first day falls in (and over the first column); only for the long range.
    private fun monthLabels(gridStart: LocalDate, columns: Int, kind: StatsRange): List<String?>? {
        if (kind != StatsRange.Year) return null
        val month = java.time.format.DateTimeFormatter.ofPattern("MMM", java.util.Locale.getDefault())
        return (0 until columns).map { column ->
            val monday = gridStart.plusDays(column * 7L)
            val first = (0 until 7).map { monday.plusDays(it.toLong()) }.firstOrNull { it.dayOfMonth == 1 }
            if (first != null) first.format(month) else if (column == 0) monday.format(month) else null
        }
    }
}
