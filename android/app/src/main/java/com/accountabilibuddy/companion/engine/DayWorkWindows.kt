package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.CategoryBehavior
import com.accountabilibuddy.companion.model.DayMode
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.DayTemplate
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.model.PlanPart
import com.accountabilibuddy.companion.model.PlannedFocus
import com.accountabilibuddy.companion.model.PlannedPart
import com.accountabilibuddy.shared.WatchSnapshot
import java.time.Duration
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.ZoneId
import java.util.UUID

/** How a Budget day is going (the PC's BudgetPace): the budget left, the available time left, and whether the day can't hold it any more. */
data class BudgetPace(val budgetLeft: Duration, val availableLeft: Duration, val behind: Boolean)

/**
 * The one place that says when a day is guarded (ADR 47), a port of the Windows Core's DayWorkWindows (same behaviour,
 * kept in step by hand). Every reader of a day's work reads it from here and never the plan's parts directly:
 * - Schedule: the work parts (Watch and Comms), or the work window standing in for them.
 * - Budget: waking time (the night's wake time to the next night's bedtime, from the sleep plan) minus the non-productive
 *   windows the template lists, starting after its Pre-Work part, until the day's counted focus reaches the budget; from
 *   that moment no window is left.
 * - Free: none, and no Comms or Pre-Work parts either.
 * "Counted focus" is passed in as `focusedOn` (what the day bar and the goal count); without it nothing is counted and a
 * Budget day shows all its available time. Local wall-clock times.
 */
object DayWorkWindows {
    /** Id of the stand-in part a Budget window is returned as, the PC's same one. */
    val BUDGET_WINDOW_PART_ID: UUID = UUID.fromString("5c7a2e10-0f01-4b3d-8f6e-1a9c40000f01")

    private val EARLIEST: LocalDateTime = LocalDateTime.of(1, 1, 1, 0, 0)
    private val LATEST: LocalDateTime = LocalDateTime.of(9999, 1, 1, 0, 0)

    /**
     * Every part the guards and the day bar see touching [range]: the plan's parts (a Free day without its work and
     * Pre-Work parts, a Budget day with only its non-productive windows and first Pre-Work) plus each Budget day's guarded
     * windows as parts of the first Watch category, clipped to the range, in order.
     */
    fun partsIn(plan: DayPlan, range: LocalWindow, now: LocalDateTime, focusedOn: ((LocalDate) -> Duration)? = null): List<PlannedPart> {
        val view = viewOf(plan)
        val parts = view.partsIn(range).toMutableList()
        val hasBudgetDay = plan.templates.any { it.template.mode == DayMode.Budget } || plan.oneOffDays.any { it.template.mode == DayMode.Budget }
        val work = plan.categories.firstOrNull { it.behavior == CategoryBehavior.Watch }
        if (hasBudgetDay && work != null) {
            var date = range.start.toLocalDate().minusDays(1)
            val last = range.end.toLocalDate().plusDays(1)
            while (!date.isAfter(last)) {
                for (window in budgetWindows(plan, view, date, focusedOn, now)) {
                    val shown = DayTimeline.clip(window, range) ?: continue
                    val part = PlanPart(BUDGET_WINDOW_PART_ID, work.id, window.start.toLocalTime(), window.end.toLocalTime())
                    parts.add(PlannedPart(part, work, shown))
                }

                date = date.plusDays(1)
            }
        }

        return parts.sortedWith(compareBy<PlannedPart>({ it.window.start }, { it.window.end }, { it.part.id }))
    }

    /**
     * Counted focus for the plan's Budget days from the devices' combined focus today (sessions that count; only today is
     * known here, other dates give zero); null without a focus snapshot, which counts nothing.
     */
    fun focusedToday(focus: WatchSnapshot?, now: LocalDateTime, zone: ZoneId): ((LocalDate) -> Duration)? {
        if (focus == null) return null
        val minutes = focus.focusedMinutes(now.atZone(zone).toInstant(), zone)
        return { date -> if (date == now.toLocalDate()) Duration.ofMinutes(minutes.toLong()) else Duration.ZERO }
    }

    /** The part at [time] as the guards see it, or null when nothing is planned then. */
    fun partAt(plan: DayPlan, time: LocalDateTime, focusedOn: ((LocalDate) -> Duration)? = null): PlannedPart? =
        partsIn(plan, LocalWindow(time, time.plusMinutes(1)), time, focusedOn).firstOrNull { it.window.contains(time) }

    /**
     * The guarded work windows (work and Comms parts, a Budget day's windows) touching [range], cut to it, earliest first.
     * What the former `DayTimeline.planned` gave, now by mode.
     */
    fun workWindows(plan: DayPlan, range: LocalWindow, now: LocalDateTime, focusedOn: ((LocalDate) -> Duration)? = null): List<PlannedFocus> =
        partsIn(plan, range, now, focusedOn)
            .filter { it.category.behavior.isWork }
            .map { PlannedFocus(it.name, it.category.argb, it.window) }

    /** The guarded windows of one date (midnight to midnight), given that day's counted focus so far. */
    fun workWindows(plan: DayPlan, date: LocalDate, focusedSoFar: Duration, now: LocalDateTime): List<PlannedFocus> {
        val start = date.atStartOfDay()
        return workWindows(plan, LocalWindow(start, start.plusDays(1)), now) { day -> if (day == date) focusedSoFar else Duration.ZERO }
    }

    /**
     * How a Budget day is going: the budget left after [focusedSoFar] and the available time left from [now], with "behind"
     * when the available time can no longer hold the budget. Null when the date isn't a Budget day.
     */
    fun pace(plan: DayPlan, date: LocalDate, focusedSoFar: Duration, now: LocalDateTime): BudgetPace? {
        val template = plan.templateFor(date)
        val minutes = template.budgetMinutes
        if (template.mode != DayMode.Budget || minutes == null) return null

        val left = Duration.ofMinutes(minutes.toLong()).minus(focusedSoFar).let { if (it.isNegative) Duration.ZERO else it }
        val ahead = LocalWindow(now, LATEST)
        val available = budgetWindows(plan, viewOf(plan), date, null, now)
            .mapNotNull { DayTimeline.clip(it, ahead) }
            .fold(Duration.ZERO) { sum, window -> sum.plus(Duration.between(window.start, window.end)) }
        return BudgetPace(left, available, !left.isZero && available < left)
    }

    // A Budget day's guarded windows: from the night's wake time (after the day's Pre-Work part) to the next night's
    // bedtime, minus every other part the guards see in that stretch. Once the counted focus meets the budget, only what
    // lies before now is left.
    private fun budgetWindows(plan: DayPlan, view: DayPlan, date: LocalDate, focusedOn: ((LocalDate) -> Duration)?, now: LocalDateTime): List<LocalWindow> {
        val template = plan.templateFor(date)
        val budget = template.budgetMinutes
        if (template.mode != DayMode.Budget || budget == null || budget <= 0) return emptyList()

        val midnight = date.atStartOfDay()
        var start = plan.nightBefore(date)?.let { midnight.with(it.wake) } ?: midnight
        val end = bedtimeOf(plan, date)
        val warmUp = view.templateFor(date).parts.firstOrNull {
            plan.category(it.categoryId)?.behavior == CategoryBehavior.PreWork && !it.crossesMidnight
        }
        if (warmUp != null) {
            val warmUpEnd = if (warmUp.endsAtMidnight) midnight.plusDays(1) else midnight.with(warmUp.end)
            if (warmUpEnd.isAfter(start)) start = warmUpEnd
        }

        if (!end.isAfter(start)) return emptyList()

        val stretch = LocalWindow(start, end)
        var windows = DayTimeline.subtract(listOf(stretch), view.partsIn(stretch).map { it.window })
        if (focusedOn != null && focusedOn(date) >= Duration.ofMinutes(budget.toLong())) {
            windows = windows.mapNotNull { DayTimeline.clip(it, LocalWindow(EARLIEST, now)) }
        }

        return windows
    }

    // When the waking day ends: the next night's bedtime (the evening of the date, or past midnight for an early-hours
    // bedtime); the end of the date when its next night isn't planned.
    private fun bedtimeOf(plan: DayPlan, date: LocalDate): LocalDateTime {
        val nextMidnight = date.plusDays(1).atStartOfDay()
        val next = plan.nightBefore(date.plusDays(1)) ?: return nextMidnight
        val evening = !next.wake.isAfter(next.bedtime)
        return (if (evening) nextMidnight.minusDays(1) else nextMidnight).with(next.bedtime)
    }

    // The plan as the guards see it (Free and Budget templates without their work parts).
    private fun viewOf(plan: DayPlan): DayPlan {
        if (plan.templates.none { it.template.mode != DayMode.Schedule } && plan.oneOffDays.none { it.template.mode != DayMode.Schedule }) return plan
        return plan.copy(
            templates = plan.templates.map { it.copy(template = guarded(plan, it.template)) },
            oneOffDays = plan.oneOffDays.map { it.copy(template = guarded(plan, it.template)) },
        )
    }

    // A Free day loses its work, Comms and Pre-Work parts; a Budget day its work and Comms parts and every Pre-Work part
    // but the first (the warm-up before the day's first window).
    private fun guarded(plan: DayPlan, template: DayTemplate): DayTemplate {
        if (template.mode == DayMode.Schedule) return template

        fun behaviorOf(part: PlanPart) = plan.category(part.categoryId)?.behavior
        val parts = template.parts.filter { part -> behaviorOf(part)?.let { !it.isWork && it != CategoryBehavior.PreWork } == true }.toMutableList()
        if (template.mode == DayMode.Budget) {
            template.parts
                .filter { behaviorOf(it) == CategoryBehavior.PreWork && !it.crossesMidnight }
                .minByOrNull { it.start }
                ?.let { parts.add(it) }
        }

        return template.copy(parts = parts)
    }
}
