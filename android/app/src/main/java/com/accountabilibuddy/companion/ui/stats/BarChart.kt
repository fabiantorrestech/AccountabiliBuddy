package com.accountabilibuddy.companion.ui.stats

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.wrapContentWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.stats.BarKind
import com.accountabilibuddy.companion.stats.StatsBar
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.LocalDate

/**
 * A bar chart drawn on a Canvas from the theme's tokens: no chart library, no gradients, no shadows (boards 5d, 19c). Each bar has
 * an equal share of the width; a bar with [StatsBar.noBar] draws nothing (no data is never a zero), one with no value draws a small
 * stub. The goal is a dashed line with its label above the right end. Tapping a bar that has a date calls [onBarClick]. Its words
 * are the bars' descriptions, so a screen reader hears them even though the drawing has no text.
 */
@Composable
fun BarChart(
    bars: List<StatsBar>,
    goalLine: Float?,
    goalLabel: String?,
    modifier: Modifier = Modifier,
    chartHeight: Dp = 160.dp,
    onBarClick: ((LocalDate) -> Unit)? = null,
) {
    val colors = Ab.colors
    val fills = barFills()
    val density = LocalDensity.current
    val description = remember(bars, goalLabel) { bars.filter { !it.noBar }.joinToString("; ") { it.description } + (goalLabel?.let { ". $it" } ?: "") }
    Column(modifier.fillMaxWidth().semantics(mergeDescendants = true) { contentDescription = description }) {
        Box(Modifier.fillMaxWidth().height(chartHeight + GOAL_LABEL_ROOM)) {
            Canvas(
                Modifier
                    .fillMaxWidth()
                    .height(chartHeight)
                    .align(Alignment.BottomStart)
                    .pointerInput(bars, onBarClick) {
                        if (onBarClick == null) return@pointerInput
                        detectTapGestures { at ->
                            val index = (at.x / (size.width / bars.size)).toInt().coerceIn(0, bars.lastIndex)
                            bars[index].date?.let(onBarClick)
                        }
                    },
            ) {
                val cell = size.width / bars.size
                val barWidth = (cell * 0.62f).coerceIn(with(density) { 3.dp.toPx() }, with(density) { 44.dp.toPx() })
                val radius = with(density) { minOf(6.dp.toPx(), barWidth / 2) }
                val stub = with(density) { 2.dp.toPx() }
                bars.forEachIndexed { index, bar ->
                    if (bar.noBar) return@forEachIndexed
                    val height = maxOf(stub, size.height * bar.value.coerceIn(0f, 1f))
                    val left = cell * index + (cell - barWidth) / 2
                    val color = if (bar.value <= 0f) colors.plan else fills(bar.kind)
                    drawRoundRect(
                        color, Offset(left, size.height - height), Size(barWidth, height),
                        CornerRadius(radius, radius),
                    )
                    // The bottom corners stay square so the bars stand on one baseline.
                    if (height > radius) {
                        drawRect(color, Offset(left, size.height - radius), Size(barWidth, radius))
                    }
                }

                if (goalLine != null) {
                    val y = size.height * (1f - goalLine.coerceIn(0f, 1f))
                    drawLine(
                        colors.faint, Offset(0f, y), Offset(size.width, y), with(density) { 1.dp.toPx() },
                        pathEffect = PathEffect.dashPathEffect(floatArrayOf(with(density) { 5.dp.toPx() }, with(density) { 4.dp.toPx() })),
                    )
                }
            }

            if (goalLine != null && goalLabel != null) {
                val y = chartHeight * (1f - goalLine.coerceIn(0f, 1f))
                Text(
                    goalLabel, style = Ab.type.micro, color = Ab.colors.muted,
                    modifier = Modifier.align(Alignment.TopEnd).offset(y = y),
                )
            }
        }

        BarLabels(bars)
    }
}

/** The labels under a chart's bars: one cell per bar, the bar's own width, today's bold in ink. */
@Composable
private fun BarLabels(bars: List<StatsBar>) {
    Row(Modifier.fillMaxWidth().padding(top = 6.dp).clearAndSetSemantics { }) {
        bars.forEach { bar ->
            Box(Modifier.weight(1f), contentAlignment = Alignment.Center) {
                Text(
                    bar.label, style = Ab.type.micro, color = if (bar.isToday) Ab.colors.ink else Ab.colors.muted,
                    fontWeight = if (bar.isToday) FontWeight.Bold else FontWeight.Normal, maxLines = 1, softWrap = false, textAlign = TextAlign.Center,
                    modifier = Modifier.wrapContentWidth(Alignment.CenterHorizontally, unbounded = true),
                )
            }
        }
    }
}

/** The colour of a kind of bar, read from the tokens; the meanings (goal met, drift) go quiet in the Quiet colour mode. */
@Composable
internal fun barFills(): (BarKind) -> Color {
    val colors = Ab.colors
    val met = Ab.meaning(colors.met)
    val drift = Ab.meaning(colors.drift)
    return { kind ->
        when (kind) {
            BarKind.Met -> met
            BarKind.Accent -> colors.accent
            BarKind.Plan -> colors.plan
            BarKind.Drift -> drift
            BarKind.Sleep -> colors.sleep
        }
    }
}

private val GOAL_LABEL_ROOM = 14.dp
