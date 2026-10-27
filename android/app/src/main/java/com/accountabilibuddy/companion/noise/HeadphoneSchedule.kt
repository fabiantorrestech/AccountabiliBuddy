package com.accountabilibuddy.companion.noise

import com.accountabilibuddy.companion.engine.DayWorkWindows
import com.accountabilibuddy.companion.model.CategoryBehavior
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.model.SleepTimes
import java.time.Duration
import java.time.LocalDate
import java.time.LocalDateTime
import java.util.UUID

/** Whether "Play when headphones connect" is allowed right now under "Only at certain times". */
object HeadphoneSchedule {
    /** True with the switch off, or when any ticked option matches [now]. */
    fun allowsNow(
        settings: HeadphoneSettings,
        plan: DayPlan,
        now: LocalDateTime,
        focusedOn: ((LocalDate) -> Duration)? = null,
        nightFallback: ((LocalDate) -> SleepTimes?)? = null,
    ): Boolean {
        if (!settings.onlyAtTimes) return true
        return (settings.duringPlanWindow && inPlanWindow(settings, plan, now, focusedOn, nightFallback)) ||
            (settings.onDaysAndHours && inDaysAndHours(settings, now))
    }

    /** The categories the plan window option uses: the ticked ones, or the Work category (first Watch category) by default. */
    fun chosenCategoryIds(settings: HeadphoneSettings, plan: DayPlan): Set<UUID> =
        settings.planCategoryIds
            ?: plan.categories.firstOrNull { it.behavior == CategoryBehavior.Watch }?.let { setOf(it.id) }.orEmpty()

    private fun inPlanWindow(
        settings: HeadphoneSettings,
        plan: DayPlan,
        now: LocalDateTime,
        focusedOn: ((LocalDate) -> Duration)?,
        nightFallback: ((LocalDate) -> SleepTimes?)?,
    ): Boolean {
        val chosen = chosenCategoryIds(settings, plan)
        return DayWorkWindows.partsIn(plan, LocalWindow(now, now.plusMinutes(1)), now, focusedOn, nightFallback)
            .any { it.window.contains(now) && it.category.id in chosen }
    }

    private fun inDaysAndHours(settings: HeadphoneSettings, now: LocalDateTime): Boolean {
        if (now.dayOfWeek.value !in settings.days) return false
        val minute = now.hour * 60 + now.minute
        val start = settings.startMinute
        val end = settings.endMinute
        // An end before the start runs past midnight.
        return if (start <= end) minute in start until end else minute >= start || minute < end
    }
}
