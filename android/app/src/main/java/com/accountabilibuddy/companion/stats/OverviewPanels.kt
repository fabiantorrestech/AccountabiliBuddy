package com.accountabilibuddy.companion.stats

import com.accountabilibuddy.companion.engine.DayTimeline
import com.accountabilibuddy.companion.model.LocalWindow
import java.time.Duration
import java.time.LocalDate

/**
 * The Overview view (board 5d): deep work per day for a week or a month, per hour for one day, with the days listed under it.
 * Returns null when no device has sent a session, so the page says so instead of drawing zeros that were never measured.
 */
object OverviewPanels {
    private const val GOAL_HEADROOM = 1.25

    /** @param extraCards Phone and Sleep tiles, built from this phone's own data (they are left out when it has none). */
    fun build(data: StatsData, period: StatsPeriod, extraCards: List<StatCard>): BarPanel? {
        if (!data.hasHistory) return null
        return if (period.kind == StatsRange.Day) day(data, period.from) else days(data, period, extraCards)
    }

    private fun days(data: StatsData, period: StatsPeriod, extraCards: List<StatCard>): BarPanel {
        val (from, to) = period.from to period.to
        val past = data.daysBetween(from, to)
        val hasFocus = past.any { it.focused > Duration.ZERO }
        val total = past.fold(Duration.ZERO) { sum, day -> sum.plus(day.focused) }
        val goal = data.goal
        val scale = maxOf(goal?.let { StatsFormat.minutes(it) * GOAL_HEADROOM } ?: 0.0, past.maxOfOrNull { StatsFormat.minutes(it.focused) } ?: 0.0, 1.0)
        val bars = mutableListOf<StatsBar>()
        var date = from
        while (!date.isAfter(to)) {
            val day = data.dayOf(date)
            val noBar = date.isAfter(data.today) || day == null
            val focused = day?.focused ?: Duration.ZERO
            val met = goal != null && focused >= goal
            val kind = if (met) BarKind.Met else if (date == data.today) BarKind.Accent else BarKind.Plan
            val label = StatsFormat.barLabel(period, date)
            bars += StatsBar(
                label, (StatsFormat.minutes(focused) / scale).toFloat(), kind, date == data.today, noBar,
                if (noBar) "${StatsFormat.dayText(date)} · no data" else "${StatsFormat.dayText(date)} · ${StatsFormat.duration(focused)}${if (met) " · goal met" else ""}",
                date.takeUnless { noBar },
            )
            date = date.plusDays(1)
        }

        val summary = goal?.let { ConsistencyStats.summarize(past, it, data.isWorkday, data.today) }
        // A past period is named by its dates: "this week" on last month would say something false.
        val words = StatsFormat.periodWords(period, data.today)
        val note = when {
            !hasFocus -> "deep work · nothing counted yet"
            summary != null && summary.workdaysCounted > 0 -> "deep work $words · ${summary.workdaysMet} of ${summary.workdaysCounted} workdays met"
            else -> "deep work $words"
        }
        return BarPanel(
            StatsFormat.hoursMinutes(total), note, bars, goal?.let { (StatsFormat.minutes(it) / scale).toFloat() }, goal?.let { "goal ${StatsFormat.duration(it)}" },
            extraCards + driftCard(data, from, to, hasFocus), rows(data, period), StatsDepth.caption(data, period),
        )
    }

    // One bar per hour of the selected day; a full bar is a full hour.
    private fun day(data: StatsData, day: LocalDate): BarPanel {
        val start = day.atStartOfDay()
        val total = data.dayOf(day)?.focused ?: Duration.ZERO
        val bars = (0 until 24).map { hour ->
            val window = LocalWindow(start.plusHours(hour.toLong()), start.plusHours(hour + 1L))
            val minutes = data.windows.mapNotNull { DayTimeline.clip(it, window) }.sumOf { Duration.between(it.start, it.end).toNanos() } / NANOS_PER_MINUTE
            val inFuture = day == data.today && window.start.isAfter(data.nowLocal)
            val now = day == data.today && window.contains(data.nowLocal)
            StatsBar(
                if (hour % 6 == 0) StatsFormat.hourShort(hour) else "", (minutes / 60.0).toFloat(),
                if (minutes <= 0) BarKind.Plan else if (now) BarKind.Accent else BarKind.Met, false, inFuture,
                "${StatsFormat.time(window.start)} · ${StatsFormat.duration(Duration.ofMinutes(Math.rint(minutes).toLong()))}",
            )
        }

        val goal = data.goal
        val note = when {
            goal == null -> "deep work"
            total >= goal -> "deep work · goal met"
            else -> "deep work · ${StatsFormat.duration(goal.minus(total))} to go"
        }
        // The day's own tiles (deep work, drift) sit under the chart in the day detail; the week's averages would only repeat them.
        return BarPanel(StatsFormat.hoursMinutes(total), note, bars, null, null, emptyList(), emptyList(), null)
    }

    // Drift is a number only with sessions to drift in; a stretch count of zero then is a real zero.
    private fun driftCard(data: StatsData, from: LocalDate, to: LocalDate, hasFocus: Boolean): List<StatCard> {
        if (!hasFocus) return emptyList()
        val visits = data.driftBetween(from, to)
        val time = visits.fold(Duration.ZERO) { sum, visit -> sum.plus(visit.length) }
        return listOf(
            if (visits.isEmpty()) StatCard("Drift", "0", "no drift in your sessions", StatsView.Drift)
            else StatCard("Drift", visits.size.toString(), "stretches off track · ${StatsFormat.duration(time)}", StatsView.Drift),
        )
    }

    // The days under the chart (board 5d). Up to a week lists every day that has happened, oldest first; a longer period lists the days with focus, newest first.
    private fun rows(data: StatsData, period: StatsPeriod): List<StatsDayRow> {
        val days = data.daysBetween(period.from, period.to)
        val shown = if (period.days <= 7) days else days.filter { it.focused > Duration.ZERO }.reversed()
        return shown.map { day ->
            val met = data.goal != null && day.focused >= data.goal
            val detail = when {
                day.focused <= Duration.ZERO -> "nothing counted"
                met -> "${StatsFormat.duration(day.focused)} · goal met"
                day.date == data.today -> "${StatsFormat.duration(day.focused)} so far"
                else -> StatsFormat.duration(day.focused)
            }
            StatsDayRow(day.date, StatsFormat.dayText(day.date), detail)
        }
    }

    private const val NANOS_PER_MINUTE = 60_000_000_000.0
}
