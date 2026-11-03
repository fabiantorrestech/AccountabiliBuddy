package com.accountabilibuddy.companion.calendar

import com.accountabilibuddy.companion.model.DayPlan
import java.time.Duration
import java.time.LocalDate
import java.time.LocalTime
import java.time.format.DateTimeFormatter
import java.util.Locale

/**
 * Today's lines for what calendar events changed in the guards (ADR 58), worked out from the stored plan and the effective one. They
 * name the category and the times, never an event's title: "Comms from calendar: 2:00-3:00 PM", and for a Work event counted toward a
 * Budget day "Work from calendar counted toward today's budget: 45 min". Times are 12-hour with AM/PM, whatever the phone's setting.
 * Empty when the effective plan is the stored plan itself (the feature is off, or nothing applies).
 */
object CalendarTodayLines {
    private val WITH_MERIDIEM: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a", Locale.US)
    private val WITHOUT_MERIDIEM: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm", Locale.US)

    fun of(stored: DayPlan, effective: DayPlan, date: LocalDate): List<String> {
        if (effective === stored) return emptyList()

        val template = effective.templateFor(date)
        val lines = template.parts
            .filter { it.fromEvent }
            .sortedBy { it.start }
            .mapNotNull { part -> effective.category(part.categoryId)?.let { "${it.name} from calendar: ${range(part.start, part.end)}" } }
        val credit = template.budgetCredit
        return if (credit.isZero) lines else lines + "Work from calendar counted toward today's budget: ${minutes(credit)}"
    }

    // "2:00-3:00 PM" when both ends share AM or PM, else "11:30 AM-12:30 PM".
    private fun range(start: LocalTime, end: LocalTime): String {
        val sameHalf = start.hour / 12 == end.hour / 12 && end != LocalTime.MIDNIGHT
        return if (sameHalf) "${WITHOUT_MERIDIEM.format(start)}-${WITH_MERIDIEM.format(end)}" else "${WITH_MERIDIEM.format(start)}-${WITH_MERIDIEM.format(end)}"
    }

    private fun minutes(duration: Duration): String {
        val total = duration.toMinutes()
        return if (total >= 60 && total % 60 != 0L) "${total / 60} h ${total % 60} min" else if (total >= 60) "${total / 60} h" else "$total min"
    }
}
