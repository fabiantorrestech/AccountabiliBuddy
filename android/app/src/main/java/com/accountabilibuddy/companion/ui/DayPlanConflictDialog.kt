package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.model.DayPlanConflict
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId
import java.time.format.DateTimeFormatter

private val DayFormat = DateTimeFormatter.ofPattern("EEE M/d")

/** "4:12 PM" today, else "Mon 10/19, 4:12 PM". */
private fun whenText(instant: Instant, clock: ClockFormat): String {
    val local = instant.atZone(ZoneId.systemDefault()).toLocalDateTime()
    return if (local.toLocalDate() == LocalDate.now()) clock.time(local) else "${local.format(DayFormat)}, ${clock.time(local)}"
}

/**
 * The plan changed on the PC and on this phone (ADR 19): both versions with when each was edited (the newer one
 * outlined), what each changed as bullet points, and a choice. Nothing is lost silently. It can't be dismissed without
 * choosing, so it is the theme's dialog written out here rather than the kit's (which closes on a tap outside).
 */
@Composable
fun DayPlanConflictDialog(conflict: DayPlanConflict, pcName: String?, clock: ClockFormat, onKeepPc: () -> Unit, onUseMine: () -> Unit) {
    val colors = Ab.colors
    AlertDialog(
        onDismissRequest = { },
        containerColor = colors.surface,
        titleContentColor = colors.ink,
        textContentColor = colors.ink,
        shape = Ab.shapes.cardShape,
        title = { Text("The plan changed in two places", style = Ab.type.section) },
        text = {
            Column(Modifier.heightIn(max = 460.dp).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Text("Pick the one to keep; the other is dropped.", style = Ab.type.bodyMedium, color = colors.muted)
                Side("On ${pcName ?: "the PC"}", conflict.pcEditedAt, clock, conflict.pcChanges, newer = conflict.pcIsNewer)
                Side("On this phone", conflict.phoneEditedAt, clock, conflict.phoneChanges, newer = !conflict.pcIsNewer)
            }
        },
        confirmButton = { AbTextButton("Use this phone's", onUseMine) },
        dismissButton = { AbTextButton("Keep ${pcName ?: "the PC"}'s", onKeepPc) },
    )
}

@Composable
private fun Side(title: String, editedAt: Instant?, clock: ClockFormat, changes: List<String>, newer: Boolean) {
    val colors = Ab.colors
    Surface(
        Modifier.fillMaxWidth(),
        shape = Ab.shapes.mediumShape,
        color = colors.surface2,
        contentColor = colors.ink,
        border = if (newer) BorderStroke(2.dp, colors.ink) else BorderStroke(1.dp, colors.line),
    ) {
        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(title, style = Ab.type.bodyMedium, fontWeight = FontWeight.SemiBold, color = colors.ink)
            val edited = editedAt?.let { "edited ${whenText(it, clock)}" } ?: "edit time unknown"
            Text(
                if (newer) "$edited · newer" else edited,
                style = Ab.type.small, color = if (newer) colors.ink else colors.muted, fontWeight = if (newer) FontWeight.SemiBold else FontWeight.Normal,
            )
            for (change in changes) {
                Text("• $change", style = Ab.type.bodyMedium, color = colors.ink)
            }
        }
    }
}
