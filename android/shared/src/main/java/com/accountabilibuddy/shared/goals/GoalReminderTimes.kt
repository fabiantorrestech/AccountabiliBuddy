package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import java.time.LocalTime
import java.time.OffsetDateTime
import java.time.ZoneId

/**
 * When a goal reminder rings (brief §5.9), the PC's `GoalReminderTimes` rule for rule: on each day its [GoalReminder.days] name, at its
 * local [GoalReminder.time] ([GoalLocalTime]); never for a moment before it was set or last changed ([GoalReminder.editedAt]); one that
 * doesn't repeat rings once (on its date, or on its first weekday after it was set).
 */
object GoalReminderTimes {
    /** The morning the "Nudge when the week falls behind" rings (brief §5.9: "the morning after"). */
    val NUDGE_TIME: LocalTime = LocalTime.of(9, 0)

    /** The moment [reminder] rings on [day]; null when it doesn't ring that day. */
    fun on(reminder: GoalReminder, day: LocalDate, zone: ZoneId): OffsetDateTime? {
        if (!reminder.isLive || !ringsOn(reminder, day, zone)) return null
        val at = GoalLocalTime.at(day, reminder.time, zone)
        return if (at.toInstant() < reminder.editedAt.toInstant()) null else at
    }

    /** The nudge's moment on [day] (9:00 AM local). */
    fun nudgeOn(day: LocalDate, zone: ZoneId): OffsetDateTime = GoalLocalTime.at(day, NUDGE_TIME, zone)

    private fun ringsOn(reminder: GoalReminder, day: LocalDate, zone: ZoneId): Boolean = when (reminder.days) {
        GoalReminderDays.EveryDay -> true
        GoalReminderDays.Today, GoalReminderDays.Date -> reminder.date == day
        GoalReminderDays.Weekdays -> {
            val weekdays = reminder.weekdays.orEmpty()
            when {
                weekdays.isEmpty() || GoalWeekday.of(day.dayOfWeek) !in weekdays -> false
                reminder.repeat -> true
                else -> firstOnce(reminder, weekdays, zone) == day
            }
        }
    }

    // Once: the first of its weekdays on or after the day it was set (a time already gone that day moves to the next one).
    private fun firstOnce(reminder: GoalReminder, weekdays: List<GoalWeekday>, zone: ZoneId): LocalDate? {
        val from = reminder.editedAt.atZoneSameInstant(zone).toLocalDate()
        var candidate = from
        while (candidate <= from.plusDays(7)) {
            if (GoalWeekday.of(candidate.dayOfWeek) in weekdays &&
                GoalLocalTime.at(candidate, reminder.time, zone).toInstant() >= reminder.editedAt.toInstant()
            ) {
                return candidate
            }
            candidate = candidate.plusDays(1)
        }
        return null
    }
}
