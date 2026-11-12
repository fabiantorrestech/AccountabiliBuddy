package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import java.time.format.DateTimeFormatter
import java.util.Locale
import kotlin.math.abs

/**
 * The facts the review's suggestion used, as pills (brief §5.8), the PC's `GoalReviewViewModel.FactsOf` line for line: "Nothing for 3
 * weeks" and "2 days behind" and "Behind in 2 of the last 3" (amber), a level goal's "182.4 lb · −1.2 in 30 days" (else "+12% in 30
 * days"), "Pace: 25 min a day" / "Pace: 0.46 lb a week", "At this pace: Mar 3"; "Not much history yet" when none.
 */
object GoalReviewFacts {
    private val FINISH: DateTimeFormatter = DateTimeFormatter.ofPattern("MMM d", Locale.US)

    fun of(goal: Goal, suggestion: GoalReviewSuggestion, data: GoalSourceData, today: LocalDate): List<GoalReviewFact> {
        val facts = mutableListOf<GoalReviewFact>()
        if (suggestion.quiet) facts += GoalReviewFact("Nothing for 3 weeks", amber = true)
        if (suggestion.behindDays > 0) {
            facts += GoalReviewFact(if (suggestion.behindDays == 1) "1 day behind" else "${suggestion.behindDays} days behind", amber = true)
        }
        if (suggestion.behindPeriods >= 2) {
            facts += GoalReviewFact("Behind in ${suggestion.behindPeriods} of the last ${suggestion.periodsChecked}", amber = true)
        }

        val unit = goal.target.unit
        val change = if (goal.kind == GoalKind.Amount && goal.target.direction == GoalDirection.Reach) readingChange(goal, data, today) else null
        val percent = suggestion.changePercent30
        if (change != null) {
            facts += GoalReviewFact("${GoalTexts.value(change.first, unit)} · ${signed(change.second, unit)} in 30 days")
        } else if (percent != null) {
            facts += GoalReviewFact("${if (percent >= 0) "+" else "−"}${abs(Math.rint(percent)).toLong()}% in 30 days")
        }

        val pace = suggestion.pacePerDay
        if (pace != null && pace > 0) {
            facts += GoalReviewFact(if (unit == GoalUnit.Minutes) "Pace: ${GoalTexts.minutes(pace)} a day" else "Pace: ${GoalTexts.value(pace * 7, unit)} a week")
        }

        suggestion.projectedFinish?.let { facts += GoalReviewFact("At this pace: ${it.format(FINISH)}") }
        if (facts.isEmpty()) facts += GoalReviewFact("Not much history yet")
        return facts
    }

    // A level goal's latest reading and its change over the last 30 days; null without two readings.
    private fun readingChange(goal: Goal, data: GoalSourceData, today: LocalDate): Pair<Double, Double>? {
        val readings = GoalCounting.days(goal, data, today.minusDays(30), today, today).filter { it.hasValue }
        if (readings.size < 2) return null
        return readings.last().value to (readings.last().value - readings.first().value)
    }

    private fun signed(change: Double, unit: GoalUnit): String {
        val magnitude = GoalTexts.value(abs(change), unit)
        return if (change < 0) "−$magnitude" else "+$magnitude"
    }
}
