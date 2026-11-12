package com.accountabilibuddy.companion.goals

import com.accountabilibuddy.companion.steps.StepsState
import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeekly
import com.accountabilibuddy.shared.goals.GoalExtraTiles
import com.accountabilibuddy.shared.goals.GoalFacts
import com.accountabilibuddy.shared.goals.GoalFactsBuilder
import com.accountabilibuddy.shared.goals.GoalLabelId
import com.accountabilibuddy.shared.goals.GoalLists
import com.accountabilibuddy.shared.goals.GoalSourceData
import com.accountabilibuddy.shared.goals.GoalSourceLine
import com.accountabilibuddy.shared.goals.GoalSourceLines
import com.accountabilibuddy.shared.goals.GoalTakesText
import com.accountabilibuddy.shared.goals.GoalTile
import com.accountabilibuddy.shared.goals.GoalTiles
import com.accountabilibuddy.shared.goals.GoalKind
import com.accountabilibuddy.shared.goals.GoalDirection
import com.accountabilibuddy.shared.sync.SyncFile
import java.time.Instant
import java.time.ZoneId
import java.util.UUID

/**
 * Everything one goal's page shows (round 2 R6b1), worked out off the main thread from the store and what the phone already holds
 * (the same inputs as [PhoneGoalsOverview]); never stored. [data] feeds the heatmap, the day panel and the trend.
 *
 * @property usesTrend Level goals (an amount to reach, such as a weight) show a trend and readings instead of the heatmap.
 */
data class PhoneGoalPage(
    val goal: Goal,
    val facts: GoalFacts,
    val data: GoalSourceData,
    val areaName: String?,
    val tiles: List<GoalTile>,
    val takes: GoalTakesText?,
    val sourceLines: List<GoalSourceLine>,
    val reminderCount: Int,
    val labelName: (UUID) -> String?,
) {
    val usesTrend: Boolean get() = goal.kind == GoalKind.Amount && goal.target.direction == GoalDirection.Reach

    companion object {
        /** Null when the goal is gone. [labels]: every label name on this phone's lists (ids come from [GoalLabelId.idFor]). */
        fun build(
            goalId: UUID,
            lists: GoalLists,
            display: GoalDisplay,
            files: List<SyncFile>,
            goalFiles: List<SyncFile>,
            steps: StepsState?,
            labels: List<String>,
            now: Instant,
            zone: ZoneId,
            weekly: GoalDeepWorkWeekly = GoalDeepWorkWeekly(),
        ): PhoneGoalPage? {
            val stored = lists.liveGoals.firstOrNull { it.id == goalId } ?: return null
            val goal = PhoneDeepWork.view(stored, PhoneDeepWork.pcDailyMinutes(goalFiles), weekly)
            val today = now.atZone(zone).toLocalDate()
            val data = PhoneGoalData.build(files, listOf(goal), lists.liveEntries, steps, now, zone)
            val facts = GoalFactsBuilder.build(goal, data, today, display.weekStart)
            val byId = labels.associateBy { GoalLabelId.idFor(it) }
            val labelName: (UUID) -> String? = { byId[it] }
            val extra = goal.extraTiles ?: if (display.extraTiles) GoalExtraTiles(streak = true, total = true) else GoalExtraTiles.NONE
            return PhoneGoalPage(
                goal = goal,
                facts = facts,
                data = data,
                areaName = lists.liveAreas.firstOrNull { it.id == goal.areaId }?.name,
                tiles = GoalTiles.build(facts, data, extra),
                takes = facts.whatItTakes?.let { GoalTakesText.of(it, goal, today) },
                sourceLines = GoalSourceLines.of(goal, facts.threshold, labelName),
                reminderCount = lists.reminders.count { it.goalId == goalId && it.isLive },
                labelName = labelName,
            )
        }
    }
}
