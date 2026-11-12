package com.accountabilibuddy.shared.goals

/**
 * One month's part of a goal heatmap ([GoalHeatmapGrid]): the columns its shown days fill, for a label centred over them, and
 * where the month starts, for its staircase divider; the PC's `GoalHeatmapMonth`.
 *
 * @property month 1 to 12.
 * @property label "May" (three letters, English, as the PC's invariant culture writes it).
 * @property firstColumn The column of the month's first shown day.
 * @property lastColumn The column of the month's last shown day.
 * @property firstRow The row of the month's first shown day.
 * @property hasDivider True when the month's first shown day is its 1st and an earlier month is shown before it: one continuous
 * staircase divider runs between them, turning at ([firstColumn], [firstRow]).
 */
data class GoalHeatmapMonth(
    val year: Int,
    val month: Int,
    val label: String,
    val firstColumn: Int,
    val lastColumn: Int,
    val firstRow: Int,
    val hasDivider: Boolean,
) {
    /** The middle of the month's columns, in columns from the grid's left edge (its label is centred here). */
    val centreColumn: Double get() = (firstColumn + lastColumn + 1) / 2.0
}
