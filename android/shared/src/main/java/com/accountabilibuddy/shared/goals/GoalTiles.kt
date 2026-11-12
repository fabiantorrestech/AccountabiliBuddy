package com.accountabilibuddy.shared.goals

import kotlin.math.abs
import kotlin.math.floor
import kotlin.math.max
import kotlin.math.min

/**
 * The goal page's tiles, the PC's `GoalDetailViewModel.BuildTiles` rule for rule (Today → This period → Target, then Streak and
 * Total when switched on), worked out from [GoalFacts] and the goal's own history. A waiting Deep work change isn't here: the phone
 * holds none (its daily target is the PC's, read only, ADR 29).
 */
object GoalTiles {
    /** [extra]: the goal's own Extra tiles choice, else the Options' one ([GoalExtraTiles.NONE] when neither is set). */
    fun build(facts: GoalFacts, data: GoalSourceData, extra: GoalExtraTiles): List<GoalTile> {
        val tiles = mutableListOf(today(facts), period(facts))
        val progress = facts.target
        if (progress != null) tiles += target(facts, progress)
        if (extra.streak) tiles += streak(facts)
        if (extra.total) tiles += total(facts, data)
        return tiles
    }

    private fun clamp(value: Double): Double = min(1.0, max(0.0, value))

    private fun today(facts: GoalFacts): GoalTile {
        val goal = facts.goal
        val today = facts.todayState
        val unit = goal.target.unit
        val threshold = facts.threshold
        if (goal.kind == GoalKind.Habit && threshold != null) {
            return GoalTile(
                "today", floor(today.minutes).toInt().toString(), "of ${GoalTexts.minutes(threshold)}", "Minutes today",
                if (today.hit) {
                    if (today.byHand) "Done · ticked by hand" else "Done · ticked itself"
                } else {
                    "${GoalTexts.minutes(max(0.0, threshold - today.minutes))} to go · ticks itself at ${GoalTexts.minutes(threshold)}"
                },
                if (threshold > 0) clamp(today.minutes / threshold) else 0.0, null, null, false, null,
            )
        }

        return when (goal.kind) {
            GoalKind.Habit -> GoalTile(
                "today", if (today.hit) "Done" else "Not yet", null, "Today", if (today.hit) null else "Tick it in the day panel or on Today",
                null, null, null, false, null,
            )
            GoalKind.Time -> {
                val daily = if (goal.target.period == GoalPeriod.Day) goal.target.amount else null
                GoalTile(
                    "today", GoalTexts.minutes(today.value), daily?.let { "of ${GoalTexts.minutes(it)}" },
                    if (goal.isDeepWork) "Focus today" else "Minutes today",
                    when {
                        daily != null -> if (today.value >= daily) "Met today" else "${GoalTexts.minutes(daily - today.value)} to go"
                        goal.isDeepWork && goal.target.amount == null -> "Tracking only: no daily target"
                        else -> null
                    },
                    if (daily != null) (if (daily > 0) clamp(today.value / daily) else 0.0) else null, null, null, false, null,
                )
            }
            else -> {
                val daily = if (goal.target.period == GoalPeriod.Day && goal.target.direction != GoalDirection.Reach) goal.target.amount else null
                GoalTile(
                    "today", if (today.hasValue) GoalTexts.value(today.value, unit) else "–",
                    daily?.let { "of ${GoalTexts.shortValue(it, unit)}" },
                    if (goal.kind == GoalKind.Amount && goal.target.direction == GoalDirection.Reach) "Today's reading" else "Today",
                    if (today.hasValue) null else "Nothing logged yet today",
                    if (daily != null) (if (daily > 0) clamp(today.value / daily) else 0.0) else null, null, null, false, null,
                )
            }
        }
    }

    private fun period(facts: GoalFacts): GoalTile {
        val goal = facts.periodGoal
        val period = facts.period
        val name = GoalTexts.periodName(goal.target.period)
        val pace = when (facts.status) {
            GoalPeriodStatus.OnTrack -> " · on pace"
            GoalPeriodStatus.Behind -> " · behind"
            GoalPeriodStatus.NotDueYet -> " · not due yet"
            GoalPeriodStatus.Paused -> " · paused"
        }
        // This period's total value (the week's minutes for Deep work), from the days held.
        val weekTotal = if (facts.periodCells.isNotEmpty()) facts.recent.filter { facts.periodSpan.contains(it.day) }.sumOf { it.value } else period.progress
        val amount = goal.target.amount
        if (GoalCounting.countsDays(goal)) {
            val times = goal.target.period != GoalPeriod.Day && goal.target.weeklyDays == null && goal.kind == GoalKind.Habit
            return GoalTile(
                "period", period.progress.toInt().toString(), "of ${period.required.toInt()}", "${if (times) "Times" else "Days"} $name$pace",
                if (goal.kind == GoalKind.Time) "${GoalTexts.minutes(weekTotal)} $name" else null, null, null, null, false, facts.periodCells,
            )
        }

        if (amount != null && goal.target.direction != GoalDirection.Reach) {
            return GoalTile(
                "period", GoalTexts.value(period.progress, goal.target.unit), "of ${GoalTexts.shortValue(amount, goal.target.unit)}",
                "${name[0].uppercaseChar()}${name.substring(1)}$pace", null, if (amount > 0) clamp(period.progress / amount) else 0.0, null, null, false, null,
            )
        }

        val reach = goal.target.direction == GoalDirection.Reach
        return GoalTile(
            "period", if (reach) GoalTexts.value(period.progress, goal.target.unit) else GoalTexts.value(weekTotal, goal.target.unit), null,
            if (reach) "Latest $name" else "${if (goal.target.unit == GoalUnit.Minutes) "Time" else "Total"} $name", null, null, null, null, false, facts.periodCells,
        )
    }

    private fun target(facts: GoalFacts, progress: GoalTargetProgress): GoalTile {
        val goal = facts.goal
        val unit = goal.target.unit
        val from = if (goal.target.direction == GoalDirection.Reach) progress.start ?: progress.current else 0.0
        val span = progress.target - from
        val fraction = if (abs(span) < 1e-9) (if (progress.met) 1.0 else 0.0) else clamp((progress.current - from) / span)
        val expected = progress.expectedToday
        val mark = if (expected != null && abs(span) > 1e-9) clamp((expected - from) / span) else null
        val status = if (progress.met) "Met" else when (progress.status) {
            GoalPeriodStatus.OnTrack -> "On track"
            GoalPeriodStatus.Behind -> "Behind"
            else -> null
        }
        val date = goal.target.targetDate
        return GoalTile(
            "target", GoalTexts.value(progress.current, unit), "of ${GoalTexts.value(progress.target, unit)}",
            if (date != null) "Target · by ${GoalTexts.date(date, facts.today)}" else "Target",
            facts.whatItTakes?.projectedFinish?.let { "done ~${GoalTexts.date(it, facts.today)}" },
            fraction, mark, status, !progress.met && progress.status == GoalPeriodStatus.Behind, null,
        )
    }

    private fun streak(facts: GoalFacts): GoalTile = GoalTile(
        "streak", facts.streak.toString(), null, if (facts.goal.target.period == GoalPeriod.Day) "Day streak" else "Streak (periods in a row)",
        null, null, null, null, false, null,
    )

    private fun total(facts: GoalFacts, data: GoalSourceData): GoalTile {
        val goal = facts.goal
        val total = GoalCounting.history(goal, data, facts.today).values.sumOf { if (goal.kind == GoalKind.Habit) (if (it.hit) 1.0 else 0.0) else it.value }
        return GoalTile(
            "total", if (goal.kind == GoalKind.Habit) GoalTexts.number(total, 0) else GoalTexts.value(total, goal.target.unit), null,
            if (goal.kind == GoalKind.Habit) "Days done in all" else "Total so far", null, null, null, null, false, null,
        )
    }
}
