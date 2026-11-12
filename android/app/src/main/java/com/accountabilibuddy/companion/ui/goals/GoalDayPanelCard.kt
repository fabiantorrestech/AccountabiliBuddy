package com.accountabilibuddy.companion.ui.goals

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalDayPanel

/**
 * The heatmap's day panel (brief §4.3): always one day, today until a cell is tapped, then "Back to today". The date, "8 of 15 min", a
 * status pill (teal counted, amber short or missed, grey in progress or paused), the source line, and what can be done: Tick it /
 * Untick (a habit; an untick is a hand entry of 0) or a value by hand (any other goal but Deep work). Back-filling a past day is
 * allowed: it updates that week and can rejoin a streak, and never changes a guard decision (guards never read goal entries).
 *
 * @param onTick Ticks (1) or unticks (0) the shown day.
 * @param onSave Saves a value by hand for the shown day.
 */
@Composable
fun GoalDayPanelCard(panel: GoalDayPanel, onBackToToday: (() -> Unit)?, onTick: (Double) -> Unit, onSave: (Double) -> Boolean, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    var editing by rememberSaveable(panel.day) { mutableStateOf(false) }
    var text by rememberSaveable(panel.day) { mutableStateOf("") }
    Column(
        modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(colors.surface2).padding(14.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(panel.title, style = Ab.type.bodyMedium, fontWeight = FontWeight.Bold, color = colors.ink, modifier = Modifier.weight(1f))
            if (onBackToToday != null) AbTextButton("Back to today", onClick = onBackToToday)
        }
        Row(verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(panel.big, style = Ab.type.bodyMedium.copy(fontFeatureSettings = "tnum"), fontWeight = FontWeight.SemiBold, color = colors.ink)
            panel.of?.let { Text(it, style = Ab.type.small.copy(fontFeatureSettings = "tnum"), color = colors.muted) }
            Text("·", style = Ab.type.small, color = colors.faint)
            val tone = when (panel.tone) {
                GoalDayPanel.Tone.Teal -> colors.met
                GoalDayPanel.Tone.Amber -> colors.amberInk
                GoalDayPanel.Tone.Grey -> colors.muted
            }
            Text(panel.status, style = Ab.type.small, fontWeight = FontWeight.SemiBold, color = tone)
        }
        Text(panel.source, style = Ab.type.small, color = colors.muted)
        panel.note?.let { Text(it, style = Ab.type.micro, color = colors.faint) }
        if (editing && panel.canEdit) {
            AbTextField(text, { text = it }, panel.editCaption, keyboardType = KeyboardType.Decimal)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                AbPrimaryButton("Save", onClick = {
                    val value = text.trim().replace(',', '.').toDoubleOrNull()
                    if (value != null && value >= 0 && onSave(value)) editing = false
                }, enabled = text.trim().replace(',', '.').toDoubleOrNull()?.let { it >= 0 } == true)
                AbTextButton("Cancel", onClick = { editing = false })
            }
        } else if (panel.canTick || panel.canEdit) {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.heightIn(min = 52.dp)) {
                if (panel.canTick) {
                    if (panel.tickText == "Untick") AbTonalButton(panel.tickText, onClick = { onTick(0.0) }) else AbPrimaryButton(panel.tickText, onClick = { onTick(1.0) })
                }
                if (panel.canEdit) AbTonalButton("Edit", onClick = { editing = true })
            }
        }
    }
}
