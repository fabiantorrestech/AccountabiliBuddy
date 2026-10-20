package com.accountabilibuddy.companion.model

import java.time.LocalDate
import java.time.LocalTime
import java.util.UUID

/**
 * Planned time off on one date: all day, or only from [start] to [end] (past midnight when [end] isn't after
 * [start]). No scheduled block runs during it.
 */
data class TimeOff(val id: UUID, val date: LocalDate, val start: LocalTime? = null, val end: LocalTime? = null, val note: String? = null) {
    val isAllDay: Boolean get() = start == null || end == null

    fun window(): LocalWindow {
        val day = date.atStartOfDay()
        if (start == null || end == null) {
            return LocalWindow(day, day.plusDays(1))
        }

        val from = date.atTime(start)
        val to = date.atTime(end)
        return LocalWindow(from, if (to.isAfter(from)) to else to.plusDays(1))
    }
}
