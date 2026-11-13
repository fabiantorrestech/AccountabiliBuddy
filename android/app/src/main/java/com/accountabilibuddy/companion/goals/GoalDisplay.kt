package com.accountabilibuddy.companion.goals

import com.accountabilibuddy.shared.goals.GoalWeekStart
import java.util.UUID

/**
 * How this phone shows the goals (round 2 R6; the PC's `goals.ui` and `goals.settings`): the list's view, the folded areas, the week
 * start (Sunday by default), the heatmap's day labels (Some, Q373) and whether goal pages show the extra tiles (off). Per device,
 * never synced; nothing in it is private text (areas are named by id only).
 */
data class GoalDisplay(
    val view: GoalListView = GoalListView.ByArea,
    val collapsed: Set<UUID> = emptySet(),
    val weekStart: GoalWeekStart = GoalWeekStart.Sunday,
    val heatmapLabels: GoalHeatmapLabels = GoalHeatmapLabels.Some,
    val extraTiles: Boolean = false,
    /** HM1: the month lines of every heatmap with months (Normal). */
    val monthLines: com.accountabilibuddy.shared.goals.GoalMonthLines = com.accountabilibuddy.shared.goals.GoalMonthLines.DEFAULT,
)
