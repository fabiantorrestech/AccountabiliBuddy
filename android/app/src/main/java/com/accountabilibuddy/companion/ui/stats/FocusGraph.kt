package com.accountabilibuddy.companion.ui.stats

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.stats.TrendPanel
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The Focus graph (board 19g): deep work as a line in the accent with a flat 10% fill under it and a ringed last point, drift as a
 * dashed line, the goal as a dashed line with its label. A null value is a gap: the line stops, it never drops to zero. Drawn from
 * the tokens on a Canvas; no gradient, no shadow, no chart library.
 */
@Composable
fun FocusGraph(panel: TrendPanel, modifier: Modifier = Modifier, chartHeight: Dp = 180.dp) {
    val colors = Ab.colors
    val drift = Ab.meaning(colors.drift)
    val count = panel.main.size
    BoxWithConstraints(modifier.fillMaxWidth().semantics(mergeDescendants = true) { contentDescription = panel.descriptions.joinToString("; ") + ". " + panel.goalLabel }) {
        val inset = 8.dp
        val spacing = if (count > 1) (maxWidth - inset * 2) / (count - 1) else 0.dp
        Column {
            Box(Modifier.fillMaxWidth().height(chartHeight + GOAL_LABEL_ROOM)) {
                Canvas(Modifier.fillMaxWidth().height(chartHeight).align(Alignment.BottomStart)) {
                    val left = inset.toPx()
                    val step = if (count > 1) (size.width - left * 2) / (count - 1) else 0f
                    fun x(index: Int) = left + step * index
                    fun y(value: Double) = size.height * (1f - (value / panel.maximum).toFloat().coerceIn(0f, 1f))

                    val goalY = y(panel.goal)
                    drawLine(
                        colors.faint, Offset(0f, goalY), Offset(size.width, goalY), 1.dp.toPx(),
                        pathEffect = PathEffect.dashPathEffect(floatArrayOf(5.dp.toPx(), 4.dp.toPx())),
                    )

                    // Under the line: a flat 10% of the accent, one piece for each unbroken run.
                    runsOf(panel.main).forEach { run ->
                        if (run.size < 2) return@forEach
                        val fill = Path().apply {
                            moveTo(x(run.first()), size.height)
                            run.forEach { lineTo(x(it), y(panel.main[it]!!)) }
                            lineTo(x(run.last()), size.height)
                            close()
                        }
                        drawPath(fill, colors.accent.copy(alpha = 0.10f))
                    }

                    panel.drift?.let { values ->
                        runsOf(values).forEach { run -> line(run, values, ::x, ::y, drift, 2.dp.toPx(), PathEffect.dashPathEffect(floatArrayOf(6.dp.toPx(), 5.dp.toPx()))) }
                    }

                    runsOf(panel.main).forEach { run -> line(run, panel.main, ::x, ::y, colors.accent, 2.5.dp.toPx(), null) }
                    val last = panel.main.indexOfLast { it != null }
                    panel.main.forEachIndexed { index, value ->
                        if (value == null || index == last) return@forEachIndexed
                        drawCircle(colors.accent, 3.dp.toPx(), Offset(x(index), y(value)))
                    }

                    if (last >= 0) {
                        drawCircle(colors.surface, 5.5.dp.toPx(), Offset(x(last), y(panel.main[last]!!)))
                        drawCircle(colors.accent, 5.5.dp.toPx(), Offset(x(last), y(panel.main[last]!!)), style = Stroke(2.5.dp.toPx()))
                    }
                }

                Text(panel.goalLabel, style = Ab.type.micro, color = colors.muted, modifier = Modifier.align(Alignment.TopEnd).offset(y = chartHeight * (1f - (panel.goal / panel.maximum).toFloat().coerceIn(0f, 1f))))
            }

            Box(Modifier.fillMaxWidth().height(24.dp).padding(top = 6.dp).clearAndSetSemantics { }) {
                val labelWidth = (spacing * 2f).coerceIn(28.dp, 48.dp)
                panel.labels.forEachIndexed { index, label ->
                    if (label == null) return@forEachIndexed
                    Text(
                        label, style = Ab.type.micro, color = colors.muted, textAlign = TextAlign.Center, maxLines = 1, softWrap = false,
                        modifier = Modifier.width(labelWidth).offset(x = inset + spacing * index - labelWidth / 2),
                    )
                }
            }
        }
    }
}

/** The legend under the graph: a solid dash for deep work, a dashed one for drift. */
@Composable
fun FocusGraphLegend(showDrift: Boolean, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    val drift = Ab.meaning(colors.drift)
    Row(modifier.fillMaxWidth().clearAndSetSemantics { }, horizontalArrangement = Arrangement.spacedBy(16.dp), verticalAlignment = Alignment.CenterVertically) {
        LegendLine("Deep work, daily avg", colors.accent, dashed = false)
        if (showDrift) LegendLine("Drift", drift, dashed = true)
    }
}

@Composable
private fun LegendLine(text: String, color: Color, dashed: Boolean) {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        Canvas(Modifier.width(18.dp).height(4.dp)) {
            drawLine(
                color, Offset(0f, size.height / 2), Offset(size.width, size.height / 2), 2.dp.toPx(),
                pathEffect = if (dashed) PathEffect.dashPathEffect(floatArrayOf(4.dp.toPx(), 3.dp.toPx())) else null,
            )
        }
        Text(text, style = Ab.type.micro, color = Ab.colors.muted)
    }
}

// The indexes of each unbroken run of values (a gap breaks the line).
private fun runsOf(values: List<Double?>): List<List<Int>> {
    val runs = mutableListOf<MutableList<Int>>()
    values.forEachIndexed { index, value ->
        if (value == null) {
            return@forEachIndexed
        }

        if (index > 0 && values[index - 1] != null && runs.isNotEmpty()) runs.last().add(index) else runs += mutableListOf(index)
    }

    return runs
}

private fun DrawScope.line(
    run: List<Int>, values: List<Double?>, x: (Int) -> Float, y: (Double) -> Float, color: Color, width: Float, effect: PathEffect?,
) {
    if (run.size < 2) return
    val path = Path().apply {
        moveTo(x(run.first()), y(values[run.first()]!!))
        run.drop(1).forEach { lineTo(x(it), y(values[it]!!)) }
    }
    drawPath(path, color, style = Stroke(width, pathEffect = effect, cap = androidx.compose.ui.graphics.StrokeCap.Round, join = androidx.compose.ui.graphics.StrokeJoin.Round))
}

private val GOAL_LABEL_ROOM = 14.dp
