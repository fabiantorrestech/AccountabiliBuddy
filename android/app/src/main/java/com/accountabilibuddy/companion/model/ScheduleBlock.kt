package com.accountabilibuddy.companion.model

import java.time.DayOfWeek
import java.time.LocalTime
import java.util.UUID

/**
 * A weekly blocked-time window, e.g. Monday 09:00 to 12:00 with the "Coding" profile.
 * If [end] is not after [start], the block runs past midnight into the next day.
 */
data class ScheduleBlock(val id: UUID, val day: DayOfWeek, val start: LocalTime, val end: LocalTime, val profileId: UUID) {
    val crossesMidnight: Boolean get() = !end.isAfter(start)
}
