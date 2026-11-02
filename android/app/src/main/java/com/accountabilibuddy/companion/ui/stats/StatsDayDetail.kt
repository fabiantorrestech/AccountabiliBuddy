package com.accountabilibuddy.companion.ui.stats

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.ChevronLeft
import androidx.compose.material.icons.outlined.ChevronRight
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.stats.DayListRow
import com.accountabilibuddy.companion.stats.DayPanel
import com.accountabilibuddy.companion.stats.DayStripKind
import com.accountabilibuddy.companion.stats.StatCard
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbQuietIconButton
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * One day in detail (the PC's right column, board 5k): the title with ‹ ›, the 12 AM to 12 AM strip with focus, drift and planned
 * sleep, deep work against the goal and drift as two tiles, and the day's sessions (and stretches off track, times only) as a list.
 * [onPrevious] and [onNext] are null at the ends of the days the phone holds.
 */
@Composable
fun StatsDayDetail(panel: DayPanel, onPrevious: (() -> Unit)?, onNext: (() -> Unit)?, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    AbCard(modifier) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(panel.title, style = Ab.type.section, color = colors.ink, modifier = Modifier.weight(1f))
            AbQuietIconButton(Icons.Outlined.ChevronLeft, "Previous day", onClick = { onPrevious?.invoke() }, enabled = onPrevious != null)
            AbQuietIconButton(Icons.Outlined.ChevronRight, "Next day", onClick = { onNext?.invoke() }, enabled = onNext != null)
        }

        if (!panel.hasData) {
            Text("Nothing was counted on this day: it is before the first session this phone has received.", style = Ab.type.bodyMedium, color = colors.muted)
            return@AbCard
        }

        DayStrip(panel)
        StatTiles(
            listOf(
                StatCard("Deep work", panel.focus, panel.focusNote),
                StatCard("Drift", panel.driftCount.toString(), if (panel.driftCount == 0) "no drift in your sessions" else "stretches off track · ${panel.driftTime}"),
            ),
        )
        if (panel.rows.isEmpty()) {
            Text("Nothing was counted on this day.", style = Ab.type.bodyMedium, color = colors.muted)
        } else {
            Column {
                panel.rows.forEach { row -> DayRowLine(row) }
            }
        }
    }
}

// The 12 AM to 12 AM strip, its time labels and its legend. Sleep first, focus over it, drift last, so a visit off track stays visible.
@Composable
private fun DayStrip(panel: DayPanel) {
    val colors = Ab.colors
    // Focus is a meaning (goal met) and goes quiet in Quiet colour, so the accent stays for progress; drift and sleep keep their colours.
    val focus = Ab.meaning(colors.met)
    val description = panel.spans.joinToString("; ") { it.description }.ifEmpty { "Nothing on the strip" }
    Column(Modifier.fillMaxWidth().semantics(mergeDescendants = true) { contentDescription = "Day strip, 12 AM to 12 AM. $description" }) {
        Canvas(Modifier.fillMaxWidth().height(STRIP_HEIGHT)) {
            val radius = CornerRadius(size.height / 2, size.height / 2)
            drawRoundRect(colors.surface2, size = size, cornerRadius = radius)
            for (kind in listOf(DayStripKind.Sleep, DayStripKind.Focus, DayStripKind.Drift)) {
                panel.spans.filter { it.kind == kind }.forEach { span ->
                    val from = size.width * span.from
                    val width = maxOf(size.width * (span.to - span.from), 2.dp.toPx())
                    val inset = if (kind == DayStripKind.Drift) 3.dp.toPx() else 0f
                    drawRoundRect(
                        stripColor(kind, colors.sleep, focus, colors.drift), Offset(from, inset), Size(width, size.height - inset * 2),
                        CornerRadius(minOf(radius.x, width / 2, 4.dp.toPx())),
                    )
                }
            }
        }

        Row(Modifier.fillMaxWidth().padding(top = 4.dp).clearAndSetSemantics { }, horizontalArrangement = Arrangement.SpaceBetween) {
            listOf("12 AM", "6 AM", "12 PM", "6 PM", "12 AM").forEach { Text(it, style = Ab.type.micro, color = colors.muted) }
        }
    }

    Row(Modifier.fillMaxWidth().clearAndSetSemantics { }, horizontalArrangement = Arrangement.spacedBy(16.dp), verticalAlignment = Alignment.CenterVertically) {
        LegendDot("Focus", focus)
        LegendDot("Drift", colors.drift)
        LegendDot("Sleep", colors.sleep)
    }
}

private fun stripColor(kind: DayStripKind, sleep: Color, focus: Color, drift: Color): Color = when (kind) {
    DayStripKind.Sleep -> sleep
    DayStripKind.Focus -> focus
    DayStripKind.Drift -> drift
}

@Composable
private fun LegendDot(text: String, color: Color) {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        Box(Modifier.size(8.dp).background(color, CircleShape))
        Text(text, style = Ab.type.micro, color = Ab.colors.muted)
    }
}

@Composable
private fun DayRowLine(row: DayListRow) {
    val colors = Ab.colors
    Row(
        Modifier.fillMaxWidth().heightIn(min = 44.dp).padding(vertical = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Text(row.time, style = Ab.type.small, color = colors.muted, modifier = Modifier.widthIn(min = 68.dp), maxLines = 1, softWrap = false)
        Box(Modifier.size(8.dp).background(if (row.isDrift) colors.drift else Ab.meaning(colors.met), CircleShape))
        Text(row.title, style = Ab.type.bodyMedium, color = colors.ink, modifier = Modifier.weight(1f))
        Text(row.length, style = Ab.type.small, color = colors.muted, textAlign = TextAlign.End, fontWeight = FontWeight.Medium)
    }
}

private val STRIP_HEIGHT = 18.dp
