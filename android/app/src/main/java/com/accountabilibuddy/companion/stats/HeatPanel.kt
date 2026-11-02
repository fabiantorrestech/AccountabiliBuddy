package com.accountabilibuddy.companion.stats

/**
 * A heatmap view (Consistency, Intensity): the cells, labels for the rows and columns, tiles and a line of advice.
 *
 * @property subtitle Under the title ("Goal met · last 13 weeks").
 * @property note The line about what brighter means.
 * @property helper A suggestion from the data (Intensity's best hours); null for none.
 * @property caption An honest line about how much data is behind it; null for none.
 */
data class HeatPanel(
    val title: String,
    val subtitle: String,
    val cells: List<HeatCell>,
    val rows: Int,
    val columns: Int,
    val rowLabels: List<String>?,
    val columnLabels: List<String?>?,
    val cards: List<StatCard>,
    val note: String,
    val helper: String? = null,
    val caption: String? = null,
)
