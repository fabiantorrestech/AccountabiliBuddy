package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * One cell of the goal heatmap (the PC's `HeatCell`): its place (row = weekday from the week start, column = week), its ramp level
 * (0 to 4), the tip a long press shows ("Fri 10/30 · 0 min · missed"), the day (null for a future cell) and how it is marked.
 */
data class GoalHeatCell(
    val row: Int,
    val column: Int,
    val level: Int,
    val tip: String,
    val day: LocalDate?,
    val mark: GoalHeatCellMark,
    val progress: Double,
)
