package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.DayTemplate
import com.accountabilibuddy.companion.model.PlanPart
import java.time.format.DateTimeFormatter
import java.time.format.TextStyle
import java.util.Locale

/**
 * What changed between two day plans, as short bullet lines for the conflict prompt (ADR 19), e.g.
 * "Workdays: Work 9:00 AM–12:00 PM → 8:30 AM–12:00 PM" or "Friday is no longer a workday".
 */
object DayPlanChanges {
    private val clock = DateTimeFormatter.ofPattern("h:mm a", Locale.US)
    private val dayFormat = DateTimeFormatter.ofPattern("EEE M/d", Locale.US)

    fun describe(base: DayPlan, edited: DayPlan): List<String> {
        val lines = mutableListOf<String>()
        categories(base, edited, lines)
        workdays(base, edited, lines)
        template("Workdays", base.workday, edited.workday, base, edited, lines)
        template("Other days", base.otherDay, edited.otherDay, base, edited, lines)
        oneOffDays(base, edited, lines)
        return lines.ifEmpty { listOf("No changes") }
    }

    private fun categories(base: DayPlan, edited: DayPlan, lines: MutableList<String>) {
        for (category in edited.categories) {
            val before = base.category(category.id)
            if (before == null) {
                lines += "New category: ${category.name}"
                continue
            }

            // Each change on its own line: a rename and a recolour are two changes.
            if (before.name != category.name) lines += "Category ${before.name} renamed to ${category.name}"
            if (!before.color.equals(category.color, ignoreCase = true)) lines += "${category.name}: new colour"
            if (before.behavior != category.behavior) lines += "${category.name} now acts as ${category.behavior}"
        }

        base.categories.filter { edited.category(it.id) == null }.forEach { lines += "Removed category: ${it.name}" }
    }

    private fun workdays(base: DayPlan, edited: DayPlan, lines: MutableList<String>) {
        (edited.workdays - base.workdays).sorted().forEach { lines += "${dayName(it)} is now a workday" }
        (base.workdays - edited.workdays).sorted().forEach { lines += "${dayName(it)} is no longer a workday" }
    }

    private fun template(title: String, before: DayTemplate, after: DayTemplate, base: DayPlan, edited: DayPlan, lines: MutableList<String>) {
        for (part in after.parts) {
            val old = before.parts.firstOrNull { it.id == part.id }
            if (old == null) {
                lines += "$title: added ${name(part, edited)} ${times(part)}"
                continue
            }

            if (old.start != part.start || old.end != part.end) lines += "$title: ${name(part, edited)} ${times(old)} → ${times(part)}"
            if (old.label != part.label) lines += "$title: ${name(old, base)} renamed to ${name(part, edited)}"
            // A renamed category shows once under categories, not again for every part using it.
            if (old.categoryId != part.categoryId) {
                lines += "$title: ${name(part, edited)} is now ${edited.category(part.categoryId)?.name ?: "another category"}"
            }
        }

        before.parts.filter { old -> after.parts.none { it.id == old.id } }.forEach { lines += "$title: removed ${name(it, base)} ${times(it)}" }
    }

    private fun oneOffDays(base: DayPlan, edited: DayPlan, lines: MutableList<String>) {
        for (day in edited.oneOffDays) {
            val old = base.oneOffDays.firstOrNull { it.date == day.date }
            when {
                old == null -> lines += "Added a one-off day: ${day.date.format(dayFormat)}" + (day.note?.let { " ($it)" } ?: "")
                old != day.copy(id = old.id) -> lines += "Changed the one-off day ${day.date.format(dayFormat)}"
            }
        }

        base.oneOffDays.filter { old -> edited.oneOffDays.none { it.date == old.date } }
            .forEach { lines += "Removed the one-off day ${it.date.format(dayFormat)}" }
    }

    private fun name(part: PlanPart, plan: DayPlan): String = part.label ?: plan.category(part.categoryId)?.name ?: "A part"

    private fun times(part: PlanPart): String = "${part.start.format(clock)}–${part.end.format(clock)}"

    private fun dayName(day: java.time.DayOfWeek): String = day.getDisplayName(TextStyle.FULL, Locale.US)
}
