package com.accountabilibuddy.companion.ui.stats

import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.stats.HeatPanel
import java.time.LocalDate

/**
 * Consistency (board 19e): a square per day, weeks across and Monday to Sunday down, brighter for more deep work. A day with no
 * data draws nothing (never a missed day). Tapping a day opens it. A screen reader hears the subtitle and then each day with data.
 */
@Composable
fun DayHeatmap(panel: HeatPanel, modifier: Modifier = Modifier, onDayClick: ((LocalDate) -> Unit)? = null) {
    HeatGrid(
        panel.cells, panel.rows, panel.columns, rowLabels = null, panel.columnLabels, labelsOnTop = true, gap = 4.dp, maxCell = 34.dp,
        description = "${panel.subtitle}. ${panel.note} " + panel.cells.filter { it.level >= 0 }.joinToString("; ") { it.description }, modifier = modifier, onCellClick = onDayClick,
    )
}
