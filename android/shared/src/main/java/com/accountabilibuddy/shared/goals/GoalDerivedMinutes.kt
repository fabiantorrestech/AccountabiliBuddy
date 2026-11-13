package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import kotlin.math.max

/**
 * The minutes a PC writes as derived entries (ADR 79; the PC's `GoalDerivedMinutes`): the #tag minutes a device counts beyond what the
 * phone's own sessions show, and the calendar minutes beyond focus. The counting subtracts a device's own share so it is never twice.
 */
object GoalDerivedMinutes {
    /** The extra #tag minutes of [ownSessions] on [day]: counted with tags minus counted without (what a phone would show of them). */
    fun focusLocalExtra(goal: Goal, ownSessions: List<GoalFocusSession>, day: LocalDate): Double {
        val rules = GoalSources.countedFor(goal)
        if (goal.isDeepWork || rules.none { it.automatic && it.type == GoalSourceType.FocusTag }) return 0.0
        val dayWindow = dayWindow(day)
        val local = GoalCounting.wholeMinutes(clip(GoalFocusAttribution.focusWindows(ownSessions, rules, true), dayWindow))
        val shape = GoalCounting.wholeMinutes(clip(GoalFocusAttribution.focusWindows(ownSessions, rules, false), dayWindow))
        return max(0.0, local - shape)
    }

    /** The calendar minutes of the goal's calendar rules on [day] beyond its focus (and tag) windows; 0 with none. */
    fun calendarExtra(goal: Goal, data: GoalSourceData, day: LocalDate): Double {
        val rules = GoalSources.countedFor(goal)
        if (goal.isDeepWork) return 0.0
        val dayWindow = dayWindow(day)
        val calendar = rules.filter { it.automatic && it.type == GoalSourceType.CalendarCategory }.flatMap { calendarWindows(it, data) }
        if (calendar.isEmpty()) return 0.0
        val focus = GoalFocusAttribution.focusWindows(data.focus, rules.filter { it.automatic }, data.tagsAvailable)
        return extra(clip(focus, dayWindow), clip(calendar, dayWindow))
    }

    /** The whole minutes [more] adds to [basis]. */
    internal fun extra(basis: List<GoalWindow>, more: List<GoalWindow>): Double =
        max(0.0, GoalCounting.wholeMinutes(basis + more) - GoalCounting.wholeMinutes(basis))

    internal fun calendarWindows(rule: GoalSourceRule, data: GoalSourceData): List<GoalWindow> {
        val windows = rule.calendarCategoryId?.let { data.calendarWindows?.get(it) } ?: return emptyList()
        return GoalTimeline.union(windows)
    }

    private fun dayWindow(day: LocalDate): GoalWindow {
        val start = day.atStartOfDay()
        return GoalWindow(start, start.plusDays(1))
    }

    private fun clip(windows: List<GoalWindow>, dayWindow: GoalWindow): List<GoalWindow> = windows.mapNotNull { GoalTimeline.clip(it, dayWindow) }
}
