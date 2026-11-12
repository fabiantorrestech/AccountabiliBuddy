package com.accountabilibuddy.companion.goals

import com.accountabilibuddy.companion.steps.StepsState
import com.accountabilibuddy.shared.goals.GoalArea
import com.accountabilibuddy.shared.goals.GoalFacts
import com.accountabilibuddy.shared.goals.GoalFactsBuilder
import com.accountabilibuddy.shared.goals.GoalLists
import com.accountabilibuddy.shared.goals.GoalWeekSummary
import com.accountabilibuddy.shared.sync.SyncFile
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId

/**
 * Everything the phone's Goals list shows, worked out at once off the main thread ([build]): the live areas in their order, every
 * live goal's [GoalFacts] (area order, then the goal's own), Deep work as its view (the PC's daily goal, read only), and the week's
 * counts for the subtitle. Computed from the store and what the phone already holds; never stored.
 *
 * @property pcDailyMinutes The PC's daily goal Deep work shows; null when no PC file has carried one yet.
 */
data class PhoneGoalsOverview(
    val today: LocalDate,
    val areas: List<GoalArea>,
    val facts: List<GoalFacts>,
    val summary: GoalWeekSummary,
    val pcDailyMinutes: Int?,
) {
    companion object {
        /**
         * @param files Every received device's file, removed devices' kept copies included (focus, as Stats counts it).
         * @param goalFiles The files of the devices still in the group: Deep work's daily target is the newest PC's among them (as Today).
         */
        fun build(
            lists: GoalLists,
            display: GoalDisplay,
            files: List<SyncFile>,
            goalFiles: List<SyncFile>,
            steps: StepsState?,
            now: Instant,
            zone: ZoneId,
        ): PhoneGoalsOverview {
            val today = now.atZone(zone).toLocalDate()
            val pcDaily = PhoneDeepWork.pcDailyMinutes(goalFiles)
            val areas = lists.liveAreas
            val areaOrder = areas.withIndex().associate { (index, area) -> area.id to index }
            val goals = lists.liveGoals
                .map { PhoneDeepWork.view(it, pcDaily) }
                .sortedWith(compareBy({ areaOrder[it.areaId] ?: Int.MAX_VALUE }, { it.position }))
            val data = PhoneGoalData.build(files, goals, lists.liveEntries, steps, now, zone)
            val facts = goals.map { GoalFactsBuilder.build(it, data, today, display.weekStart) }
            return PhoneGoalsOverview(today, areas, facts, GoalWeekSummary.of(facts, today, display.weekStart), pcDaily)
        }
    }
}
