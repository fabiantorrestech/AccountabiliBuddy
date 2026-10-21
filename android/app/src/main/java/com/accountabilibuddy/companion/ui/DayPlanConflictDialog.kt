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
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedCard
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.model.DayPlanConflict
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId
import java.time.format.DateTimeFormatter

private val Newer = Color(0xFF14B8A6)
private val DayFormat = DateTimeFormatter.ofPattern("EEE M/d")

/** "4:12 PM" today, else "Mon 10/19, 4:12 PM". */
private fun whenText(instant: Instant, clock: ClockFormat): String {
    val local = instant.atZone(ZoneId.systemDefault()).toLocalDateTime()
    return if (local.toLocalDate() == LocalDate.now()) clock.time(local) else "${local.format(DayFormat)}, ${clock.time(local)}"
}

/**
 * The plan changed on the PC and on this phone (ADR 19): both versions with when each was edited (the newer one
 * outlined), what each changed as bullet points, and a choice. Nothing is lost silently.
 */
@Composable
fun DayPlanConflictDialog(conflict: DayPlanConflict, pcName: String?, clock: ClockFormat, onKeepPc: () -> Unit, onUseMine: () -> Unit) {
    AlertDialog(
        onDismissRequest = { },
        title = { Text("The plan changed in two places") },
        text = {
            Column(Modifier.heightIn(max = 460.dp).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Text("Pick the one to keep; the other is dropped.", style = MaterialTheme.typography.bodyMedium)
                Side("On ${pcName ?: "the PC"}", conflict.pcEditedAt, clock, conflict.pcChanges, newer = conflict.pcIsNewer)
                Side("On this phone", conflict.phoneEditedAt, clock, conflict.phoneChanges, newer = !conflict.pcIsNewer)
            }
        },
        confirmButton = { TextButton(onClick = onUseMine) { Text("Use this phone's") } },
        dismissButton = { TextButton(onClick = onKeepPc) { Text("Keep ${pcName ?: "the PC"}'s") } },
    )
}

@Composable
private fun Side(title: String, editedAt: Instant?, clock: ClockFormat, changes: List<String>, newer: Boolean) {
    OutlinedCard(
        border = if (newer) BorderStroke(2.dp, Newer) else BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant),
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(title, fontWeight = FontWeight.SemiBold)
            val edited = editedAt?.let { "edited ${whenText(it, clock)}" } ?: "edit time unknown"
            Text(if (newer) "$edited · newer" else edited, style = MaterialTheme.typography.bodySmall, color = if (newer) Newer else MaterialTheme.colorScheme.onSurfaceVariant)
            for (change in changes) {
                Text("• $change", style = MaterialTheme.typography.bodyMedium)
            }
        }
    }
}
