package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import java.time.ZoneId

/**
 * How long a goal has been going (change note §4a), the PC's `GoalAge`: from the day it was created to today, minus its paused
 * days. Units: days under 2 weeks, then weeks under 2 months, then months, then "1 year 2 months". The tooltip reads
 * "Since Mon 5/4/2026 · 191 days (27 weeks)", adding "· paused 46 days" when there were any.
 *
 * @property days Days going (paused days left out).
 * @property pausedDays Paused days between its creation and today.
 * @property short "6 months", for "6 months going".
 * @property tooltip "Since Mon 5/4/2026 · 191 days (27 weeks) · paused 46 days".
 */
data class GoalAge(val days: Int, val pausedDays: Int, val short: String, val tooltip: String) {
    companion object {
        // .NET's invariant short day names, Sunday first.
        private val DAY_NAMES = listOf("Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat")

        fun forGoal(goal: Goal, today: LocalDate, zone: ZoneId): GoalAge {
            val created = goal.createdOn(zone)
            val calendarDays = maxOf(0L, today.toEpochDay() - created.toEpochDay()).toInt()
            var paused = 0
            var day = created
            while (day < today) {
                if (goal.isPausedOn(day)) {
                    paused++
                }

                day = day.plusDays(1)
            }

            val days = calendarDays - paused
            val since = "Since ${DAY_NAMES[created.dayOfWeek.value % 7]} ${created.monthValue}/${created.dayOfMonth}/${created.year}"
            val tooltip = "$since · ${plural(days, "day")}" +
                (if (days >= 7) " (${plural(days / 7, "week")})" else "") +
                (if (paused > 0) " · paused ${plural(paused, "day")}" else "")
            return GoalAge(days, paused, shortText(created, days), tooltip)
        }

        // The units: whole months between the creation day and the creation day plus the days going.
        private fun shortText(created: LocalDate, days: Int): String {
            if (days < 14) {
                return plural(days, "day")
            }

            val reached = created.plusDays(days.toLong())
            val months = ((reached.year - created.year) * 12) + reached.monthValue - created.monthValue -
                (if (reached.dayOfMonth < created.dayOfMonth) 1 else 0)
            if (months < 2) {
                return plural(days / 7, "week")
            }

            if (months < 12) {
                return plural(months, "month")
            }

            val years = months / 12
            val rest = months % 12
            return if (rest == 0) plural(years, "year") else "${plural(years, "year")} ${plural(rest, "month")}"
        }

        private fun plural(count: Int, unit: String): String = if (count == 1) "1 $unit" else "$count ${unit}s"
    }
}
