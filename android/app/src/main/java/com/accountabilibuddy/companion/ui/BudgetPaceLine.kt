package com.accountabilibuddy.companion.ui

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.engine.BudgetPace
import com.accountabilibuddy.companion.engine.DayBarBuilder
import com.accountabilibuddy.companion.engine.DayWorkWindows
import com.accountabilibuddy.companion.notify.HeaderSource
import java.time.Duration
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * Today's Budget pace for the PC card (ADR 47): the budget left and the day left from the plan and the devices' combined
 * focus, as the PC's own pace line says it.
 */
object BudgetPaceLine {
    /** Today's pace; null when today isn't a Budget day. */
    fun today(app: CompanionApp, now: LocalDateTime): BudgetPace? {
        val state = app.store.state.value
        val date = now.toLocalDate()
        val focused = DayWorkWindows.focusedToday(HeaderSource.focus(app, now), now, ZoneId.systemDefault())?.invoke(date) ?: Duration.ZERO
        return DayWorkWindows.pace(state.document.dayPlan, date, focused, now, state.sleep::fallbackNightBefore)
    }

    /** E.g. "Budget: 2h 10m left · 5h 00m of day left", "Budget: met for today"; "· behind" when the day can't hold the rest. */
    fun text(pace: BudgetPace): String {
        if (pace.budgetLeft.isZero) return "Budget: met for today"
        val behind = if (pace.behind) " · behind" else ""
        return "Budget: ${DayBarBuilder.duration(pace.budgetLeft)} left · ${DayBarBuilder.duration(pace.availableLeft)} of day left$behind"
    }
}
