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
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.clipRect
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.stats.StatsFormat
import com.accountabilibuddy.companion.stats.StatsPeriod
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalStatsDay
import com.accountabilibuddy.shared.goals.GoalStatsDayMark
import com.accountabilibuddy.shared.goals.GoalStatsSeries
import java.time.LocalDate

/**
 * One goal's days as bars (round 2 R6b4), drawn on a Canvas from the theme's tokens and the goal's colour, in the manner of
 * [BarChart]: a hit day is a solid bar, a day with something under its target a lighter one, a missed day a small stub, today the
 * accent, and a day that wasn't asked or is still to come nothing. **Paused days are hatched** over the chart's whole height, side by
 * side, so a paused stretch reads as one hatched band (as the goal heatmap hatches its cells): neither hit nor missed. The target is a
 * dashed line with its label above the right end. Tapping a day calls [onDayClick] with its words ("Fri 10/30 · 0 min · missed").
 */
@Composable
fun StatsGoalChart(
    series: GoalStatsSeries,
    period: StatsPeriod,
    tint: Color,
    today: LocalDate,
    modifier: Modifier = Modifier,
    chartHeight: Dp = 160.dp,
    onDayClick: (GoalStatsDay) -> Unit = {},
) {
    val colors = Ab.colors
    val days = series.days
    if (days.isEmpty()) return
    val target = series.targetFraction?.toFloat()
    val label = series.targetLabel
    val description = remember(series) {
        days.filter { it.mark != GoalStatsDayMark.None }.joinToString("; ") { it.text } + (label?.let { ". $it" } ?: "")
    }
    Column(modifier.fillMaxWidth().semantics(mergeDescendants = true) { contentDescription = description }) {
        Box(Modifier.fillMaxWidth().height(chartHeight + TARGET_LABEL_ROOM)) {
            Canvas(
                Modifier
                    .fillMaxWidth()
                    .height(chartHeight)
                    .align(Alignment.BottomStart)
                    .pointerInput(days) {
                        detectTapGestures { at -> onDayClick(days[(at.x / (size.width / days.size)).toInt().coerceIn(0, days.lastIndex)]) }
                    },
            ) {
                val cell = size.width / days.size
                val barWidth = (cell * 0.62f).coerceIn(minOf(3.dp.toPx(), cell), 44.dp.toPx())
                val radius = minOf(6.dp.toPx(), barWidth / 2)
                val stub = 2.dp.toPx()
                days.forEachIndexed { index, day ->
                    val left = cell * index
                    when (day.mark) {
                        GoalStatsDayMark.None -> Unit
                        GoalStatsDayMark.Paused -> hatch(left, cell, colors.faint)
                        GoalStatsDayMark.Missed -> bar(colors.plan, left + (cell - barWidth) / 2, barWidth, stub, radius)
                        else -> {
                            val color = when (day.mark) {
                                GoalStatsDayMark.Hit -> tint
                                GoalStatsDayMark.Today -> colors.accent
                                else -> tint.copy(alpha = 0.42f)
                            }
                            bar(color, left + (cell - barWidth) / 2, barWidth, maxOf(stub, size.height * day.fraction.toFloat().coerceIn(0f, 1f)), radius)
                        }
                    }
                }

                if (target != null) {
                    val y = size.height * (1f - target.coerceIn(0f, 1f))
                    drawLine(colors.faint, Offset(0f, y), Offset(size.width, y), 1.dp.toPx(), pathEffect = PathEffect.dashPathEffect(floatArrayOf(5.dp.toPx(), 4.dp.toPx())))
                }
            }

            if (target != null && label != null) {
                Text(
                    label, style = Ab.type.micro, color = colors.muted,
                    modifier = Modifier.align(Alignment.TopEnd).offset(y = chartHeight * (1f - target.coerceIn(0f, 1f))),
                )
            }
        }

        // The labels under the bars: one cell per day, today's bold in ink (as the other Stats charts).
        Row(Modifier.fillMaxWidth().padding(top = 6.dp).clearAndSetSemantics { }) {
            days.forEach { day ->
                val isToday = day.day == today
                Box(Modifier.weight(1f), contentAlignment = Alignment.Center) {
                    Text(
                        StatsFormat.barLabel(period, day.day), style = Ab.type.micro, color = if (isToday) colors.ink else colors.muted,
                        fontWeight = if (isToday) FontWeight.Bold else FontWeight.Normal, maxLines = 1, softWrap = false, textAlign = TextAlign.Center,
                        modifier = Modifier.wrapContentWidth(Alignment.CenterHorizontally, unbounded = true),
                    )
                }
            }
        }
    }
}

// A bar standing on the baseline: round at the top, square at the bottom.
private fun DrawScope.bar(color: Color, left: Float, width: Float, height: Float, radius: Float) {
    drawRoundRect(color, Offset(left, size.height - height), Size(width, height), CornerRadius(radius, radius))
    if (height > radius) {
        drawRect(color, Offset(left, size.height - radius), Size(width, radius))
    }
}

// A paused day: diagonal lines over the day's whole column. The lines are placed from the chart's left edge, so the hatching of
// neighbouring paused days joins into one band.
private fun DrawScope.hatch(left: Float, width: Float, color: Color) {
    val step = 6.dp.toPx()
    val height = size.height
    clipRect(left, 0f, left + width, height) {
        var x = (kotlin.math.floor((left - height) / step)) * step
        while (x < left + width) {
            drawLine(color, Offset(x, height), Offset(x + height, 0f), 1.dp.toPx())
            x += step
        }
    }
}

private val TARGET_LABEL_ROOM = 14.dp
