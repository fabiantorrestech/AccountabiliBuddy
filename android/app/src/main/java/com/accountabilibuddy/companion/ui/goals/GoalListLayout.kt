package com.accountabilibuddy.companion.ui.goals

import com.accountabilibuddy.companion.goals.GoalDisplay
import com.accountabilibuddy.companion.goals.GoalListView
import com.accountabilibuddy.companion.goals.PhoneGoalsOverview
import java.util.UUID

/**
 * What the Goals list draws for a view and an area chip (change note §4a, the PC's list rules):
 * - **By area:** every live area in its order (the chosen one only, with an area chip) with its active goals; folded areas keep their
 *   header; the paused goals are left out with a "N paused goals · show them in All" link.
 * - **Current:** the active goals in one block, no sections.
 * - **All:** every goal in one block, the paused ones last.
 *
 * @property count "YOUR GOALS 8": the goals the view shows (All counts the paused ones).
 * @property pausedHidden Paused goals By area or Current leaves out.
 */
data class GoalListLayout(val sections: List<GoalListSection>, val count: Int, val pausedHidden: Int) {
    companion object {
        fun of(overview: PhoneGoalsOverview, display: GoalDisplay, areaFilter: UUID?): GoalListLayout {
            val shown = overview.facts.filter { areaFilter == null || it.goal.areaId == areaFilter }
            val active = shown.filter { !it.isPaused }
            val paused = shown.filter { it.isPaused }
            return when (display.view) {
                GoalListView.ByArea -> {
                    val sections = overview.areas.filter { areaFilter == null || it.id == areaFilter }.map { area ->
                        GoalListSection(area, active.filter { it.goal.areaId == area.id }, area.id in display.collapsed)
                    }
                    GoalListLayout(sections, active.size, paused.size)
                }
                GoalListView.Current -> GoalListLayout(listOf(GoalListSection(null, active, false)), active.size, paused.size)
                GoalListView.All -> GoalListLayout(listOf(GoalListSection(null, active + paused, false)), shown.size, 0)
            }
        }
    }
}
