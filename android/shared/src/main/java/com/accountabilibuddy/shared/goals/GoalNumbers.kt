package com.accountabilibuddy.shared.goals

/**
 * The one number a goal row shows on the right, its bar's share, its visual and its status pill (change note §4a), the PC's
 * `GoalNumbers` (App, `Services/Goals`) rule for rule: amber Behind; a muted "Due Fri" while a weekly goal isn't due yet; Paused.
 */
object GoalNumbers {
    /** "1h 10m / 2h", "8 / 15 min", "2 / 3", "6,240 / 8k", "182.4 lb", "4 / 7", "1h 10m today". */
    fun number(facts: GoalFacts): String {
        val goal = facts.goal
        val target = goal.target
        when {
            goal.kind == GoalKind.Habit -> {
                if (target.period == GoalPeriod.Day) {
                    val threshold = facts.threshold
                    return if (threshold != null) {
                        "${kotlin.math.floor(facts.todayState.minutes).toInt()} / ${GoalTexts.minutes(threshold)}"
                    } else if (facts.todayState.hit) {
                        "Done"
                    } else {
                        "Not yet"
                    }
                }

                return "${facts.period.progress.toInt()} / ${facts.period.required.toInt()}"
            }

            goal.kind == GoalKind.Amount && target.direction == GoalDirection.Reach -> {
                val latest = facts.recent.lastOrNull { it.hasValue }?.value
                return if (latest != null) GoalTexts.value(latest, target.unit) else "No reading yet"
            }

            goal.kind == GoalKind.Milestone && facts.target != null -> {
                val milestone = facts.target
                return "${GoalTexts.number(milestone.current, 0)} / ${GoalTexts.number(milestone.target, 0)}"
            }
        }

        val amount = target.amount
        if (amount == null) {
            val longTerm = facts.target
            if (longTerm != null) {
                return "${GoalTexts.value(longTerm.current, target.unit)} / ${GoalTexts.shortValue(longTerm.target, target.unit)}"
            }

            return "${GoalTexts.value(facts.todayState.value, target.unit)} today"
        }

        val done = if (target.period == GoalPeriod.Day) facts.todayState.value else facts.period.progress
        return "${GoalTexts.value(done, target.unit)} / ${GoalTexts.shortValue(amount, target.unit)}"
    }

    /** The bar's share (0 to 1) for goals drawn with a bar; NaN when there is nothing to measure against. */
    fun fraction(facts: GoalFacts): Double {
        val goal = facts.goal
        val milestone = facts.target
        if (goal.kind == GoalKind.Milestone && milestone != null && milestone.target > 0) {
            return (milestone.current / milestone.target).coerceIn(0.0, 1.0)
        }

        val amount = goal.target.amount
        if (amount != null && amount > 0) {
            val done = if (goal.target.period == GoalPeriod.Day) facts.todayState.value else facts.period.progress
            return (done / amount).coerceIn(0.0, 1.0)
        }

        val target = facts.target
        if (target != null && target.target > 0) {
            return (target.current / target.target).coerceIn(0.0, 1.0)
        }

        return Double.NaN
    }

    /** The row's visual for its kind. */
    fun visual(facts: GoalFacts): GoalRowVisual {
        val goal = facts.goal
        val target = goal.target
        return when {
            goal.kind == GoalKind.Habit && target.period == GoalPeriod.Day -> GoalRowVisual.Strip
            goal.kind == GoalKind.Habit && facts.periodCells.size in 1..7 -> GoalRowVisual.WeekDots
            goal.kind == GoalKind.Amount && target.direction == GoalDirection.Reach -> GoalRowVisual.Sparkline
            goal.kind == GoalKind.Amount && target.period == GoalPeriod.Day && target.amount != null -> GoalRowVisual.DayBars
            fraction(facts).isNaN() -> GoalRowVisual.None
            else -> GoalRowVisual.Bar
        }
    }

    /** The status pill's text and whether it is amber (Behind); null for none. */
    fun pill(facts: GoalFacts): Pair<String, Boolean>? {
        if (facts.isPaused) {
            return "Paused" to false
        }

        if (facts.status == GoalPeriodStatus.Behind) {
            return "Behind" to true
        }

        if (facts.status == GoalPeriodStatus.NotDueYet && facts.goal.target.period != GoalPeriod.Day && facts.hasPeriodTarget) {
            return "Due ${GoalTexts.weekday(facts.periodSpan.end)}" to false
        }

        return null
    }
}
