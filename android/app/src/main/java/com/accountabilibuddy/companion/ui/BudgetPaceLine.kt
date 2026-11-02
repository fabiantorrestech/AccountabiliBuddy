package com.accountabilibuddy.companion.ui

import com.accountabilibuddy.companion.engine.BudgetPace
import com.accountabilibuddy.companion.engine.DayBarBuilder

/**
 * The Budget pace line on the session screen (ADR 47): the budget left and the day left, as the PC's own pace line says it. The pace
 * itself comes with Today's numbers (`TodayInfo.pace`).
 */
object BudgetPaceLine {
    /** E.g. "Budget: 2h 10m left · 5h 00m of day left", "Budget: met for today"; "· behind" when the day can't hold the rest. */
    fun text(pace: BudgetPace): String {
        if (pace.budgetLeft.isZero) return "Budget: met for today"
        val behind = if (pace.behind) " · behind" else ""
        return "Budget: ${DayBarBuilder.duration(pace.budgetLeft)} left · ${DayBarBuilder.duration(pace.availableLeft)} of day left$behind"
    }
}
