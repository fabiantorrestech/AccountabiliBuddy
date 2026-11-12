package com.accountabilibuddy.companion.ui.goals

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.History
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalReading
import com.accountabilibuddy.shared.goals.GoalTexts
import com.accountabilibuddy.shared.goals.GoalUnit
import java.time.LocalDate
import java.time.LocalTime

/**
 * "READINGS · LATEST FIRST" (brief §4.3 Weight detail): numbered, a date pill and a time pill per entry, the value in the accent. Every
 * value of a day is kept; the chart uses the day's latest hand edit, else its first reading, and each line says whether it is that one.
 * A tap edits that day's value by hand (a new hand entry, which wins; Q423 stamp).
 */
@Composable
fun GoalReadingsCard(readings: List<GoalReading>, unit: GoalUnit, today: LocalDate, onEditDay: (LocalDate) -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    var all by rememberSaveable { mutableStateOf(false) }
    val shown = if (all) readings else readings.take(PREVIEW)
    AbCard(modifier, contentPadding = androidx.compose.foundation.layout.PaddingValues(14.dp), verticalSpacing = 4.dp) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Icon(Icons.Outlined.History, contentDescription = null, tint = colors.muted, modifier = Modifier.width(18.dp))
            Text("READINGS · LATEST FIRST", style = Ab.type.micro, fontWeight = FontWeight.SemiBold, color = colors.muted)
        }
        if (readings.isEmpty()) {
            Text("No readings yet. Log one above.", style = Ab.type.small, color = colors.faint, modifier = Modifier.padding(vertical = 12.dp))
        }
        shown.forEachIndexed { index, reading ->
            Row(
                Modifier.fillMaxWidth().heightIn(min = 58.dp).clickable(role = Role.Button, onClickLabel = "Edit this day's value") { onEditDay(reading.day) }.padding(vertical = 6.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                Text("${index + 1}", style = Ab.type.small.copy(fontFeatureSettings = "tnum"), color = colors.faint, modifier = Modifier.width(18.dp))
                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                    Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        Pill(if (reading.day == today) "Today  ${GoalTexts.date(reading.day, today)}" else GoalTexts.date(reading.day, today), bold = reading.day == today)
                        Pill(clock12(reading.time))
                    }
                    Text("${reading.source} · ${reading.note}", style = Ab.type.micro, color = if (reading.charted) colors.muted else colors.faint)
                }
                Text(
                    GoalTexts.value(reading.value, unit), style = Ab.type.bodyMedium.copy(fontFeatureSettings = "tnum"), fontWeight = FontWeight.SemiBold,
                    color = if (reading.charted) colors.accent else colors.muted,
                )
            }
        }
        if (readings.size > PREVIEW) AbTextButton(if (all) "Show fewer" else "Show all ${readings.size}", onClick = { all = !all })
    }
}

private const val PREVIEW = 8

@Composable
private fun Pill(text: String, bold: Boolean = false) {
    Text(
        text, style = Ab.type.micro, fontWeight = if (bold) FontWeight.Bold else FontWeight.Medium, color = Ab.colors.ink, maxLines = 1,
        modifier = Modifier.clip(Ab.shapes.pill).background(Ab.colors.surface2).padding(horizontal = 9.dp, vertical = 3.dp),
    )
}

/** "9:00 AM", "12:05 PM": 12-hour with AM / PM whatever the system's 24-hour setting (CLAUDE.md). */
fun clock12(time: LocalTime): String {
    val hour = if (time.hour % 12 == 0) 12 else time.hour % 12
    return "$hour:${time.minute.toString().padStart(2, '0')} ${if (time.hour < 12) "AM" else "PM"}"
}
