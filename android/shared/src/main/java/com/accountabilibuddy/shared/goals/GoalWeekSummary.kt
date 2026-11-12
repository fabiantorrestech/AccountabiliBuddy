package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * The week's goal counts: the phone's Goals subtitle ("Week of Nov 8 · 5 of 8 on track", brief §4.3) and the PC's This week
 * card (`GoalThisWeekViewModel.Update`), by the same rule: every live goal not paused today is on track, behind or not due yet
 * this week (brief §5.4: a paused goal leaves every count).
 *
 * @property weekStart The first day of this week, on the user's week start.
 * @property total Active (not paused) goals counted.
 */
data class GoalWeekSummary(val weekStart: LocalDate, val onTrack: Int, val total: Int, val behind: Int, val notDue: Int) {
    /** "Week of Nov 8 · 5 of 8 on track". */
    fun subtitle(): String = "Week of ${MONTHS[weekStart.monthValue - 1]} ${weekStart.dayOfMonth} · $onTrack of $total on track"

    companion object {
        // .NET's invariant "MMM", as the PC's card writes its range.
        private val MONTHS = listOf("Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec")

        /** The counts over [goals]' facts for the week holding [today]. */
        fun of(goals: List<GoalFacts>, today: LocalDate, weekStart: GoalWeekStart): GoalWeekSummary {
            val active = goals.filter { !it.isPaused && it.status != GoalPeriodStatus.Paused }
            return GoalWeekSummary(
                weekStart = GoalPeriods.weekStartOf(today, weekStart),
                onTrack = active.count { it.status == GoalPeriodStatus.OnTrack },
                total = active.size,
                behind = active.count { it.status == GoalPeriodStatus.Behind },
                notDue = active.count { it.status == GoalPeriodStatus.NotDueYet },
            )
        }
    }
}
