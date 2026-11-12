package com.accountabilibuddy.companion.ui.goals

import com.accountabilibuddy.shared.goals.GoalUnit
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.SpanStyle
import androidx.compose.material3.Icon
import androidx.compose.material.icons.outlined.Timer
import androidx.compose.material.icons.Icons
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.drawText
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.goals.PhoneGoalPage
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbSegmented
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalTexts
import com.accountabilibuddy.shared.goals.GoalTrend
import com.accountabilibuddy.shared.goals.GoalTrendBanner
import com.accountabilibuddy.shared.goals.GoalTrendRange
import com.accountabilibuddy.shared.goals.GoalTrends
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlin.math.ceil
import kotlin.math.floor
import kotlin.math.max
import kotlin.math.min

/**
 * An amount goal's trend (brief §4.3 "Weight detail"): the latest value with its 30-day change, a card with 1M / 3M / 6M / All and the
 * line (one point a day: the day's latest hand edit, else its first reading), a dashed projection at the 3-month pace to the target, the
 * target dotted, and the banner under it ("2 days behind your target"). The numbers are the shared [GoalTrends].
 */
@Composable
fun GoalTrendCard(page: PhoneGoalPage, tint: Color, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    var rangeIndex by rememberSaveable(page.goal.id) { mutableIntStateOf(GoalTrendRange.All.ordinal) }
    val range = GoalTrendRange.entries[rangeIndex]
    val trend by produceState<GoalTrend?>(null, page.goal, page.data, page.facts.today, range) {
        value = withContext(Dispatchers.Default) { GoalTrends.build(page.goal, page.data, page.facts.today, range) }
    }
    val shown = trend
    val unit = page.goal.target.unit
    Column(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Row(Modifier.fillMaxWidth().heightIn(min = 56.dp).padding(horizontal = 4.dp), verticalAlignment = Alignment.Bottom) {
            // Board Goals-Phone-Weight: the value at 44 sp / 600 in the accent, its unit at 17 sp beside it.
            val value = shown?.latest?.let { GoalTexts.value(it, unit) } ?: "–"
            val split = ' ' in value && (unit == GoalUnit.Lb || unit == GoalUnit.Kg)
            Text(
                buildAnnotatedString {
                    withStyle(SpanStyle(fontSize = 44.sp, fontWeight = FontWeight.SemiBold)) { append(if (split) value.substringBeforeLast(' ') else value) }
                    if (split) withStyle(SpanStyle(fontSize = 17.sp)) { append(" " + value.substringAfterLast(' ')) }
                },
                style = Ab.type.count.copy(fontSize = 44.sp, lineHeight = 48.sp), color = colors.accent, maxLines = 1, modifier = Modifier.weight(1f),
            )
            shown?.changeText?.let {
                Text(it, style = Ab.type.small, color = if (shown.changeGood == true) colors.met else colors.muted, modifier = Modifier.padding(bottom = 8.dp))
            }
        }
        AbCard(contentPadding = androidx.compose.foundation.layout.PaddingValues(12.dp), verticalSpacing = 10.dp) {
            AbSegmented(GoalTrendRange.entries.map { it.label }, rangeIndex, { rangeIndex = it })
            TrendChart(shown, tint, page.facts.today)
            Row(horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                LegendItem("Logged", colors.accent, dashed = false)
                LegendItem("At your 3-month pace", colors.accent.copy(alpha = 0.6f), dashed = true)
                if (shown?.targetLevel != null) LegendItem("Target", colors.met, dotted = true)
            }
        }
        shown?.banner?.let { TrendBanner(it) }
    }
}

@Composable
private fun TrendChart(trend: GoalTrend?, tint: Color, today: java.time.LocalDate) {
    val colors = Ab.colors
    val measurer = rememberTextMeasurer()
    val label = TextStyle(fontSize = 11.sp, color = colors.muted)
    val lineColor = colors.accent
    Canvas(Modifier.fillMaxWidth().height(200.dp).semantics { contentDescription = "Trend chart" }) {
        val t = trend ?: return@Canvas
        val all = t.points + t.projection
        if (all.isEmpty()) {
            val m = measurer.measure("No readings yet", label)
            drawText(m, topLeft = Offset((size.width - m.size.width) / 2, (size.height - m.size.height) / 2))
            return@Canvas
        }
        val values = all.map { it.value } + listOfNotNull(t.targetLevel)
        val rawLow = values.min()
        val rawHigh = values.max()
        val pad = max((rawHigh - rawLow) * 0.1, 0.5)
        val step = niceStep((rawHigh - rawLow + 2 * pad) / 4)
        val low = floor((rawLow - pad) / step) * step
        val high = ceil((rawHigh + pad) / step) * step
        val firstDay = all.minOf { it.day.toEpochDay() }
        val lastDay = max(all.maxOf { it.day.toEpochDay() }, t.targetDate?.toEpochDay()?.takeIf { it <= all.maxOf { p -> p.day.toEpochDay() } + 400 } ?: 0L)
        val leftPad = 34.dp.toPx()
        val bottomPad = 20.dp.toPx()
        val plotW = size.width - leftPad - 6.dp.toPx()
        val plotH = size.height - bottomPad - 6.dp.toPx()
        fun x(day: Long) = leftPad + plotW * (if (lastDay == firstDay) 0.5f else (day - firstDay).toFloat() / (lastDay - firstDay))
        fun y(value: Double) = 6.dp.toPx() + plotH * (1f - ((value - low) / (high - low)).toFloat())
        var level = low
        while (level <= high + 1e-9) {
            drawLine(colors.line, Offset(leftPad, y(level)), Offset(leftPad + plotW, y(level)), 1.dp.toPx())
            val m = measurer.measure(GoalTexts.number(level, 0), label)
            drawText(m, topLeft = Offset(leftPad - m.size.width - 4.dp.toPx(), y(level) - m.size.height / 2f))
            level += step
        }
        // Month ticks along the bottom (about four).
        val spanDays = lastDay - firstDay
        val every = max(1L, spanDays / 4)
        var day = firstDay
        while (day <= lastDay) {
            val text = java.time.LocalDate.ofEpochDay(day).month.getDisplayName(java.time.format.TextStyle.SHORT, java.util.Locale.ENGLISH)
            val m = measurer.measure(text, label)
            drawText(m, topLeft = Offset((x(day) - m.size.width / 2f).coerceIn(leftPad, size.width - m.size.width), size.height - m.size.height))
            day += every
        }
        // A thin line at today, between what was logged and what is projected.
        val todayDay = today.toEpochDay()
        if (todayDay in firstDay..lastDay && lastDay > firstDay) {
            drawLine(colors.faint, Offset(x(todayDay), 6.dp.toPx()), Offset(x(todayDay), 6.dp.toPx() + plotH), 1.dp.toPx())
        }
        t.targetLevel?.let { target ->
            drawLine(colors.met, Offset(leftPad, y(target)), Offset(leftPad + plotW, y(target)), 1.5.dp.toPx(), pathEffect = PathEffect.dashPathEffect(floatArrayOf(2f, 6f)), cap = StrokeCap.Round)
        }
        if (t.projection.size == 2) {
            val a = t.projection[0]
            val b = t.projection[1]
            drawLine(lineColor.copy(alpha = 0.6f), Offset(x(a.day.toEpochDay()), y(a.value)), Offset(x(b.day.toEpochDay()), y(b.value)), 2.dp.toPx(), pathEffect = PathEffect.dashPathEffect(floatArrayOf(10f, 8f)))
            drawCircle(colors.met, 4.dp.toPx(), Offset(x(b.day.toEpochDay()), y(b.value)), style = Stroke(1.5.dp.toPx()))
            // "Target · Mar 1" at the target's point, kept inside the plot.
            t.targetDate?.let { date ->
                val month = date.month.getDisplayName(java.time.format.TextStyle.SHORT, java.util.Locale.ENGLISH)
                val m = measurer.measure("Target · $month ${date.dayOfMonth}", label.copy(color = colors.met))
                val left = (x(b.day.toEpochDay()) - m.size.width - 6.dp.toPx()).coerceAtLeast(leftPad)
                drawText(m, topLeft = Offset(left, (y(b.value) - m.size.height - 4.dp.toPx()).coerceAtLeast(0f)))
            }
        }
        t.points.zipWithNext().forEach { (p, q) ->
            drawLine(lineColor, Offset(x(p.day.toEpochDay()), y(p.value)), Offset(x(q.day.toEpochDay()), y(q.value)), 2.5.dp.toPx(), cap = StrokeCap.Round)
        }
        t.points.forEach { p -> drawCircle(lineColor, if (t.points.size <= 40) 3.dp.toPx() else 1.5.dp.toPx(), Offset(x(p.day.toEpochDay()), y(p.value))) }
    }
}

private fun niceStep(raw: Double): Double {
    if (raw <= 0) return 1.0
    val pow = Math.pow(10.0, floor(Math.log10(raw)))
    val f = raw / pow
    val nice = when {
        f <= 1 -> 1.0
        f <= 2 -> 2.0
        f <= 5 -> 5.0
        else -> 10.0
    }
    return min(nice * pow, 1e9)
}

@Composable
private fun LegendItem(text: String, color: Color, dashed: Boolean = false, dotted: Boolean = false) {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        Canvas(Modifier.width(18.dp).height(8.dp)) {
            val effect = when {
                dashed -> PathEffect.dashPathEffect(floatArrayOf(6f, 5f))
                dotted -> PathEffect.dashPathEffect(floatArrayOf(2f, 5f))
                else -> null
            }
            drawLine(color, Offset(0f, size.height / 2), Offset(size.width, size.height / 2), 2.dp.toPx(), pathEffect = effect, cap = StrokeCap.Round)
        }
        Text(text, style = Ab.type.micro, color = Ab.colors.muted, maxLines = 1)
    }
}

@Composable
private fun TrendBanner(banner: GoalTrendBanner) {
    val colors = Ab.colors
    val (fill, edge, ink) = when (banner.tone) {
        GoalTrendBanner.Tone.Amber -> Triple(colors.amberFill, colors.amberLine, colors.amberInk)
        GoalTrendBanner.Tone.Teal -> Triple(colors.accentSoft, colors.line, colors.ink)
        GoalTrendBanner.Tone.Neutral -> Triple(colors.infoBg, colors.line, colors.infoInk)
    }
    Column(
        Modifier.fillMaxWidth().clip(Ab.shapes.mediumShape).background(fill).border(1.dp, edge, Ab.shapes.mediumShape).padding(14.dp),
        verticalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        banner.headline?.let { headline ->
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Icon(Icons.Outlined.Timer, contentDescription = null, tint = ink, modifier = Modifier.size(16.dp))
                Text(headline, style = Ab.type.bodyMedium, fontWeight = FontWeight.Bold, color = ink)
            }
        }
        // The date the pace reaches the target is the line's point: bold, as the board writes it ("around **Mar 3**").
        val date = Regex("around ([^,.]+)").find(banner.body)?.groups?.get(1)
        Text(
            buildAnnotatedString {
                append(banner.body)
                if (date != null) addStyle(SpanStyle(fontWeight = FontWeight.Bold), date.range.first, date.range.last + 1)
            },
            style = Ab.type.small, color = colors.ink,
        )
    }
}
