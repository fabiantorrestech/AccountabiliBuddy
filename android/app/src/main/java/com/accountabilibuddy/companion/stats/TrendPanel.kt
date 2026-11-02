package com.accountabilibuddy.companion.stats

/**
 * The Focus graph: deep work (and drift) per day averaged over weeks, or day by day. A null value is a gap, not a zero.
 *
 * @property maximum The value (minutes) at the top of the chart.
 * @property goal The daily goal in minutes, drawn dashed.
 */
data class TrendPanel(
    val subtitle: String,
    val main: List<Double?>,
    val drift: List<Double?>?,
    val labels: List<String?>,
    val descriptions: List<String>,
    val maximum: Double,
    val goal: Double,
    val goalLabel: String,
    val cards: List<StatCard>,
    val caption: String? = null,
)
