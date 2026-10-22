package com.accountabilibuddy.companion.model

import java.time.LocalTime

/** A row of the bar, as the Windows app numbers them in its "daybar" document. */
enum class BarRow { Calendar, Plan, Focus, Blockers }

/** A row and whether it shows. */
data class BarRowChoice(val row: BarRow, val shown: Boolean)

/**
 * Which stretch of the day the bar shows and its rows, as in the Windows app's "daybar" document: [rows] top to bottom
 * (null: the default), [calendarsOff] the calendars left off the Calendar row.
 */
data class DayBarSettings(
    val range: DayBarRange,
    val dayStart: LocalTime,
    val dayEnd: LocalTime,
    val rows: List<BarRowChoice>? = null,
    val calendarsOff: Set<String> = emptySet(),
) {
    /**
     * The rows the phone draws, top to bottom: the saved order (repeats dropped), then any row missing from it, shown,
     * in the default order; hidden rows and Blockers left out (the phone can't see the PC's blockers yet).
     */
    fun visibleRows(): List<BarRow> {
        val ordered = mutableListOf<BarRowChoice>()
        for (choice in rows.orEmpty()) {
            if (ordered.none { it.row == choice.row }) {
                ordered += choice
            }
        }

        BarRow.entries.filter { row -> ordered.none { it.row == row } }.forEach { ordered += BarRowChoice(it, true) }
        return ordered.filter { it.shown && it.row != BarRow.Blockers }.map { it.row }
    }

    companion object {
        val Default = DayBarSettings(DayBarRange.FitToDay, LocalTime.of(6, 0), LocalTime.MIDNIGHT)
    }
}
