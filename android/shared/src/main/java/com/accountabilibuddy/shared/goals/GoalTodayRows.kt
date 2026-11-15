package com.accountabilibuddy.shared.goals

import java.util.UUID
import kotlin.math.floor

/**
 * Which goals the phone's "Today's goals" shows and what each row says (boards Main, B-Phone-Tray-Open; brief §4.1, §4.3 "Show on
 * Today"). The choice is the PC's rule (`TodayGoalsViewModel.Refresh`): every live goal with Show on Today on that is not paused
 * today, in the Goals list's order. The rows' own texts and controls are the phone boards'. Nothing here feeds a guard.
 */
object GoalTodayRows {
    /**
     * @param facts Every live goal's facts, in the Goals list's order.
     * @param areaName An area's name by id, or null.
     * @param inSession A focus session runs: Deep work's line says the session counts.
     */
    fun of(facts: List<GoalFacts>, areaName: (UUID) -> String?, inSession: Boolean): GoalTodayCard {
        val rows = facts.filter { it.goal.isLive && it.goal.showOnToday && !it.isPaused }.map { row(it, areaName(it.goal.areaId), inSession) }
        return GoalTodayCard(rows, rows.count { !it.done })
    }

    private fun row(facts: GoalFacts, area: String?, inSession: Boolean): GoalTodayRow {
        val goal = facts.goal
        val control = control(facts)
        val fraction = GoalNumbers.fraction(facts).takeUnless { it.isNaN() } ?: 0.0
        val threshold = facts.threshold
        return GoalTodayRow(
            goalId = goal.id,
            name = goal.name,
            icon = goal.icon,
            color = goal.color,
            subLine = subLine(facts, area, control, inSession),
            control = control,
            checked = control == GoalTodayControl.Tick && facts.todayState.hit,
            number = if (control == GoalTodayControl.Tick && goal.target.period == GoalPeriod.Day && threshold != null && !GoalTexts.tickedByBeeminder(facts.todayState, threshold)) {
                "${floor(facts.todayState.minutes).toInt()}/${floor(threshold).toInt()}"
            } else {
                null
            },
            fraction = if (control == GoalTodayControl.Bar) fraction else 0.0,
            done = when (control) {
                GoalTodayControl.Tick -> facts.todayState.hit
                GoalTodayControl.Log -> facts.todayState.hasValue
                // An "at most" goal asks for nothing to do (a full bar means the limit is used up, not that the goal is finished; FX-C).
                GoalTodayControl.Bar -> fraction >= 1.0 || GoalTexts.isLimit(goal)
                GoalTodayControl.None -> true
            },
            isDeepWork = goal.isDeepWork,
        )
    }

    /** A habit ticks; a level to reach (a weight) or an amount with nothing to measure is logged; everything else with a share is a bar. */
    internal fun control(facts: GoalFacts): GoalTodayControl {
        val goal = facts.goal
        val measurable = !GoalNumbers.fraction(facts).isNaN()
        return when {
            goal.isDeepWork -> if (measurable) GoalTodayControl.Bar else GoalTodayControl.None
            goal.kind == GoalKind.Habit -> GoalTodayControl.Tick
            goal.kind == GoalKind.Amount && goal.target.direction == GoalDirection.Reach -> GoalTodayControl.Log
            measurable -> GoalTodayControl.Bar
            goal.kind == GoalKind.Amount -> GoalTodayControl.Log
            else -> GoalTodayControl.None
        }
    }

    private fun subLine(facts: GoalFacts, area: String?, control: GoalTodayControl, inSession: Boolean): String {
        val goal = facts.goal
        val target = goal.target
        return when (control) {
            GoalTodayControl.Tick -> {
                val detail = if (target.period == GoalPeriod.Day) {
                    if (facts.streak > 0) "${facts.streak}-day streak" else null
                } else {
                    "${facts.period.progress.toInt()} of ${facts.period.required.toInt()} ${GoalTexts.periodName(target.period)}"
                }
                listOfNotNull(area, detail).joinToString(" · ").ifEmpty { GoalTexts.targetLine(goal, facts.today) }
            }
            GoalTodayControl.Log -> {
                val last = facts.recent.lastOrNull { it.hasValue }
                when {
                    last == null -> "Nothing logged yet"
                    last.day == facts.today -> "Logged today · ${GoalTexts.value(last.value, target.unit)}"
                    else -> "Not logged since ${if (facts.today.toEpochDay() - last.day.toEpochDay() < 7) GoalTexts.weekday(last.day) else GoalTexts.date(last.day, facts.today)}"
                }
            }
            GoalTodayControl.Bar -> {
                val amount = target.amount
                val line = if (amount != null && amount > 0) {
                    // An "at most" goal that counts days holds a per day amount; its period's cap is the prorated one (FX-C).
                    val perDay = target.period == GoalPeriod.Day || (GoalTexts.isLimit(goal) && GoalCounting.countsDays(goal))
                    val done = if (perDay) facts.todayState.value else facts.period.progress
                    val gap = amount - done
                    val head = "${GoalTexts.value(done, target.unit)} of ${GoalTexts.shortValue(amount, target.unit)}"
                    // An "at most" goal has room left under its limit, never "to go" or "done" (FX-C).
                    if (GoalTexts.isLimit(goal)) "$head · ${GoalTexts.limitLeft(goal, done, if (perDay) amount else facts.period.required)}"
                    else if (gap > 0) "$head · ${GoalTexts.value(gap, target.unit)} to go" else "$head · done"
                } else {
                    GoalNumbers.number(facts)
                }
                if (goal.isDeepWork && inSession) "${line.substringBefore(" · ")} · this session counts" else line
            }
            GoalTodayControl.None -> "${GoalTexts.value(facts.todayState.value, target.unit)} today"
        }
    }
}
