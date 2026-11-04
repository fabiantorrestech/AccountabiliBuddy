package com.accountabilibuddy.companion.stats

import com.accountabilibuddy.companion.steps.StepsState
import java.text.NumberFormat
import java.time.LocalDate

/**
 * The Steps view's numbers for a period, from the days this phone read from Health Connect (up to 30, read on the phone and shared only
 * with the user's own devices). A day Health Connect had no steps for has no bar, never a zero, and a period longer than those days
 * (or one before them) shows only what there is.
 */
object StepsPanels {
    private const val DAYS = 30
    private const val HEADROOM = 1.15

    /** Null with the switch off or no counted day in [period] (inside the last 30 days). */
    fun build(steps: StepsState, period: StatsPeriod, today: LocalDate): BarPanel? {
        if (!steps.enabled) return null
        val numbers = NumberFormat.getIntegerInstance()
        val byDay = steps.days.filter { !it.date.isAfter(today) && !it.date.isBefore(period.from) && !it.date.isAfter(period.to) }.associateBy { it.date }
        if (byDay.isEmpty()) return null

        val goal = steps.goal
        val scale = maxOf(goal * HEADROOM, byDay.values.maxOf { it.count }.toDouble())
        val bars = (0 until period.days).map { offset ->
            val date = period.from.plusDays(offset.toLong())
            val day = byDay[date]
            val kind = when {
                day == null -> BarKind.Plan
                day.count >= goal -> BarKind.Met
                date == today -> BarKind.Accent
                else -> BarKind.Plan
            }
            val text = when {
                date.isAfter(today) || day == null -> "${StatsFormat.dayText(date)} · no data"
                else -> "${StatsFormat.dayText(date)} · ${numbers.format(day.count)} steps" + if (day.count >= goal) " · goal met" else ""
            }
            StatsBar(StatsFormat.barLabel(period, date), ((day?.count ?: 0) / scale).toFloat(), kind, date == today, day == null, text, null)
        }

        // Today is a partial day, so it is left out of the average (it still has its bar and counts toward "goal met").
        val whole = byDay.filterKeys { it != today }.values
        val average = if (whole.isEmpty()) null else Math.rint(whole.sumOf { it.count }.toDouble() / whole.size).toInt()
        val met = byDay.values.count { it.count >= goal }
        val cards = listOf(
            StatCard("Goal met", "$met of ${byDay.size}", "${StatsFormat.plural(byDay.size, "day")} with steps counted"),
            StatCard("Best day", numbers.format(byDay.values.maxOf { it.count }), "steps"),
        )
        return BarPanel(
            average?.let { numbers.format(it) } ?: "–",
            if (average == null) "no whole day counted yet · today so far is on the chart"
            else "steps a day on average · ${whole.size} whole ${StatsFormat.plural(whole.size, "day")}, today left out",
            bars, (goal / scale).toFloat(), "goal ${numbers.format(goal)}", cards, emptyList(),
            if (byDay.size < period.days) "${byDay.size} of the period's ${period.days} days have a count. Steps are read from Health Connect for up to the last $DAYS days." else null,
        )
    }
}
