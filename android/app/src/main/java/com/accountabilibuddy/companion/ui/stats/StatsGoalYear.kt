package com.accountabilibuddy.companion.ui.stats

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.drawText
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.goals.GoalHeatmapLabels
import com.accountabilibuddy.companion.ui.goals.GoalHeatRamp
import com.accountabilibuddy.companion.ui.goals.drawGoalHeatCell
import com.accountabilibuddy.companion.ui.goals.goalHeatRowNames
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalHeatCell
import com.accountabilibuddy.shared.goals.GoalHeatmap
import com.accountabilibuddy.shared.goals.GoalHeatmapGrid
import com.accountabilibuddy.shared.goals.GoalWeekStart
import kotlin.math.floor

/**
 * One goal's year in Stats (round 2 R6b4), the PC's Stats per goal: the goal heatmap over a year, drawn cell for cell as the goal
 * page draws its three months ([drawGoalHeatCell]: a hit solid, a missed day hollow, **a paused day hatched**, today outlined). A year
 * of weeks is wider than a phone, and the cells never squash (a hatched cell has to stay readable): the grid scrolls sideways under
 * fixed day names and opens on its newest weeks. A tap on a day calls [onCellClick] with its cell (its tip is the day in words).
 */
@Composable
fun StatsGoalYear(
    map: GoalHeatmap,
    weekStart: GoalWeekStart,
    labels: GoalHeatmapLabels,
    tint: Color,
    modifier: Modifier = Modifier,
    onCellClick: (GoalHeatCell) -> Unit = {},
) {
    val colors = Ab.colors
    val measurer = rememberTextMeasurer()
    val density = LocalDensity.current
    val cell = 16.dp
    val gap = 3.dp
    val topBand = 18.dp
    val labelWidth = if (labels == GoalHeatmapLabels.None) 0.dp else 26.dp
    val rowNames = goalHeatRowNames(weekStart, labels)
    val gridHeight = topBand + cell * GoalHeatmapGrid.ROWS + gap * (GoalHeatmapGrid.ROWS - 1)
    val gridWidth = cell * map.grid.columns + gap * (map.grid.columns - 1)
    val label = TextStyle(fontSize = 11.sp, color = colors.muted)
    val cellPx = with(density) { cell.toPx() }
    val gapPx = with(density) { gap.toPx() }
    val topPx = with(density) { topBand.toPx() }
    // Opens on the newest weeks (the scroll position is held to the grid's width once it is measured).
    val scroll = rememberScrollState(Int.MAX_VALUE)
    Row(modifier.fillMaxWidth().semantics { contentDescription = "${map.title}. ${map.legend}. Paused days are hatched. Scroll sideways for earlier weeks; tap a day to read it." }) {
        if (labelWidth > 0.dp) {
            Canvas(Modifier.width(labelWidth).height(gridHeight)) {
                rowNames.forEachIndexed { row, name ->
                    if (name.isNotEmpty()) {
                        val measured = measurer.measure(name, label)
                        drawText(measured, topLeft = Offset(0f, topPx + row * (cellPx + gapPx) + (cellPx - measured.size.height) / 2f))
                    }
                }
            }
        }

        Box(Modifier.weight(1f).horizontalScroll(scroll)) {
            Canvas(
                Modifier.width(gridWidth).height(gridHeight).pointerInput(map) {
                    detectTapGestures { at ->
                        val column = floor(at.x / (cellPx + gapPx)).toInt()
                        val row = floor((at.y - topPx) / (cellPx + gapPx)).toInt()
                        map.cells.firstOrNull { it.row == row && it.column == column }?.let(onCellClick)
                    }
                },
            ) {
                map.grid.months.forEach { month ->
                    val x = month.centreColumn.toFloat() * (cellPx + gapPx) - gapPx / 2
                    val measured = measurer.measure(month.label, label)
                    drawText(measured, topLeft = Offset((x - measured.size.width / 2f).coerceIn(0f, maxOf(0f, size.width - measured.size.width)), 0f))
                }
                map.cells.forEach { c ->
                    val topLeft = Offset(c.column * (cellPx + gapPx), topPx + c.row * (cellPx + gapPx))
                    drawGoalHeatCell(c.mark, c.level, c.progress, topLeft, cellPx, tint, GoalHeatRamp, colors.surface2, colors.accent, colors.faint)
                }
            }
        }
    }
}
