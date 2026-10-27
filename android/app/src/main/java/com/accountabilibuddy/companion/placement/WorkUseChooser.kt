package com.accountabilibuddy.companion.placement

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import java.time.Duration
import java.time.Instant

/**
 * "Need it for work": what for (call, 2FA, work app, other), a short note and 5, 10 or 15 minutes. From the 4th use in
 * a session the note is longer and a 30-second wait comes first. [onStarted] after a use starts; [onBack] to leave.
 * The note stays on this phone.
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun WorkUseChooser(app: CompanionApp, onStarted: () -> Unit, onBack: () -> Unit, light: Boolean = true) {
    val store = app.workUse
    val session = remember { PlacementSession.current(app, Instant.now()) }
    // Not during a break: nothing is parked then.
    val sessionStart = session?.takeUnless { it.onBreak }?.start
    val textColor = if (light) Color.White else MaterialTheme.colorScheme.onSurface
    val dim = if (light) Color(0xCCFFFFFF) else MaterialTheme.colorScheme.onSurfaceVariant
    Column(verticalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.fillMaxWidth()) {
        Text("I need it for work", color = textColor, style = MaterialTheme.typography.titleLarge)
        if (sessionStart == null) {
            Text(if (session?.onBreak == true) "Not during a break." else "No session is running.", color = dim, style = MaterialTheme.typography.bodyMedium)
            TextButton(onClick = onBack) { Text("Back", color = dim) }
            return@Column
        }

        val terms = remember { store.termsFor(sessionStart) }
        val allowedFrom = remember { store.begin(sessionStart) }
        var now by remember { mutableStateOf(Instant.now()) }
        LaunchedEffect(Unit) {
            while (now.isBefore(allowedFrom)) {
                now = Instant.now()
                kotlinx.coroutines.delay(500)
            }
            now = Instant.now()
        }
        val waitLeft = Duration.between(now, allowedFrom).seconds.coerceAtLeast(0)
        var kind by remember { mutableStateOf<WorkUseKind?>(null) }
        var note by remember { mutableStateOf("") }
        var minutes by remember { mutableStateOf<Int?>(null) }
        Text(
            if (terms.waitSeconds == 0) "Not counted as the phone being out while it lasts. A short note (${terms.minNoteLength}+ characters); it stays on this phone."
            else "That's more than ${WorkUseStore.FREE_USES} in this session: a longer note (${terms.minNoteLength}+ characters) and a short wait.",
            color = dim, style = MaterialTheme.typography.bodyMedium,
        )
        Text("What for?", color = dim, style = MaterialTheme.typography.labelLarge)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            WorkUseKind.entries.forEach { option ->
                FilterChip(selected = kind == option, onClick = { kind = option }, label = { Text(option.label) })
            }
        }
        OutlinedTextField(
            value = note, onValueChange = { note = it }, label = { Text("Note (${note.trim().length}/${terms.minNoteLength})") },
            modifier = Modifier.fillMaxWidth(),
        )
        Text("For how long?", color = dim, style = MaterialTheme.typography.labelLarge)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            WorkUseStore.LENGTHS.forEach { length ->
                FilterChip(selected = minutes == length, onClick = { minutes = length }, label = { Text("$length min") })
            }
        }
        val ready = kind != null && minutes != null && note.trim().length >= terms.minNoteLength && waitLeft == 0L
        Button(
            enabled = ready,
            onClick = {
                if (store.start(sessionStart, kind!!, note, minutes!!)) {
                    app.workUseChanged()
                    onStarted()
                }
            },
            modifier = Modifier.fillMaxWidth(),
        ) { Text(if (waitLeft > 0) "Wait ${waitLeft}s" else minutes?.let { "Use my phone for work for $it min" } ?: "Use my phone for work") }
        TextButton(onClick = onBack) { Text("Back", color = dim) }
    }
}
