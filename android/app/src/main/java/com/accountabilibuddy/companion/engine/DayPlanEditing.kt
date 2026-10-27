package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.CategoryBehavior
import com.accountabilibuddy.companion.model.DayCategory
import com.accountabilibuddy.companion.model.DayMode
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.DayTemplate
import com.accountabilibuddy.companion.model.PlanTemplate
import com.accountabilibuddy.companion.model.PlanCycle
import com.accountabilibuddy.companion.model.WeekdayTemplate
import com.accountabilibuddy.companion.model.WorkWindow
import java.time.DayOfWeek
import java.time.LocalDate
import java.time.LocalTime
import java.time.temporal.TemporalAdjusters
import java.util.UUID

/**
 * The day plan editor's changes to templates and the week cycle (schema v12 and v13, ADR 47): add, duplicate, delete, switch a
 * template's mode, pick a weekday's template, add or remove a week of the cycle, and say which week this one is.
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

    /** The main work's span (first start to last end, Comms included, other work categories left out) as a window; null when there is no work. */
    fun workSpan(template: DayTemplate, categories: List<DayCategory>): WorkWindow? {
        val work = template.parts.filter { part -> categories.firstOrNull { it.id == part.categoryId }?.behavior?.isWork == true && !part.crossesMidnight }
        if (work.isEmpty()) return null
        // The first work start to the last end of that work (its category, plus Comms): a different category, such as
        // evening Extra Work, stays out. Lunch and other rest parts stay their own parts inside the window.
        val main = (work.filter { part -> categories.firstOrNull { it.id == part.categoryId }?.behavior == CategoryBehavior.Watch }.minByOrNull { it.start } ?: work[0]).categoryId
        val span = work.filter { part -> part.categoryId == main || categories.firstOrNull { it.id == part.categoryId }?.behavior == CategoryBehavior.Comms }
        val start = span.minOf { it.start }
        val end = if (span.any { it.endsAtMidnight }) LocalTime.MIDNIGHT else span.maxOf { it.end }
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
        fun remap(week: List<WeekdayTemplate>) = week.map { if (it.templateId == id) it.copy(templateId = DayPlan.OFF_DAY_TEMPLATE_ID) else it }
        return plan.copy(
            templates = plan.templates.filter { it.id != id },
            cycle = plan.cycle.copy(weeks = plan.cycle.weeks.map(::remap)),
        )
    }

    /** Which weekdays (in any week of the cycle) follow day type [id]. */
    fun weekdaysOf(plan: DayPlan, id: UUID): List<DayOfWeek> =
        DayPlan.MONDAY_FIRST.filter { day -> plan.cycle.weeks.any { templateIdOn(it, day) == id } }

    /** The day type id [day] follows in [week]; Off day when the week doesn't list it. */
    fun templateIdOn(week: List<WeekdayTemplate>, day: DayOfWeek): UUID = week.firstOrNull { it.day == day }?.templateId ?: DayPlan.OFF_DAY_TEMPLATE_ID

    /** The plan with [day] of week [weekIndex] (0-based) of the cycle following [templateId]. */
    fun withWeekday(plan: DayPlan, weekIndex: Int, day: DayOfWeek, templateId: UUID): DayPlan {
        fun set(week: List<WeekdayTemplate>) = DayPlan.MONDAY_FIRST.map { WeekdayTemplate(it, if (it == day) templateId else templateIdOn(week, it)) }
        return plan.copy(cycle = plan.cycle.copy(weeks = plan.cycle.weeks.mapIndexed { index, week -> if (index == weekIndex) set(week) else week }))
    }

    /**
     * A week added at the end of the cycle, a copy of the last one (up to [PlanCycle.MAX_WEEKS]). The first one added
     * makes the week of [today] week 1, so every date keeps its template until the new week's turn.
     */
    fun withWeekAdded(plan: DayPlan, today: LocalDate): DayPlan {
        val weeks = plan.cycle.weeks
        if (weeks.size >= PlanCycle.MAX_WEEKS) return plan
        val last = weeks.lastOrNull().orEmpty().let { week -> DayPlan.MONDAY_FIRST.map { WeekdayTemplate(it, templateIdOn(week, it)) } }
        val startsOn = if (weeks.size == 1) mondayOf(today) else plan.cycle.startsOn
        return plan.copy(cycle = PlanCycle(weeks + listOf(last), startsOn))
    }

    /** The plan without the last week of the cycle (the cycle keeps at least one week). */
    fun withLastWeekRemoved(plan: DayPlan): DayPlan =
        if (plan.cycle.weeks.size <= 1) plan else plan.copy(cycle = plan.cycle.copy(weeks = plan.cycle.weeks.dropLast(1)))

    /** The plan with week 1 starting on [monday] (a Monday). */
    fun withStartsOn(plan: DayPlan, monday: LocalDate): DayPlan = plan.copy(cycle = plan.cycle.copy(startsOn = monday))

    /** The Monday on or before [date]. */
    fun mondayOf(date: LocalDate): LocalDate = date.with(TemporalAdjusters.previousOrSame(DayOfWeek.MONDAY))

    /**
     * The plan with [date]'s week made week [week] (1-based) of the cycle: week 1's start moves to the Monday `week - 1`
     * weeks before that week's Monday, so the weeks keep repeating around it. The PC's WeekSwitch.WithThisWeek. Unchanged
     * when the cycle has one week, [week] isn't one of its weeks, or the week already is that one.
     */
    fun withThisWeek(plan: DayPlan, week: Int, date: LocalDate): DayPlan {
        if (plan.cycle.weeks.size < 2 || week !in 1..plan.cycle.weeks.size || plan.cycle.weekNumberFor(date) == week) return plan
        return withStartsOn(plan, mondayOf(date).minusWeeks((week - 1).toLong()))
    }
}
