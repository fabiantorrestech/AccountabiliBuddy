package com.accountabilibuddy.companion.ui.stats

import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.goals.GoalDisplayPrefs
import com.accountabilibuddy.companion.ui.goals.lineColor
import com.accountabilibuddy.shared.goals.GoalMonthLineRules
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
    // HM1: the year grid (the one with month names over it) gets the same month lines as the goal heatmaps, from the one setting.
    val monthLines by GoalDisplayPrefs.display.collectAsStateWithLifecycle()
    val reference = panel.cells.firstOrNull { it.date != null }
    val starts = remember(panel) {
        if (panel.columnLabels == null || reference == null) emptyList()
        else GoalMonthLineRules.monthStarts(reference.column, reference.row, reference.date!!, panel.columns)
    }
    HeatGrid(
        panel.cells, panel.rows, panel.columns, rowLabels = null, panel.columnLabels, labelsOnTop = true, gap = 4.dp, maxCell = 34.dp,
        description = "${panel.subtitle}. ${panel.note} " + panel.cells.filter { it.level >= 0 }.joinToString("; ") { it.description }, modifier = modifier, onCellClick = onDayClick,
        monthStarts = starts, monthLineColor = monthLines.monthLines.lineColor(),
    )
}
