package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/** Which colour token a month line is drawn in (the app maps it to its theme; [None] draws nothing). */
enum class MonthLineToken { Faint, Line, Muted, None }

/**
 * HM1: the heatmaps' month lines, the PC's `GoalMonthLines`. One setting (this device only) that every heatmap with months uses.
 * Normal is the look before (the faint token), Lighter the line token, Darker the muted-text token; Off draws no line while the
 * month labels and the cell spacing stay as they were. [label] is what the controls say; the order of [entries] is the controls'.
 */
enum class GoalMonthLines(val label: String) {
    Darker("Darker"),
    Normal("Normal"),
    Lighter("Lighter"),
    Off("Off");

    val token: MonthLineToken
        get() = when (this) {
            Normal -> MonthLineToken.Faint
            Lighter -> MonthLineToken.Line
            Darker -> MonthLineToken.Muted
            Off -> MonthLineToken.None
        }

    companion object {
        val DEFAULT = Normal

        /** A stored name read back; anything unknown (or missing) is [DEFAULT]. */
        fun fromName(name: String?): GoalMonthLines = entries.firstOrNull { it.name == name } ?: DEFAULT
    }
}

/** A point on a heatmap's cell grid, in boundaries: [column] is the line left of that column, [row] the line above that row. */
data class MonthLinePoint(val column: Int, val row: Int)

/** Where a month starts in a week grid (7 rows): the column and row of its first day. */
data class MonthStart(val column: Int, val row: Int)

object GoalMonthLineRules {
    const val ROWS = 7

    /**
     * The staircase between an earlier month and one whose first day sits at ([column], [row]) (the PC's divider): down the right
     * side of that column to the row, across to the column's left side, down to the grid's foot. A month starting on row 0 is a
     * straight line (the first step has no length).
     */
    fun staircase(column: Int, row: Int, rows: Int = ROWS): List<MonthLinePoint> {
        val points = mutableListOf<MonthLinePoint>()
        fun add(point: MonthLinePoint) { if (points.lastOrNull() != point) points += point }
        add(MonthLinePoint(column + 1, 0))
        add(MonthLinePoint(column + 1, row))
        add(MonthLinePoint(column, row))
        add(MonthLinePoint(column, rows))
        return if (row == 0) points.drop(1) else points
    }

    /**
     * The months that begin inside a grid of week columns that carries no month list (Stats' Consistency year; cell number n is column
     * n / 7, row n % 7, so the days run down each column): the first day of every month after the grid's first day, found from any one
     * cell whose date is known ([column], [row], [date]). Empty when the grid has no such cell.
     */
    fun monthStarts(column: Int, row: Int, date: LocalDate, columns: Int, rows: Int = ROWS): List<MonthStart> {
        val first = date.minusDays((column.toLong() * rows) + row)
        return (1 until columns * rows).filter { first.plusDays(it.toLong()).dayOfMonth == 1 }.map { MonthStart(it / rows, it % rows) }
    }
}
