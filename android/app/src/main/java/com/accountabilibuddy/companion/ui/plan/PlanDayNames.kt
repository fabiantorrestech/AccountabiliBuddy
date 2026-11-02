package com.accountabilibuddy.companion.ui.plan

import com.accountabilibuddy.companion.model.DayPlan
import java.time.LocalDate

/** What a day is called on the Plan page, as on the PC's: the day type's own name, "Own plan" for a date planned differently. */
object PlanDayNames {
    /** "Workday", "Off day", "Own plan" (a one-off day) or "No day type". */
    fun full(plan: DayPlan, date: LocalDate): String {
        if (plan.oneOffDays.any { it.date == date }) return "Own plan"
        val id = plan.cycle.templateIdFor(date) ?: return "No day type"
        return plan.templates.firstOrNull { it.id == id }?.name ?: "No day type"
    }

    /** The week strip's short name: "Work", "Light", "Off"; a trailing "day" is dropped, a one-off day reads "Own". */
    fun short(plan: DayPlan, date: LocalDate): String {
        if (plan.oneOffDays.any { it.date == date }) return "Own"
        val id = plan.cycle.templateIdFor(date) ?: return "None"
        val name = plan.templates.firstOrNull { it.id == id }?.name?.trim() ?: return "None"
        for (suffix in SUFFIXES) {
            if (name.length > suffix.length && name.endsWith(suffix, ignoreCase = true)) return name.dropLast(suffix.length).trim()
        }

        return name
    }

    private val SUFFIXES = listOf(" days", " day", "days", "day")
}
