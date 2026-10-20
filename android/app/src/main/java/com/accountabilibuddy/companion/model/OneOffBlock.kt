package com.accountabilibuddy.companion.model

import java.time.LocalDate
import java.time.LocalTime
import java.util.UUID

/** Extra blocked time on one date with a profile; past midnight when [end] isn't after [start]. */
data class OneOffBlock(val id: UUID, val date: LocalDate, val start: LocalTime, val end: LocalTime, val profileId: UUID) {
    fun window(): LocalWindow {
        val from = date.atTime(start)
        val to = date.atTime(end)
        return LocalWindow(from, if (to.isAfter(from)) to else to.plusDays(1))
    }
}
