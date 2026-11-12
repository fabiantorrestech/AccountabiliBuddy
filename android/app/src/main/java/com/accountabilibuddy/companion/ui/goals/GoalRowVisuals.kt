package com.accountabilibuddy.companion.ui.goals

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.width
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.clipRect
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalStripCell
import com.accountabilibuddy.shared.goals.GoalStripCellKind

/*
 * A goal row's visual by kind (change note §4, §4a), drawn small at the row's right. Hit = solid in the goal's colour; missed = a thin
 * outline, no fill; some time under the target = a faint fill; paused = hatched; today = the accent outline, filled from the bottom by
 * its progress (§3.8). Decorative: the row's accessible name says the number and the status.
 */

/** Time and milestone goals: a progress bar. */
@Composable
fun GoalProgressBar(fraction: Double, color: Color, modifier: Modifier = Modifier) {
    val track = Ab.colors.surface2
    Canvas(modifier.width(64.dp).height(6.dp)) {
        val radius = CornerRadius(size.height / 2, size.height / 2)
        drawRoundRect(track, cornerRadius = radius)
        val share = fraction.coerceIn(0.0, 1.0).toFloat()
        if (share > 0f) drawRoundRect(color, size = Size(size.width * share, size.height), cornerRadius = radius)
    }
}

/** A daily habit: the 14-day strip (the same cells as the streak, so they always agree). */
@Composable
fun GoalStripVisual(cells: List<GoalStripCell>, color: Color, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    Canvas(modifier.width(104.dp).height(16.dp)) {
        if (cells.isEmpty()) return@Canvas
        val gap = 2.dp.toPx()
        val cellWidth = (size.width - gap * (cells.size - 1)) / cells.size
        cells.forEachIndexed { index, cell ->
            val left = index * (cellWidth + gap)
            drawCell(cell, Offset(left, 0f), Size(cellWidth, size.height), color, colors.accent, colors.faint, colors.surface2)
        }
    }
}

/** A weekly-count habit: the week's days from the week start, today as a ring. */
@Composable
fun GoalWeekDots(cells: List<GoalStripCell>, color: Color, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    Canvas(modifier.width(84.dp).height(12.dp)) {
        if (cells.isEmpty()) return@Canvas
        val step = size.width / cells.size
        val radius = minOf(step, size.height) * 0.36f
        val stroke = 1.5.dp.toPx()
        cells.forEachIndexed { index, cell ->
            val center = Offset(step * index + step / 2, size.height / 2)
            when (cell.kind) {
                GoalStripCellKind.Hit -> drawCircle(color, radius, center)
                GoalStripCellKind.Partial -> drawCircle(color.copy(alpha = 0.3f), radius, center)
                GoalStripCellKind.Missed -> drawCircle(colors.faint, radius - stroke / 2, center, style = Stroke(stroke))
                GoalStripCellKind.Paused -> drawCircle(colors.faint.copy(alpha = 0.5f), radius - stroke / 2, center, style = Stroke(stroke))
                GoalStripCellKind.Today -> {
                    if (cell.progress > 0) drawCircle(color.copy(alpha = 0.35f), radius, center)
                    drawCircle(colors.accent, radius - stroke / 2, center, style = Stroke(stroke))
                }
                GoalStripCellKind.NotDue -> drawCircle(colors.surface2, radius, center)
            }
        }
    }
}

/** A daily amount (steps): the last 7 days as bars, solid where met, today outlined. */
@Composable
fun GoalDayBars(cells: List<GoalStripCell>, heights: List<Double>, color: Color, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    Canvas(modifier.width(64.dp).height(22.dp)) {
        if (cells.isEmpty()) return@Canvas
        val gap = 3.dp.toPx()
        val barWidth = (size.width - gap * (cells.size - 1)) / cells.size
        val radius = CornerRadius(1.5.dp.toPx(), 1.5.dp.toPx())
        cells.forEachIndexed { index, cell ->
            val share = heights.getOrElse(index) { 0.0 }.coerceIn(0.0, 1.0).toFloat()
            val barHeight = maxOf(size.height * share, 2.dp.toPx())
            val topLeft = Offset(index * (barWidth + gap), size.height - barHeight)
            val fill = if (cell.kind == GoalStripCellKind.Hit) color else color.copy(alpha = 0.4f)
            drawRoundRect(fill, topLeft, Size(barWidth, barHeight), radius)
            if (cell.isToday) {
                drawRoundRect(colors.accent, Offset(topLeft.x, 0f), Size(barWidth, size.height), radius, style = Stroke(1.dp.toPx()))
            }
        }
    }
}

/** A level to reach (weight): the recent values as a line, the latest as a dot (in the accent, brief §3.9). */
@Composable
fun GoalSparkline(values: List<Double?>, modifier: Modifier = Modifier) {
    val accent = Ab.colors.accent
    Canvas(modifier.width(64.dp).height(22.dp)) {
        val points = values.withIndex().mapNotNull { (index, value) -> value?.let { index to it } }
        if (points.isEmpty()) return@Canvas
        val low = points.minOf { it.second }
        val high = points.maxOf { it.second }
        val span = (high - low).takeIf { it > 0 } ?: 1.0
        val inset = 3.dp.toPx()
        val last = (values.size - 1).coerceAtLeast(1)
        fun at(index: Int, value: Double) = Offset(
            inset + (size.width - inset * 2) * index / last,
            inset + (size.height - inset * 2) * (1 - ((value - low) / span).toFloat()),
        )
        val path = Path()
        points.forEachIndexed { order, (index, value) ->
            val point = at(index, value)
            if (order == 0) path.moveTo(point.x, point.y) else path.lineTo(point.x, point.y)
        }
        drawPath(path, accent.copy(alpha = 0.8f), style = Stroke(1.5.dp.toPx()))
        val (index, value) = points.last()
        drawCircle(accent, 2.5.dp.toPx(), at(index, value))
    }
}

// One strip cell (§3.8).
private fun DrawScope.drawCell(cell: GoalStripCell, topLeft: Offset, cellSize: Size, color: Color, accent: Color, faint: Color, empty: Color) {
    val radius = CornerRadius(2.dp.toPx(), 2.dp.toPx())
    val stroke = 1.dp.toPx()
    val inner = Offset(topLeft.x + stroke / 2, topLeft.y + stroke / 2)
    val innerSize = Size(cellSize.width - stroke, cellSize.height - stroke)
    when (cell.kind) {
        GoalStripCellKind.Hit -> drawRoundRect(color, topLeft, cellSize, radius)
        GoalStripCellKind.Partial -> {
            drawRoundRect(color.copy(alpha = 0.22f), topLeft, cellSize, radius)
            drawRoundRect(color.copy(alpha = 0.6f), inner, innerSize, radius, style = Stroke(stroke))
        }
        GoalStripCellKind.Missed -> drawRoundRect(faint, inner, innerSize, radius, style = Stroke(stroke))
        GoalStripCellKind.Paused -> {
            clipRect(topLeft.x, topLeft.y, topLeft.x + cellSize.width, topLeft.y + cellSize.height) {
                val step = 3.dp.toPx()
                var x = topLeft.x - cellSize.height
                while (x < topLeft.x + cellSize.width) {
                    drawLine(faint, Offset(x, topLeft.y + cellSize.height), Offset(x + cellSize.height, topLeft.y), stroke)
                    x += step
                }
            }
        }
        GoalStripCellKind.Today -> {
            val filled = (cellSize.height * cell.progress.coerceIn(0.0, 1.0).toFloat())
            if (filled > 0f) drawRoundRect(color, Offset(topLeft.x, topLeft.y + cellSize.height - filled), Size(cellSize.width, filled), radius)
            drawRoundRect(accent, inner, innerSize, radius, style = Stroke(stroke * 1.5f))
        }
        GoalStripCellKind.NotDue -> drawRoundRect(empty.copy(alpha = 0.5f), topLeft, cellSize, radius)
    }
}
