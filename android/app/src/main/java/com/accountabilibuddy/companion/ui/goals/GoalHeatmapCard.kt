package com.accountabilibuddy.companion.ui.goals

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.KeyboardArrowLeft
import androidx.compose.material.icons.automirrored.outlined.KeyboardArrowRight
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.clipRect
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextMeasurer
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.drawText
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.goals.GoalHeatmapLabels
import com.accountabilibuddy.companion.goals.PhoneGoalPage
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalHeatCellMark
import com.accountabilibuddy.shared.goals.GoalHeatmap
import com.accountabilibuddy.shared.goals.GoalHeatmapGrid
import com.accountabilibuddy.shared.goals.GoalHeatmapRange
import com.accountabilibuddy.shared.goals.GoalPeriods
import com.accountabilibuddy.shared.goals.GoalWeekStart
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.time.LocalDate
import kotlin.math.floor

/**
 * The goal page's heatmap (brief §4.3: "Last 3 months" with ‹ ›, a grid of weeks with today in an accent outline and future days
 * dashed). A tap selects a day (dark ring) for the day panel; a long press shows the PC's hover bubble ("Fri 10/30 · 0 min · missed")
 * in the line under the grid, which otherwise holds the legend (a fixed slot, so nothing moves). Hit = solid ramp, missed = hollow,
 * some time = faint fill, paused = hatched (§3.8). The grid is the shared [GoalHeatmap]; it never squashes: the oldest weeks crop.
 *
 * @param selected The picked day, or null for today.
 */
@Composable
fun GoalHeatmapCard(
    page: PhoneGoalPage,
    weekStart: GoalWeekStart,
    labels: GoalHeatmapLabels,
    tint: Color,
    selected: LocalDate?,
    onSelect: (LocalDate) -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = Ab.colors
    val today = page.facts.today
    var endDay by rememberSaveable(page.goal.id) { mutableStateOf<Long?>(null) }
    var tip by remember(page.goal.id) { mutableStateOf<String?>(null) }
    val end = endDay?.let { LocalDate.ofEpochDay(it) }?.takeIf { it < today } ?: today
    val measurer = rememberTextMeasurer()
    val density = LocalDensity.current
    val cell = 20.dp
    val gap = 4.dp
    val labelWidth = if (labels == GoalHeatmapLabels.None) 0.dp else 26.dp
    val topBand = 18.dp
    val range = GoalHeatmapRange.ThreeMonths

    BoxWithConstraints(modifier.fillMaxWidth()) {
        val fit = floor(((maxWidth - labelWidth + gap) / (cell + gap)).toDouble()).toInt().coerceAtLeast(1)
        val map by produceState<GoalHeatmap?>(null, page.goal, page.data, today, end, fit, weekStart) {
            value = withContext(Dispatchers.Default) { GoalHeatmap.build(page.goal, page.data, today, weekStart, range, end, fit) }
        }
        val shown = map
        val rampMix = GoalHeatRamp
        val rowNames = rowNames(weekStart, labels)
        val cellPx = with(density) { cell.toPx() }
        val gapPx = with(density) { gap.toPx() }
        val labelPx = with(density) { labelWidth.toPx() }
        val topPx = with(density) { topBand.toPx() }
        fun dayAt(offset: Offset): LocalDate? {
            val g = shown ?: return null
            val column = floor((offset.x - labelPx) / (cellPx + gapPx)).toInt()
            val row = floor((offset.y - topPx) / (cellPx + gapPx)).toInt()
            if (column < 0 || column >= g.grid.columns || row !in 0 until GoalHeatmapGrid.ROWS) return null
            return g.cells.firstOrNull { it.row == row && it.column == column }?.day
        }
        Column(Modifier.fillMaxWidth()) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Text(shown?.title ?: "Last 3 months", style = Ab.type.bodyMedium, fontWeight = FontWeight.SemiBold, color = colors.muted, modifier = Modifier.weight(1f))
                IconButton(onClick = { endDay = GoalHeatmapGrid.step(range, end, -1, today).toEpochDay(); tip = null }) {
                    Icon(Icons.AutoMirrored.Outlined.KeyboardArrowLeft, contentDescription = "Earlier months", tint = colors.ink)
                }
                IconButton(onClick = { endDay = GoalHeatmapGrid.step(range, end, 1, today).takeIf { it < today }?.toEpochDay(); tip = null }, enabled = shown?.canGoNext == true) {
                    Icon(Icons.AutoMirrored.Outlined.KeyboardArrowRight, contentDescription = "Later months", tint = if (shown?.canGoNext == true) colors.ink else colors.faint)
                }
            }
            Canvas(
                Modifier.fillMaxWidth().height(topBand + cell * 7 + gap * 6)
                    .semantics { contentDescription = "Heatmap of the last 3 months. Tap a day to see it." }
                    .pointerInput(shown) {
                        detectTapGestures(
                            onTap = { offset -> dayAt(offset)?.let { tip = null; onSelect(it) } },
                            onLongPress = { offset -> dayAt(offset)?.let { day -> tip = shown?.cells?.firstOrNull { it.day == day }?.tip } },
                        )
                    },
            ) {
                val g = shown ?: return@Canvas
                val label = TextStyle(fontSize = 11.sp, color = colors.muted)
                g.grid.months.forEach { month ->
                    val x = labelPx + month.centreColumn.toFloat() * (cellPx + gapPx) - gapPx / 2
                    val measured = measurer.measure(month.label, label)
                    drawText(measured, topLeft = Offset((x - measured.size.width / 2f).coerceAtLeast(labelPx), 0f))
                }
                rowNames.forEachIndexed { row, name ->
                    if (name.isNotEmpty()) {
                        val measured = measurer.measure(name, label)
                        drawText(measured, topLeft = Offset(0f, topPx + row * (cellPx + gapPx) + (cellPx - measured.size.height) / 2f))
                    }
                }
                g.cells.forEach { c ->
                    val topLeft = Offset(labelPx + c.column * (cellPx + gapPx), topPx + c.row * (cellPx + gapPx))
                    drawGoalHeatCell(c.mark, c.level, c.progress, topLeft, cellPx, tint, rampMix, colors.surface2, colors.accent, colors.faint)
                    if (selected != null && c.day == selected) {
                        drawRoundRect(colors.ink, topLeft - Offset(1.dp.toPx(), 1.dp.toPx()), Size(cellPx + 2.dp.toPx(), cellPx + 2.dp.toPx()), CornerRadius(5.dp.toPx()), style = Stroke(2.dp.toPx()))
                    }
                }
            }
            // The legend line; a long press shows the day's tip here instead (the PC's hover bubble).
            Text(tip ?: shown?.legend ?: " ", style = Ab.type.micro, color = colors.muted, modifier = Modifier.padding(top = 6.dp).height(18.dp), maxLines = 1)
        }
    }
}

/** How much of the goal's colour each of the five heat levels takes. */
internal val GoalHeatRamp = listOf(0f, 0.22f, 0.45f, 0.72f, 1f)

/** The seven row names from the week's first day ("Mo", "We", "Fr" with Some), blank where a row has none. */
internal fun goalHeatRowNames(weekStart: GoalWeekStart, labels: GoalHeatmapLabels): List<String> = rowNames(weekStart, labels)

private fun rowNames(weekStart: GoalWeekStart, labels: GoalHeatmapLabels): List<String> {
    if (labels == GoalHeatmapLabels.None) return List(7) { "" }
    val names = listOf("Su", "Mo", "Tu", "We", "Th", "Fr", "Sa")
    val first = GoalPeriods.firstDay(weekStart)
    return (0 until 7).map { row -> if (labels == GoalHeatmapLabels.All || row % 2 == 1) names[(first + row) % 7] else "" }
}

// One cell (brief §3.8): solid ramp for a hit, outline for missed, faint fill for some, today outlined and filled from the bottom,
// paused hatched, a day still to come dashed. Stats' per-goal year draws its cells with the same rule.
internal fun DrawScope.drawGoalHeatCell(
    mark: GoalHeatCellMark, level: Int, progress: Double, topLeft: Offset, side: Float, tint: Color, mix: List<Float>, empty: Color, accent: Color, faint: Color,
) {
    val radius = CornerRadius(4.dp.toPx())
    val box = Size(side, side)
    val stroke = 1.dp.toPx()
    val inner = Offset(topLeft.x + stroke / 2, topLeft.y + stroke / 2)
    val innerBox = Size(side - stroke, side - stroke)
    val ramp = lerp(empty, tint, mix[level.coerceIn(0, mix.lastIndex)])
    when (mark) {
        GoalHeatCellMark.None -> drawRoundRect(if (level == 0) empty.copy(alpha = 0.6f) else ramp, topLeft, box, radius)
        GoalHeatCellMark.Hollow -> drawRoundRect(faint, inner, innerBox, radius, style = Stroke(stroke))
        GoalHeatCellMark.Faint -> {
            drawRoundRect(ramp.copy(alpha = 0.35f), topLeft, box, radius)
            drawRoundRect(tint.copy(alpha = 0.6f), inner, innerBox, radius, style = Stroke(stroke))
        }
        GoalHeatCellMark.Paused -> clipRect(topLeft.x, topLeft.y, topLeft.x + side, topLeft.y + side) {
            var x = topLeft.x - side
            while (x < topLeft.x + side) {
                drawLine(faint, Offset(x, topLeft.y + side), Offset(x + side, topLeft.y), stroke)
                x += 4.dp.toPx()
            }
        }
        GoalHeatCellMark.Today -> {
            val filled = side * progress.coerceIn(0.0, 1.0).toFloat()
            if (filled > 0f) drawRoundRect(tint, Offset(topLeft.x, topLeft.y + side - filled), Size(side, filled), radius)
            drawRoundRect(accent, inner, innerBox, radius, style = Stroke(2.dp.toPx()))
        }
        GoalHeatCellMark.Future -> drawRoundRect(faint.copy(alpha = 0.6f), inner, innerBox, radius, style = Stroke(stroke, pathEffect = PathEffect.dashPathEffect(floatArrayOf(4f, 4f))))
    }
}
