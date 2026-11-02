package com.accountabilibuddy.companion.stats

/**
 * A view made of a big number, a bar chart, tiles and maybe a list of days (Overview, Drift, Sleep, Phone).
 *
 * @property goalLine Where the goal sits, 0 to 1 of the chart's height; null for none.
 * @property caption An honest line about how much data is behind it ("Only 9 days of history so far."); null for none.
 */
data class BarPanel(
    val big: String,
    val note: String,
    val bars: List<StatsBar>,
    val goalLine: Float?,
    val goalLabel: String?,
    val cards: List<StatCard>,
    val days: List<StatsDayRow> = emptyList(),
    val caption: String? = null,
)
