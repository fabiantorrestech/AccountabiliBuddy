package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.CategoryBehavior
import com.accountabilibuddy.companion.model.DayCategory
import com.accountabilibuddy.companion.model.DayMode
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.DayTemplate
import com.accountabilibuddy.companion.model.PlanTemplate
import com.accountabilibuddy.companion.model.PlanWeek
import com.accountabilibuddy.companion.model.WeekdayTemplate
import com.accountabilibuddy.companion.model.WorkWindow
import java.time.DayOfWeek
import java.time.LocalDate
import java.time.LocalTime
import java.time.temporal.TemporalAdjusters
import java.util.UUID

/**
 * The day plan editor's changes to templates and the week map (schema v12, ADR 47): add, duplicate, delete, switch a
 * template's mode, pick a weekday's template, turn alternating weeks on or off, and flip this week between A and B.
 * Pure functions on the plan; the PC checks and decides the result like any other plan edit (stricter now, looser waits).
 */
object DayPlanEditing {
    private const val DEFAULT_BUDGET_MINUTES = 6 * 60
    private val DEFAULT_WINDOW = WorkWindow(LocalTime.of(9, 0), LocalTime.of(17, 0))

    /** How many of [template]'s parts are planned work (Watch and Comms). */
    fun workPartCount(template: DayTemplate, categories: List<DayCategory>): Int =
        template.parts.count { part -> categories.firstOrNull { it.id == part.categoryId }?.behavior?.isWork == true }

    /** [template] without its work parts. */
    fun withoutWorkParts(template: DayTemplate, categories: List<DayCategory>): DayTemplate =
        template.copy(parts = template.parts.filter { part -> categories.firstOrNull { it.id == part.categoryId }?.behavior?.isWork != true })

    /** The work parts' span (first start to last end) as a window; null when there are none or the span runs past midnight. */
    fun workSpan(template: DayTemplate, categories: List<DayCategory>): WorkWindow? {
        val work = template.parts.filter { part -> categories.firstOrNull { it.id == part.categoryId }?.behavior?.isWork == true && !part.crossesMidnight }
        if (work.isEmpty()) return null
        val start = work.minOf { it.start }
        val end = if (work.any { it.endsAtMidnight }) LocalTime.MIDNIGHT else work.maxOf { it.end }
        return WorkWindow(start, end)
    }

    /**
     * [template] switched to [mode]. A Budget day's parts are only non-productive windows, so its work parts go; a Budget
     * day starts with 6 hours; a Schedule day with no work parts gets a 9–5 window. Leaving Budget drops its hours and
     * leaving Schedule drops its window (the day keeps its other parts).
     */
    fun withMode(template: DayTemplate, mode: DayMode, categories: List<DayCategory>): DayTemplate = when (mode) {
        DayMode.Schedule -> {
            val hasWork = workPartCount(template, categories) > 0
            val window = template.workWindow ?: if (hasWork || categories.none { it.behavior == CategoryBehavior.Watch }) null else DEFAULT_WINDOW
            template.copy(mode = mode, budgetMinutes = null, workWindow = window)
        }
        DayMode.Budget -> withoutWorkParts(template, categories).copy(mode = mode, workWindow = null, budgetMinutes = template.budgetMinutes ?: DEFAULT_BUDGET_MINUTES)
        DayMode.Free -> template.copy(mode = mode, workWindow = null, budgetMinutes = null)
    }

    /** A Schedule template as "just a work window": its work parts become one window (their span, else 9–5). */
    fun asWorkWindow(template: DayTemplate, categories: List<DayCategory>): DayTemplate {
        val window = template.workWindow ?: workSpan(template, categories) ?: DEFAULT_WINDOW
        return withoutWorkParts(template, categories).copy(workWindow = window)
    }

    /** A Schedule template as a full timeline: the window, if any, becomes a real work part again. */
    fun asFullTimeline(template: DayTemplate, categories: List<DayCategory>): DayTemplate {
        val withPart = template.withWorkWindowPart(categories)
        return withPart.copy(workWindow = null)
    }

    /** [base] if no day type has that name, else "[base] 2", "[base] 3", … */
    fun uniqueName(plan: DayPlan, base: String): String {
        val taken = plan.templates.map { it.name.trim().lowercase() }.toSet()
        if (base.trim().lowercase() !in taken) return base
        return generateSequence(2) { it + 1 }.map { "$base $it" }.first { it.lowercase() !in taken }
    }

    /** A new Schedule day type with a 9–5 work window (when the plan has a Work category). */
    fun addTemplate(plan: DayPlan): Pair<DayPlan, UUID> {
        val id = UUID.randomUUID()
        val template = withMode(DayTemplate(), DayMode.Schedule, plan.categories)
        return plan.copy(templates = plan.templates + PlanTemplate(id, uniqueName(plan, "New day type"), template)) to id
    }

    /** A copy of day type [id] under a new name, with new part ids. Null when it doesn't exist. */
    fun duplicateTemplate(plan: DayPlan, id: UUID): Pair<DayPlan, UUID>? {
        val source = plan.templates.firstOrNull { it.id == id } ?: return null
        val copy = source.template.copy(parts = source.template.parts.map { it.copy(id = UUID.randomUUID()) })
        val newId = UUID.randomUUID()
        return plan.copy(templates = plan.templates + PlanTemplate(newId, uniqueName(plan, "${source.name} copy"), copy)) to newId
    }

    /** The plan without day type [id]; weekdays that followed it follow Off day. The Off day type can't be removed (returns the plan as it was). */
    fun removeTemplate(plan: DayPlan, id: UUID): DayPlan {
        if (id == DayPlan.OFF_DAY_TEMPLATE_ID) return plan
        fun remap(week: List<WeekdayTemplate>?) = week?.map { if (it.templateId == id) it.copy(templateId = DayPlan.OFF_DAY_TEMPLATE_ID) else it }
        return plan.copy(
            templates = plan.templates.filter { it.id != id },
            week = plan.week.copy(a = remap(plan.week.a)!!, b = remap(plan.week.b)),
        )
    }

    /** Which weekdays (in week A or B) follow day type [id]. */
    fun weekdaysOf(plan: DayPlan, id: UUID): List<DayOfWeek> =
        DayPlan.MONDAY_FIRST.filter { day -> templateIdOn(plan.week.a, day) == id || (plan.week.b?.let { templateIdOn(it, day) } == id) }

    /** The day type id [day] follows in [week]; Off day when the week doesn't list it. */
    fun templateIdOn(week: List<WeekdayTemplate>, day: DayOfWeek): UUID = week.firstOrNull { it.day == day }?.templateId ?: DayPlan.OFF_DAY_TEMPLATE_ID

    /** The plan with [day] of week B (or A) following [templateId]. */
    fun withWeekday(plan: DayPlan, weekB: Boolean, day: DayOfWeek, templateId: UUID): DayPlan {
        fun set(week: List<WeekdayTemplate>) = DayPlan.MONDAY_FIRST.map { WeekdayTemplate(it, if (it == day) templateId else templateIdOn(week, it)) }
        val week = plan.week
        return if (weekB) plan.copy(week = week.copy(b = set(week.b ?: week.a)))
        else plan.copy(week = week.copy(a = set(week.a)))
    }

    /**
     * Alternating weeks on (week B starts as a copy of A, from the next Monday after [today]) or off (week B dropped).
     * Turning it on leaves every date's template as it was until B is edited.
     */
    fun withAlternating(plan: DayPlan, on: Boolean, today: LocalDate): DayPlan = when {
        on && plan.week.b == null -> plan.copy(week = PlanWeek(plan.week.a, plan.week.a, nextMonday(today)))
        !on && plan.week.b != null -> plan.copy(week = PlanWeek(plan.week.a))
        else -> plan
    }

    /** The plan with week B starting on [monday] (a Monday). */
    fun withBStartsOn(plan: DayPlan, monday: LocalDate): DayPlan = plan.copy(week = plan.week.copy(bStartsOn = monday))

    /** The Monday on or before [date]. */
    fun mondayOf(date: LocalDate): LocalDate = date.with(TemporalAdjusters.previousOrSame(DayOfWeek.MONDAY))

    private fun nextMonday(date: LocalDate): LocalDate = date.with(TemporalAdjusters.next(DayOfWeek.MONDAY))

    /** True when alternating weeks are on and [date]'s week is week B. */
    fun isWeekB(plan: DayPlan, date: LocalDate): Boolean = plan.week.isWeekB(date)

    /**
     * The plan with [date]'s week as week B (or A): week B's start moves to that Monday, or the Monday a week before it,
     * so the weeks keep alternating around it. Unchanged when alternating weeks are off or the week already is that one.
     */
    fun withThisWeek(plan: DayPlan, weekB: Boolean, date: LocalDate): DayPlan {
        if (plan.week.b == null || isWeekB(plan, date) == weekB) return plan
        val monday = mondayOf(date)
        return withBStartsOn(plan, if (weekB) monday else monday.minusWeeks(1))
    }
}
