package com.accountabilibuddy.companion.ui.plan

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The day's parts, top to bottom (board 5c): each with its start time, a 6 dp bar (the sleep tone for sleep, the part's category
 * colour in Standard colour and the plan tone in Quiet), a title and a muted line. The work block opens into rounded lines
 * (deep-work stretches, breaks, calendar events) with today's "Now" rule in the accent. Hairlines between parts. It has no
 * width of its own: the time column grows with the text size.
 */
@Composable
fun PlanTimeline(rows: List<PlanTimelineRow>, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    // The time column follows the text size, so "10:15 PM" stays on one line at a large font.
    val timeWidth = (64 * LocalDensity.current.fontScale.coerceIn(1f, 1.6f)).dp
    Column(modifier.fillMaxWidth()) {
        rows.forEachIndexed { index, row ->
            if (index > 0) Box(Modifier.fillMaxWidth().height(1.dp).background(colors.line))
            TimelineRow(row, timeWidth)
        }
    }
}

@Composable
private fun TimelineRow(row: PlanTimelineRow, timeWidth: androidx.compose.ui.unit.Dp) {
    val colors = Ab.colors
    Row(
        Modifier.fillMaxWidth().height(IntrinsicSize.Min).padding(vertical = 14.dp),
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Text(row.time, style = Ab.type.small, color = colors.muted, maxLines = 1, modifier = Modifier.width(timeWidth).padding(top = 2.dp))
        Box(Modifier.width(6.dp).fillMaxHeight().background(barColor(row), Ab.shapes.pill))
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(row.title, style = Ab.type.body, fontWeight = FontWeight.SemiBold, color = colors.ink)
            if (row.sub != null) Text(row.sub, style = Ab.type.small, color = colors.muted)
            if (row.subRows.isNotEmpty()) {
                Spacer(Modifier.height(8.dp))
                Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    for (line in row.subRows) {
                        if (line.isNowLine) NowLine(line.title) else SubRow(line)
                    }
                }
            }
        }
    }
}

// Sleep is always the sleep tone. Other parts show their category's colour in Standard colour and the plan tone in Quiet,
// so the plan reads calmly by default. A row with no colour of its own (an all-day event) takes the plan tone.
@Composable
private fun barColor(row: PlanTimelineRow): Color {
    val colors = Ab.colors
    return when {
        row.isSleep -> colors.sleep
        row.argb == null || Ab.quiet -> colors.plan
        else -> Color(row.argb.toInt())
    }
}

@Composable
private fun SubRow(line: PlanSubRow) {
    val colors = Ab.colors
    Surface(Modifier.fillMaxWidth(), shape = Ab.shapes.mediumShape, color = colors.surface2, contentColor = colors.ink) {
        Row(
            Modifier.heightIn(min = 44.dp).padding(horizontal = 14.dp, vertical = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text(line.time, style = Ab.type.small, color = colors.muted, maxLines = 1)
            Text(line.title, style = Ab.type.bodyMedium, color = if (line.isEvent) colors.ink else colors.muted, modifier = Modifier.weight(1f))
            if (line.right != null) Text(line.right, style = Ab.type.small, color = colors.muted)
        }
    }
}

/** "— Now · 1:38 PM ———": one of the two places the page uses the accent. */
@Composable
private fun NowLine(text: String) {
    val accent = Ab.colors.accent
    Row(Modifier.fillMaxWidth().heightIn(min = 24.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        Box(Modifier.width(12.dp).height(1.dp).background(accent))
        Text(text, style = Ab.type.micro, fontWeight = FontWeight.SemiBold, color = accent, maxLines = 1)
        Box(Modifier.weight(1f).height(1.dp).background(accent))
    }
}
