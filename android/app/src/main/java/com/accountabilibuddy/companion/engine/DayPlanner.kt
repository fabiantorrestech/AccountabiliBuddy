package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.BlockLift
import com.accountabilibuddy.companion.model.BlockRef
import com.accountabilibuddy.companion.model.BlockRules
import com.accountabilibuddy.companion.model.CategoryBehavior
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.DayTemplate
import com.accountabilibuddy.companion.model.OneOffDay
import com.accountabilibuddy.companion.model.PlanPart
import com.accountabilibuddy.companion.model.SleepTimes
import java.time.LocalDate
import java.time.LocalTime
import java.util.UUID

/**
 * The "Plan a day" walkthrough's answers (ADR 37): the dates, an adjusted workday (from Workdays) or an off-day (from
 * Off-Days), the night before each date (null = as the template has it), the block rules (null = as the template
 * has them) and an optional note.
 */
data class DayPlanRequest(val dates: List<LocalDate>, val offDay: Boolean, val sleep: SleepTimes?, val blocks: BlockRules?, val note: String? = null)

/**
 * Turns the walkthrough's answers into one-off days: each date gets the Workdays or Off-Days template with the night
 * and block rules applied; the templates never change. A port of the PC's Core DayPlanner (kept in step by hand).
 */
object DayPlanner {
    fun templateFor(plan: DayPlan, request: DayPlanRequest): DayTemplate {
        var template = if (request.offDay) plan.otherDay else plan.workday
        request.sleep?.let { template = withNight(template, plan, it) }
        return request.blocks?.let { template.copy(blocks = it) } ?: template
    }

    /** The dates that already have a one-off day (asked before replacing). */
    fun clashes(plan: DayPlan, dates: List<LocalDate>): List<LocalDate> =
        dates.filter { date -> plan.oneOffDays.any { it.date == date } }.sorted()

    /** The plan with a one-off day for each date (replacing any there), in date order. */
    fun apply(plan: DayPlan, request: DayPlanRequest): DayPlan {
        val template = templateFor(plan, request)
        val dates = request.dates.toSet()
        val note = request.note?.trim()?.takeIf { it.isNotEmpty() } ?: if (request.offDay) "Off-day" else "Adjusted workday"
        val added = dates.map { date ->
            OneOffDay(plan.oneOffDays.firstOrNull { it.date == date }?.id ?: UUID.randomUUID(), date, template, note)
        }
        return plan.copy(oneOffDays = (plan.oneOffDays.filter { it.date !in dates } + added).sortedBy { it.date })
    }

    /** A lift from the start of the day until the re-enable time (all day when that's midnight), Allow. */
    fun defaultLift(blocks: List<BlockRef>, reenableAt: LocalTime): BlockLift = BlockLift(LocalTime.MIDNIGHT, reenableAt, BlockLift.ALLOW, blocks)

    /** One template with its night set (wake by alarm included), as the PC's DayPlanSleep.WithNight. */
    fun withNight(template: DayTemplate, plan: DayPlan, times: SleepTimes): DayTemplate {
        val category = plan.categories.firstOrNull { it.behavior == CategoryBehavior.Sleep } ?: return template
        val existing = sleepPart(template, plan)
        return if (existing == null) {
            template.copy(parts = template.parts + PlanPart(UUID.randomUUID(), category.id, times.bedtime, times.wake, wakeByAlarm = times.wakeByAlarm))
        } else {
            template.copy(parts = template.parts.map {
                if (it.id == existing.id) it.copy(start = times.bedtime, end = times.wake, wakeByAlarm = times.wakeByAlarm) else it
            })
        }
    }

    private fun sleepPart(template: DayTemplate, plan: DayPlan): PlanPart? {
        val sleeps = template.parts.filter { plan.category(it.categoryId)?.behavior == CategoryBehavior.Sleep }
        return sleeps.firstOrNull { it.crossesMidnight } ?: sleeps.filter { it.start.isBefore(LocalTime.NOON) }.minByOrNull { it.start }
    }
}
