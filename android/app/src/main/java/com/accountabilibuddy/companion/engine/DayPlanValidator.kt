package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.DayCategory
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.DayTemplate
import com.accountabilibuddy.companion.model.PlanPart
import java.time.format.DateTimeFormatter
import java.util.Locale

/**
 * What's wrong with a day plan before it's saved: overlaps, parts under 15 minutes, missing categories, and the
 * plan-wide rules. A port of the Windows Core's DayPlanValidator, so the phone catches what the PC would refuse.
 */
object DayPlanValidator {
    private const val SHORTEST_PART_MINUTES = 15
    private val clock = DateTimeFormatter.ofPattern("h:mm a", java.util.Locale.US)
    private val dayFormat = DateTimeFormatter.ofPattern("EEE d MMM", Locale.getDefault())

    /** Null when fine; otherwise what's wrong, e.g. "Work 12:00–13:00 overlaps Lunch 12:30–13:30." */
    fun validate(template: DayTemplate, categories: List<DayCategory>): String? {
        for (part in template.parts) {
            if (categories.none { it.id == part.categoryId }) {
                return "A part uses a category that no longer exists."
            }

            if (length(part) < SHORTEST_PART_MINUTES) {
                return "${describe(part, categories)} is shorter than 15 minutes."
            }
        }

        for (i in template.parts.indices) {
            for (j in i + 1 until template.parts.size) {
                if (overlap(template.parts[i], template.parts[j]) && !template.parts[i].isFlexible && !template.parts[j].isFlexible &&
                    !DayTemplateEditing.mayOverlap(template.parts[i].categoryId, template.parts[j].categoryId, categories)
                ) {
                    return "${describe(template.parts[i], categories)} overlaps ${describe(template.parts[j], categories)}."
                }
            }
        }

        return null
    }

    /** Null when the whole plan is fine; otherwise the first problem found. */
    fun validate(plan: DayPlan): String? {
        if (plan.categories.isEmpty()) return "Keep at least one category."
        if (plan.categories.any { it.name.isBlank() }) return "Every category needs a name."
        if (plan.categories.groupBy { it.name.trim().lowercase() }.any { it.value.size > 1 }) return "Two categories have the same name."
        if (plan.categories.groupBy { it.id }.any { it.value.size > 1 }) return "Two categories have the same id."
        plan.categories.firstOrNull { !isColor(it.color) }?.let { return "${it.name} has a colour that isn't #RRGGBB." }
        if (plan.oneOffDays.groupBy { it.date }.any { it.value.size > 1 }) return "Two one-off days are on the same date."

        validate(plan.workday, plan.categories)?.let { return "Workdays: $it" }
        validate(plan.otherDay, plan.categories)?.let { return "Other days: $it" }
        for (day in plan.oneOffDays) {
            validate(day.template, plan.categories)?.let { return "${day.date.format(dayFormat)}: $it" }
        }

        return null
    }

    private fun isColor(color: String): Boolean =
        color.length == 7 && color[0] == '#' && color.drop(1).all { it.isDigit() || it.lowercaseChar() in 'a'..'f' }

    private fun length(part: PlanPart): Int {
        val minutes = DayTemplateEditing.endMinutes(part) - DayTemplateEditing.minutesOf(part.start)
        return if (part.crossesMidnight) minutes + 24 * 60 else minutes
    }

    private fun overlap(a: PlanPart, b: PlanPart): Boolean =
        DayTemplateEditing.ranges(a).any { x -> DayTemplateEditing.ranges(b).any { y -> x.first < y.second && y.first < x.second } }

    private fun describe(part: PlanPart, categories: List<DayCategory>): String {
        val name = part.label ?: categories.firstOrNull { it.id == part.categoryId }?.name ?: "A part"
        return "$name ${part.start.format(clock)}–${part.end.format(clock)}"
    }
}
