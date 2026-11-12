package com.accountabilibuddy.companion.ui.goals

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalDayPanel

/**
 * The heatmap's day panel (brief §4.3, board Goals-Phone-Habit): always one day, today until a cell is tapped, then "Back to today".
 * One row: the date ("Today · Wed 11/11") and under it one line, "8 of 15 min · in progress · 7 min to go" (the status in its
 * colour: teal counted, amber short or missed, grey in progress or paused), with what can be done at the right: **Tick it** /
 * Untick (a habit; an untick is a hand entry of 0) or Edit (a value by hand, any other goal but Deep work). The source line follows
 * under the row. Back-filling a past day is allowed: it updates that week and can rejoin a streak, and never changes a guard
 * decision (guards never read goal entries).
 *
 * @param onTick Ticks (1) or unticks (0) the shown day.
 * @param onSave Saves a value by hand for the shown day.
 */
@Composable
fun GoalDayPanelCard(panel: GoalDayPanel, onBackToToday: (() -> Unit)?, onTick: (Double) -> Unit, onSave: (Double) -> Boolean, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    var editing by rememberSaveable(panel.day) { mutableStateOf(false) }
    var text by rememberSaveable(panel.day) { mutableStateOf("") }
    val tone = when (panel.tone) {
        GoalDayPanel.Tone.Teal -> colors.met
        GoalDayPanel.Tone.Amber -> colors.amberInk
        GoalDayPanel.Tone.Grey -> colors.muted
    }
    Column(
        modifier.fillMaxWidth().clip(Ab.shapes.mediumShape).background(colors.surface2).padding(start = 14.dp, end = 10.dp, top = 10.dp, bottom = 10.dp),
        verticalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Column(Modifier.weight(1f)) {
                Text(panel.title, style = Ab.type.bodyMedium.copy(fontSize = 14.5.sp), fontWeight = FontWeight.Bold, color = colors.ink, maxLines = 1)
                Text(
                    buildAnnotatedString {
                        append(listOfNotNull(panel.big, panel.of).joinToString(" "))
                        append(" · ")
                        // The PC's pill reads "In progress · 7 min to go"; in a line it is lower case, as the board writes it.
                        withStyle(SpanStyle(color = tone, fontWeight = FontWeight.Medium)) { append(panel.status.replaceFirstChar { it.lowercase() }) }
                    },
                    style = Ab.type.small.copy(fontFeatureSettings = "tnum"), color = colors.muted,
                )
            }
            if (!editing && panel.canTick) {
                val untick = panel.tickText == "Untick"
                PanelButton(if (untick) "Untick" else "Tick it", primary = !untick, onClick = { onTick(if (untick) 0.0 else 1.0) })
            } else if (!editing && panel.canEdit) {
                AbTextButton("Edit", onClick = { editing = true })
            }
        }
        Text(panel.source, style = Ab.type.micro.copy(fontSize = 12.5.sp, letterSpacing = 0.sp), color = colors.muted, modifier = Modifier.padding(end = 4.dp))
        if (onBackToToday != null) AbTextButton("Back to today", onClick = onBackToToday)
        if (editing && panel.canEdit) {
            AbTextField(text, { text = it }, panel.editCaption, keyboardType = KeyboardType.Decimal)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                AbPrimaryButton("Save", onClick = {
                    val value = text.trim().replace(',', '.').toDoubleOrNull()
                    if (value != null && value >= 0 && onSave(value)) editing = false
                }, enabled = text.trim().replace(',', '.').toDoubleOrNull()?.let { it >= 0 } == true)
                AbTextButton("Cancel", onClick = { editing = false })
            }
        }
    }
}

// The panel's own button: a 38 dp pill (the board's "Tick it"), accent for the thing to do, tonal for taking it back. The touch
// target is Material's 48 dp around it.
@Composable
private fun PanelButton(text: String, primary: Boolean, onClick: () -> Unit) {
    val colors = Ab.colors
    Surface(
        onClick = onClick,
        shape = Ab.shapes.pill,
        color = if (primary) colors.accent else colors.surface,
        contentColor = if (primary) colors.onAccent else colors.ink,
    ) {
        Box(Modifier.height(38.dp).padding(horizontal = 18.dp), contentAlignment = Alignment.Center) {
            Text(text, style = Ab.type.bodyMedium.copy(fontSize = 14.5.sp), fontWeight = FontWeight.SemiBold, maxLines = 1)
        }
    }
}
