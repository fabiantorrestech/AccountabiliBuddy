package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import java.util.Locale
import kotlin.math.abs
import kotlin.math.ceil

/**
 * Builds a [GoalTrend] for a level goal (an amount to reach, a weight) from the goals maths only: the charted days are
 * [GoalCounting]'s day values (so the "first reading of the day" and "your edit wins" rules are the numbers' own), the pace is
 * [GoalWhatItTakes.pace] over 13 weeks (the board's "3-month pace"), and the finish is the latest charted day plus what is left at
 * that pace. Phone only: the PC has no trend chart.
 */
object GoalTrends {
    const val PACE_WEEKS = 13

    /** A projection that would take longer than this to reach the target isn't drawn (it would only flatten the chart). */
    const val MAX_PROJECTION_DAYS = 730

    private val MONTHS = listOf("Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec")

    fun build(goal: Goal, data: GoalSourceData, today: LocalDate, range: GoalTrendRange): GoalTrend {
        val all = GoalCounting.history(goal, data, today).values.filter { it.hasValue }.sortedBy { it.day }.map { GoalTrendPoint(it.day, it.value) }
        val from = range.months?.let { today.minusMonths(it.toLong()) }
        val points = if (from == null) all else all.filter { it.day >= from }
        val unit = goal.target.unit
        val level = goal.target.targetAmount ?: goal.target.amount
        val latest = all.lastOrNull()
        val down = level != null && GoalCounting.reachesDown(goal, data)

        // The last 30 days' change (from the first reading in that window to the latest).
        val window = latest?.let { last -> all.filter { it.day >= last.day.minusDays(30) } }.orEmpty()
        val change = if (window.size >= 2) window.last().value - window.first().value else null
        val changeText = change?.let {
            val sign = if (it < 0) "−" else "+"
            "$sign${GoalTexts.value(abs(it), unit)} in 30 days"
        }
        val changeGood = if (change != null && level != null) (if (down) change < 0 else change > 0) else null

        val pace = if (level != null) GoalWhatItTakes.pace(goal, data, today, PACE_WEEKS, down) else null
        var projection = emptyList<GoalTrendPoint>()
        var banner: GoalTrendBanner? = null
        if (level != null && latest != null) {
            val remaining = if (down) latest.value - level else level - latest.value
            val date = goal.target.targetDate
            if (remaining <= 0) {
                banner = GoalTrendBanner("Target reached", "Your latest reading, ${GoalTexts.value(latest.value, unit)}, is at your target of ${GoalTexts.value(level, unit)}.", GoalTrendBanner.Tone.Teal)
            } else if (pace == null || pace <= 0) {
                banner = GoalTrendBanner(null, "Not enough readings yet to show a pace. A few more over a couple of weeks will draw it.", GoalTrendBanner.Tone.Neutral)
            } else {
                val days = ceil(remaining / pace - 1e-9).toLong()
                val finish = latest.day.plusDays(days)
                val perWeek = "${perWeekText(pace * 7)}${unitSuffix(unit)} a week"
                val at = "At your 3-month pace ($perWeek) you reach ${GoalTexts.value(level, unit)} around ${shortDate(finish, today)}"
                if (days <= MAX_PROJECTION_DAYS) projection = listOf(latest, GoalTrendPoint(finish, level))
                banner = when {
                    date == null -> GoalTrendBanner(null, "$at.", GoalTrendBanner.Tone.Neutral)
                    finish > date -> {
                        val late = (finish.toEpochDay() - date.toEpochDay()).toInt()
                        GoalTrendBanner("${GoalTexts.plural(late, "day")} behind your target", "$at, ${GoalTexts.plural(late, "day")} after your target.", GoalTrendBanner.Tone.Amber)
                    }
                    finish == date -> GoalTrendBanner("On track for your target", "$at, on your target date.", GoalTrendBanner.Tone.Teal)
                    else -> {
                        val early = (date.toEpochDay() - finish.toEpochDay()).toInt()
                        GoalTrendBanner("On track for your target", "$at, ${GoalTexts.plural(early, "day")} before your target.", GoalTrendBanner.Tone.Teal)
                    }
                }
            }
        }

        return GoalTrend(range, points, projection, level, goal.target.targetDate, latest?.value, changeText, changeGood, banner)
    }

    /** "Mar 3", with the year when it isn't this year ("Mar 3, 2027"). */
    fun shortDate(day: LocalDate, today: LocalDate): String =
        "${MONTHS[day.monthValue - 1]} ${day.dayOfMonth}" + if (day.year == today.year) "" else ", ${day.year}"

    // Up to two decimals, trailing zeros cut: 0.46, 1.2, 3.
    private fun perWeekText(value: Double): String {
        val text = String.format(Locale.ROOT, "%.2f", abs(value)).trimEnd('0').trimEnd('.')
        return text.ifEmpty { "0" }
    }

    private fun unitSuffix(unit: GoalUnit): String = when (unit) {
        GoalUnit.Lb -> " lb"
        GoalUnit.Kg -> " kg"
        else -> ""
    }
}
