package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import java.time.LocalTime
import java.time.OffsetDateTime
import java.time.ZoneId

/**
 * A local date and time as a moment in a zone, the PC's `TaskReminderTimes.Local` rule for rule: a time a clock change skips moves on
 * by 30 minutes until it exists (not by the gap's length, as `java.time` would), and a time that happens twice takes the larger offset
 * (the first of the two moments).
 */
object GoalLocalTime {
    fun at(date: LocalDate, time: LocalTime, zone: ZoneId): OffsetDateTime {
        var local = date.atTime(time)
        val rules = zone.rules
        while (rules.getValidOffsets(local).isEmpty()) {
            local = local.plusMinutes(30)
        }

        val offset = rules.getValidOffsets(local).maxBy { it.totalSeconds }
        return OffsetDateTime.of(local, offset)
    }
}
