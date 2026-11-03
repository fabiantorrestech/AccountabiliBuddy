package com.accountabilibuddy.companion.stats

import com.accountabilibuddy.companion.steps.StepsState
import java.text.NumberFormat
import java.time.LocalDate

/**
 * The Steps view's numbers, from the days this phone read from Health Connect (up to 30, read on the phone and shared only with the user's
 * own devices). A day Health Connect had no steps for has no bar, never a zero.
 */
object StepsPanels {
    private const val DAYS = 30
    private const val HEADROOM = 1.15

    /** Null with the switch off or no counted day in the last 30 days. */
    fun build(steps: StepsState, today: LocalDate): BarPanel? {
        if (!steps.enabled) return null
        val numbers = NumberFormat.getIntegerInstance()
        val byDay = steps.days.filter { !it.date.isAfter(today) && !it.date.isBefore(today.minusDays(DAYS - 1L)) }.associateBy { it.date }
        if (byDay.isEmpty()) return null

        val goal = steps.goal
        val scale = maxOf(goal * HEADROOM, byDay.values.maxOf { it.count }.toDouble())
        val bars = (DAYS - 1 downTo 0).map { back ->
            val date = today.minusDays(back.toLong())
            val day = byDay[date]
            val kind = when {
                day == null -> BarKind.Plan
                day.count >= goal -> BarKind.Met
                date == today -> BarKind.Accent
                else -> BarKind.Plan
            }
            val text = when {
                day == null -> "${StatsFormat.dayText(date)} · no data"
                else -> "${StatsFormat.dayText(date)} · ${numbers.format(day.count)} steps" + if (day.count >= goal) " · goal met" else ""
            }
            // A letter under every 5th bar only, so 30 bars do not crowd the axis.
            StatsBar(if (back % 5 == 0) StatsFormat.weekdayLetter(date) else "", ((day?.count ?: 0) / scale).toFloat(), kind, date == today, day == null, text, null)
        }

        val average = Math.rint(byDay.values.sumOf { it.count }.toDouble() / byDay.size).toInt()
        val met = byDay.values.count { it.count >= goal }
        val cards = listOf(
            StatCard("Goal met", "$met of ${byDay.size}", "${StatsFormat.plural(byDay.size, "day")} with steps counted"),
            StatCard("Best day", numbers.format(byDay.values.maxOf { it.count }), "steps"),
        )
        return BarPanel(
            numbers.format(average), "steps a day on average · ${byDay.size} ${StatsFormat.plural(byDay.size, "day")} counted by Health Connect",
            bars, (goal / scale).toFloat(), "goal ${numbers.format(goal)}", cards, emptyList(),
            if (byDay.size < DAYS) "Steps are read from Health Connect for up to the last $DAYS days: ${byDay.size} of them have a count." else null,
        )
    }
}
