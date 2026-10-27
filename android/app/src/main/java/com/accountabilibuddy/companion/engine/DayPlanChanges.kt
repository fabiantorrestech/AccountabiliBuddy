package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.DayTemplate
import com.accountabilibuddy.companion.model.PlanPart
import com.accountabilibuddy.companion.model.PlanTemplate
import com.accountabilibuddy.companion.model.WeekdayTemplate
import com.accountabilibuddy.companion.model.WorkWindow
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
        dayTypes(base, edited, lines)
        week(base, edited, lines)
        for (after in edited.templates) {
            val before = base.templates.firstOrNull { it.id == after.id } ?: continue
            template(title(after), before.template, after.template, base, edited, lines)
        }

        oneOffDays(base, edited, lines)
        return lines.ifEmpty { listOf("No changes") }
    }

    /**
     * The lines of a change that touch the template today follows (the reasons a change can lower today's guarded work, which
     * makes it wait on a guarded PC, ADR 28, 47): a one-off day added or removed for today, today re-mapped to another template
     * (by the weekday's map, the week of the cycle or the cycle's start), or that template's own mode, budget, work window or parts
     * changed. Empty when today's template is untouched. The PC's `DayPlanChanges.DescribeToday` has the same wording.
     */
    fun describeToday(base: DayPlan, edited: DayPlan, today: java.time.LocalDate): List<String> {
        val lines = mutableListOf<String>()
        val oneOffBefore = base.oneOffDays.firstOrNull { it.date == today }
        val oneOffAfter = edited.oneOffDays.firstOrNull { it.date == today }
        if ((oneOffBefore == null) != (oneOffAfter == null)) {
            lines += if (oneOffAfter == null) "Today's one-off day is gone" else "Today now has a one-off day"
            modeChange("Today", todayTemplate(base, today), todayTemplate(edited, today), lines)
            return lines
        }

        if (oneOffBefore != null && oneOffAfter != null) {
            template("Today's one-off day", oneOffBefore.template, oneOffAfter.template, base, edited, lines)
            return lines
        }

        val wasId = base.cycle.templateIdFor(today) ?: DayPlan.OFF_DAY_TEMPLATE_ID
        val nowId = edited.cycle.templateIdFor(today) ?: DayPlan.OFF_DAY_TEMPLATE_ID
        if (wasId != nowId) {
            if (base.cycle.weekNumberFor(today) != edited.cycle.weekNumberFor(today)) {
                lines += "This is now week ${edited.cycle.weekNumberFor(today)} of ${edited.cycle.weeks.size}"
            }

            lines += "Today follows ${dayTypeName(edited, nowId)} now, not ${dayTypeName(base, wasId)}"
            modeChange("Today", base.templateById(wasId) ?: DayTemplate(), edited.templateById(nowId) ?: DayTemplate(), lines)
        } else {
            val before = base.templateById(wasId)
            val after = edited.templateById(nowId)
            if (before != null && after != null) template(dayTypeName(edited, nowId), before, after, base, edited, lines)
        }

        return lines
    }

    private fun todayTemplate(plan: DayPlan, today: java.time.LocalDate): DayTemplate =
        plan.oneOffDays.firstOrNull { it.date == today }?.template
            ?: plan.templateById(plan.cycle.templateIdFor(today) ?: DayPlan.OFF_DAY_TEMPLATE_ID)
            ?: DayTemplate()

    private fun modeChange(title: String, before: DayTemplate, after: DayTemplate, lines: MutableList<String>) {
        if (before.mode != after.mode) lines += "$title: now a ${after.mode.name} day"
    }

    // The two migrated templates keep the wording the plan always had.
    private fun title(template: PlanTemplate): String = when (template.id) {
        DayPlan.WORKDAY_TEMPLATE_ID -> "Workdays"
        DayPlan.OFF_DAY_TEMPLATE_ID -> "Off-Days"
        else -> template.name
    }

    private fun dayTypes(base: DayPlan, edited: DayPlan, lines: MutableList<String>) {
        for (template in edited.templates) {
            val before = base.templates.firstOrNull { it.id == template.id }
            when {
                before == null -> lines += "New day type: ${template.name}"
                before.name != template.name -> lines += "Day type ${before.name} renamed to ${template.name}"
            }
        }

        base.templates.filter { old -> edited.templates.none { it.id == old.id } }.forEach { lines += "Removed day type: ${it.name}" }
    }

    private fun week(base: DayPlan, edited: DayPlan, lines: MutableList<String>) {
        val was = base.cycle.weeks.size
        val now = edited.cycle.weeks.size
        val repeating = was > 1 || now > 1
        if (was != now) {
            lines += when {
                now == 1 -> "The same week every week now"
                was == 1 -> "Repeating weeks turned on: $now weeks, week 1 starts ${dateText(edited.cycle.startsOn)}"
                else -> "The cycle is now $now weeks, not $was"
            }
        } else if (repeating && base.cycle.startsOn != edited.cycle.startsOn) {
            lines += "Week 1 now starts ${dateText(edited.cycle.startsOn)}"
        }

        // Weeks the cycle kept (a week added or removed is the line above); a week that moved to another position shows as changed days.
        for (index in 0 until minOf(was, now)) {
            weekdays(index + 1, base, edited, base.cycle.weeks[index], edited.cycle.weeks[index], repeating, lines)
        }
    }

    private fun weekdays(
        week: Int,
        base: DayPlan,
        edited: DayPlan,
        before: List<WeekdayTemplate>,
        after: List<WeekdayTemplate>,
        repeating: Boolean,
        lines: MutableList<String>,
    ) {
        for (day in DayPlan.MONDAY_FIRST) {
            val was = before.firstOrNull { it.day == day }?.templateId
            val now = after.firstOrNull { it.day == day }?.templateId
            if (was == now) continue
            val wasId = was ?: DayPlan.OFF_DAY_TEMPLATE_ID
            val nowId = now ?: DayPlan.OFF_DAY_TEMPLATE_ID
            when {
                week == 1 && nowId == DayPlan.WORKDAY_TEMPLATE_ID && wasId != nowId -> lines += "${dayName(day)} is now a workday"
                week == 1 && wasId == DayPlan.WORKDAY_TEMPLATE_ID && nowId == DayPlan.OFF_DAY_TEMPLATE_ID -> lines += "${dayName(day)} is no longer a workday"
                wasId != nowId -> {
                    val label = if (repeating) "${dayName(day)} (week $week)" else dayName(day)
                    lines += "$label: ${dayTypeName(base, wasId)} → ${dayTypeName(edited, nowId)}"
                }
            }
        }
    }

    private fun dayTypeName(plan: DayPlan, id: java.util.UUID): String = plan.templates.firstOrNull { it.id == id }?.name ?: "another day type"

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

    private fun template(title: String, before: DayTemplate, after: DayTemplate, base: DayPlan, edited: DayPlan, lines: MutableList<String>) {
        if (before.mode != after.mode) lines += "$title: now a ${after.mode.name} day"
        if (before.workWindow != after.workWindow) {
            val old = before.workWindow
            val new = after.workWindow
            lines += when {
                old == null && new != null -> "$title: added a work window ${times(new)}"
                new == null -> "$title: removed the work window"
                else -> "$title: work window ${times(old!!)} → ${times(new)}"
            }
        }

        if (before.budgetMinutes != after.budgetMinutes) lines += "$title: budget ${hours(before.budgetMinutes)} → ${hours(after.budgetMinutes)}"

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

            // A fixed lunch period (schema v13): the part starts by itself.
            if (old.autoStart != part.autoStart) {
                lines += if (part.autoStart) "$title: ${name(part, edited)} now starts by itself" else "$title: ${name(part, edited)} no longer starts by itself"
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

    private fun times(window: WorkWindow): String = "${window.start.format(clock)}–${window.end.format(clock)}"

    /** Minutes as "6h", "6h 30m" or "45m"; "none" when not set. */
    private fun hours(minutes: Int?): String = when {
        minutes == null -> "none"
        minutes % 60 == 0 -> "${minutes / 60}h"
        minutes < 60 -> "${minutes}m"
        else -> "${minutes / 60}h ${minutes % 60}m"
    }

    private fun dateText(date: java.time.LocalDate?): String = date?.format(dayFormat) ?: "an unset date"

    private fun dayName(day: java.time.DayOfWeek): String = day.getDisplayName(TextStyle.FULL, Locale.US)
}
