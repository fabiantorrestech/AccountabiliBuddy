package com.accountabilibuddy.companion.engine

/**
 * Everything the day bar draws and says (the same lanes as on Windows): calendar events on top, planned focus,
 * an actual lane (here: sleep measured by Health Connect), the night shaded and sleep lost to staying up, hour
 * ticks and the red now line.
 */
data class DayBarModel(
    /** The day the bar is about (e.g. wake time to bedtime), which the lists under it follow. */
    val day: com.accountabilibuddy.companion.model.LocalWindow,
    val dateText: String,
    val statusText: String,
    val summaryText: String,
    val planned: List<DayBarSpan>,
    val actual: List<DayBarSpan>,
    val events: List<DayBarSpan>,
    val night: List<DayBarSpan>,
    val missed: List<DayBarSpan>,
    val ticks: List<DayBarTick>,
    val now: Double,
    val sleepLine: SleepLine?,
)
