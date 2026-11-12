package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * The Target tile's "What it takes" lines (brief §5.6), the PC's `GoalWhatItTakesViewModel.Update`: "16h left · 81 days to Sun 1/31",
 * "Each day: about 12 min · each week: 1h 25m", "Your pace: 14 min a day (4 weeks) · done ~Sun 1/10" and the verdict (green or amber).
 */
data class GoalTakesText(val remaining: String, val each: String?, val pace: String?, val verdict: String, val green: Boolean, val amber: Boolean) {
    companion object {
        fun of(takes: GoalWhatItTakes, goal: Goal, today: LocalDate): GoalTakesText {
            val unit = goal.target.unit
            fun amount(value: Double) = if (unit == GoalUnit.Minutes) GoalWhatItTakes.minutesText(GoalWhatItTakes.friendlyMinutes(value)) else GoalTexts.value(value, unit)
            val left = "${amount(takes.remaining)} left"
            val days = takes.daysLeft
            val date = goal.target.targetDate
            val remaining = if (days != null && date != null) "$left · ${GoalTexts.plural(days, "day")} to ${GoalTexts.date(date, today)}" else left
            val eachDay = takes.eachDay
            val eachWeek = takes.eachWeek
            val each = if (eachDay != null && eachWeek != null) "Each day: about ${amount(eachDay)} · each week: ${amount(eachWeek)}" else null
            val perDay = takes.pacePerDay
            val pace = perDay?.let { "Your pace: ${amount(it)} a day (${GoalWhatItTakes.DEFAULT_PACE_WEEKS} weeks)" }
            val finish = takes.projectedFinish?.let { "done ~${GoalTexts.date(it, today)}" }
            val paceLine = if (pace == null && finish == null) null else listOfNotNull(pace, finish).joinToString(" · ")
            return when (takes.verdict) {
                GoalWhatItTakesVerdict.Met -> GoalTakesText(remaining, each, paceLine, "Target met.", true, false)
                GoalWhatItTakesVerdict.Covered -> GoalTakesText(remaining, each, paceLine, "Your daily goal already covers it.", true, false)
                GoalWhatItTakesVerdict.NeedsMore -> GoalTakesText(
                    remaining, each, paceLine, "About ${amount(takes.morePerDay ?: 0.0)} more a day, or ${amount(takes.morePerWeek ?: 0.0)} more a week.", false, true,
                )
                GoalWhatItTakesVerdict.DatePassed -> GoalTakesText(remaining, each, paceLine, "The date has passed.", false, true)
                GoalWhatItTakesVerdict.NoDate -> GoalTakesText(remaining, each, paceLine, "No date set: this is the pace, not a deadline.", false, false)
            }
        }
    }
}
