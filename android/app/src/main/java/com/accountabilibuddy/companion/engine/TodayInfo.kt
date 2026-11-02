package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.DayMode
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.SleepTimes
import com.accountabilibuddy.shared.WatchSnapshot
import java.time.Duration
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * What the idle Today says, worked out with the bar's model every 30 seconds (the same focus snapshot): the date line's
 * ending ("work until 5:00 PM"), the headline's two lines ("Nothing running." and the next block), and today's focus against the goal.
 * [focusedMinutes] and [goalMinutes] feed the ring card; [goalMinutes] is null when the PC sent no goal.
 */
data class TodayInfo(
    val dateSuffix: String?,
    val headline: String,
    val next: String,
    val focusedMinutes: Int,
    val goalMinutes: Int?,
    /** Today's Budget pace (ADR 47), for the session screen; null when today is not a Budget day. */
    val pace: BudgetPace? = null,
) {
    companion object {
        const val NOTHING_RUNNING = "Nothing running."

        /**
         * The wording by kind of day (ADR 47): a Schedule day names its next work block ("Deep work at 3:00 PM."), a Budget day says
         * how much of the budget is left, a Free day says nothing is guarded, and a day with no work blocks is a day off.
         * [notWorkingToday] is the work card's "Not working today" on this phone.
         */
        fun of(
            plan: DayPlan,
            now: LocalDateTime,
            focus: WatchSnapshot?,
            clock: ClockFormat,
            nightFallback: ((LocalDate) -> SleepTimes?)?,
            notWorkingToday: Boolean,
        ): TodayInfo {
            val zone = ZoneId.systemDefault()
            val date = now.toLocalDate()
            val today = focus?.takeIf { it.date == date }
            val focusedMinutes = today?.focusedMinutes(now.atZone(zone).toInstant(), zone) ?: 0
            val goal = focus?.goalMinutes?.takeIf { it > 0 }
            // What the guards count for a Budget day (sessions added by hand never count), as the PC's pace does.
            val counted = DayWorkWindows.focusedToday(focus, now, zone)?.invoke(date) ?: Duration.ZERO
            val mode = plan.templateFor(date).mode

            fun info(suffix: String?, next: String, pace: BudgetPace? = null) = TodayInfo(suffix, NOTHING_RUNNING, next, focusedMinutes, goal, pace)

            if (notWorkingToday) return info("not working today", "Not working today.")
            if (mode == DayMode.Free) return info("free day", "Free day. Nothing is guarded.")
            if (mode == DayMode.Budget) {
                val pace = DayWorkWindows.pace(plan, date, counted, now, nightFallback)
                    ?: return info("budget day", "Budget day.")
                if (pace.budgetLeft.isZero) return info("budget met", "Budget met for today.", pace)
                val left = DayBarBuilder.duration(pace.budgetLeft)
                val tail = if (pace.behind) " Only ${DayBarBuilder.duration(pace.availableLeft)} of the day is left." else ""
                return info("budget day", "Budget day: $left of focus to go.$tail", pace)
            }

            val windows = DayWorkWindows.workWindows(plan, date, counted, now, nightFallback)
                .filter { it.window.start.toLocalDate() == date || it.window.end.toLocalDate() == date }
            if (windows.isEmpty()) return info("day off", "Day off.")
            val last = windows.maxBy { it.window.end }
            val suffix = if (last.window.end.isAfter(now)) "work until ${clock.time(last.window.end)}" else "work is done"
            val current = windows.firstOrNull { it.window.contains(now) }
            if (current != null) return info(suffix, "${current.name} until ${clock.time(current.window.end)}.")
            val next = windows.filter { it.window.start.isAfter(now) }.minByOrNull { it.window.start }
            if (next != null) return info(suffix, "${next.name} at ${clock.time(next.window.start)}.")
            return info(suffix, "Done for today.")
        }
    }
}
