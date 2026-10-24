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
    /** The collapsed card's line; null = [statusText]. */
    val statusCompact: String? = null,
    val summaryText: String,
    val planned: List<DayBarSpan>,
    val actual: List<DayBarSpan>,
    val events: List<DayBarSpan>,
    val night: List<DayBarSpan>,
    val missed: List<DayBarSpan>,
    val ticks: List<DayBarTick>,
    val now: Double,
    val sleepLine: SleepLine?,
    /** While the header counts down (a session's end, a part's start): when, and the status with "%s" for the time. */
    val countdownTo: java.time.Instant? = null,
    val countdownFormat: String? = null,
    /** The rows top to bottom, as the PC orders them (Calendar, Plan, Focus by default). */
    val rows: List<com.accountabilibuddy.companion.model.BarRow> = com.accountabilibuddy.companion.model.DayBarSettings.Default.visibleRows(),
    /** Focus sessions across the devices (an open one until now), and drift marks in them. */
    val focus: List<DayBarSpan> = emptyList(),
    val drift: List<DayBarSpan> = emptyList(),
    /** Work time (the plan's Watch parts, up to now) with no session on any device, on the Focus row in slate. */
    val gaps: List<DayBarSpan> = emptyList(),
    /** Under the bar: focused, "Distracted 12m", "Not in a session 1h 20m"; empty without focus data. */
    val focusLegend: List<LegendEntry> = emptyList(),
)
