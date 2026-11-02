package com.accountabilibuddy.companion.stats

import java.time.LocalDate

/**
 * One bar of a bar chart.
 *
 * @property label Under the bar ("M", "6a"); empty for none.
 * @property value 0 to 1 of the chart's height.
 * @property noBar Nothing is drawn: the day hasn't come, or there is no data for it (never a zero).
 * @property description What it says for a screen reader and when tapped ("Mon, Oct 26 · 2h 15m · goal met").
 * @property date The day it stands for, when tapping it should open that day.
 */
data class StatsBar(
    val label: String,
    val value: Float,
    val kind: BarKind,
    val isToday: Boolean,
    val noBar: Boolean,
    val description: String,
    val date: LocalDate? = null,
)
