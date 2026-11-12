package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * The "What it takes" text (brief §5.6), the PC's `GoalWhatItTakesViewModel.Update`: head "To hit 50h by Sun 1/31" ("What it takes" when
 * the goal has no amount), remaining "15h 50m to go · 81 days left", the one-line [each] "Each day: about 12 min · each week: about 1h 25m",
 * the labelled [rows] (Each day, Each week, Your pace), [pace] "At this pace you hit 50h around Sun 1/10" and the verdict (green or amber).
 */
data class GoalTakesText(
    val head: String,
    val remaining: String,
    val each: String?,
    val rows: List<GoalTakesRow>,
    val pace: String?,
    val verdict: String,
    val green: Boolean,
    val amber: Boolean,
) {
    companion object {
        fun of(takes: GoalWhatItTakes, goal: Goal, today: LocalDate): GoalTakesText {
            val unit = goal.target.unit
            fun amount(value: Double) = if (unit == GoalUnit.Minutes) GoalWhatItTakes.minutesText(GoalWhatItTakes.friendlyMinutes(value)) else GoalTexts.value(value, unit)
            val target = goal.target.targetAmount?.let { amount(it) }
            val by = goal.target.targetDate
            val head = when {
                target != null && by != null -> "To hit $target by ${GoalTexts.date(by, today)}"
                target != null -> "To hit $target"
                else -> "What it takes"
            }
            val left = "${amount(takes.remaining)} to go"
            val days = takes.daysLeft
            val remaining = if (days != null) "$left · ${GoalTexts.plural(days, "day")} left" else left
            val eachDay = takes.eachDay
            val eachWeek = takes.eachWeek
            val each = if (eachDay != null && eachWeek != null) "Each day: about ${amount(eachDay)} · each week: about ${amount(eachWeek)}" else null
            val rows = mutableListOf<GoalTakesRow>()
            if (eachDay != null && eachWeek != null) {
                rows.add(GoalTakesRow("Each day", "about ${amount(eachDay)}"))
                rows.add(GoalTakesRow("Each week", "about ${amount(eachWeek)}"))
            }
            takes.pacePerDay?.let { rows.add(GoalTakesRow("Your pace", "${amount(it * 7)} a week")) }
            val pace = takes.projectedFinish?.let { "At this pace you hit ${target ?: "it"} around ${GoalTexts.date(it, today)}" }
            fun make(verdict: String, green: Boolean, amber: Boolean) = GoalTakesText(head, remaining, each, rows, pace, verdict, green, amber)
            return when (takes.verdict) {
                GoalWhatItTakesVerdict.Met -> make("Target met.", true, false)
                GoalWhatItTakesVerdict.Covered -> {
                    val line = GoalTexts.targetLine(goal, today)
                    make(if (line.contains(" a ")) "Your $line already covers it" else "Your target already covers it", true, false)
                }
                GoalWhatItTakesVerdict.NeedsMore ->
                    make("About ${amount(takes.morePerDay ?: 0.0)} more a day, or ${amount(takes.morePerWeek ?: 0.0)} more a week.", false, true)
                GoalWhatItTakesVerdict.DatePassed -> make("The date has passed.", false, true)
                GoalWhatItTakesVerdict.NoDate -> make("No date set: this is the pace, not a deadline.", false, false)
            }
        }
    }
}
