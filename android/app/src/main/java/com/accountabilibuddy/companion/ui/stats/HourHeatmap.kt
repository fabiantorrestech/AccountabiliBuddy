package com.accountabilibuddy.companion.ui.stats

import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.stats.HeatPanel

/** Intensity (board 19f): a square per weekday and hour, Monday to Sunday down and the hours across, brighter for more focus. A screen reader hears the hours that have focus, one by one. */
@Composable
fun HourHeatmap(panel: HeatPanel, modifier: Modifier = Modifier) {
    HeatGrid(
        panel.cells, panel.rows, panel.columns, panel.rowLabels, panel.columnLabels, labelsOnTop = false, gap = 3.dp, maxCell = 26.dp,
        description = "${panel.subtitle}. ${panel.note}${panel.helper?.let { " $it" } ?: ""} " + panel.cells.filter { it.level > 0 }.joinToString("; ") { it.description }, modifier = modifier,
    )
}
