package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Card
import androidx.compose.material3.Checkbox
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.sync.OpenComebacks
import com.accountabilibuddy.companion.sync.deviceIdOf
import com.accountabilibuddy.shared.sync.SyncAction

/**
 * "Come back to" (ADR 30): the PC's parked thoughts. Ticking one or parking a new one goes to that PC through the sync
 * files (ADR 26); a tick shows at once and the PC's list catches up at its next sync. Read-only when the PC isn't in
 * the device group (no sync files from it).
 */
@Composable
fun ComebacksCard(comebacks: OpenComebacks) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val received by app.sync.received.collectAsState()
    val pending by app.actions.pending.collectAsState()
    val target = deviceIdOf(comebacks.source, received)
    var text by remember { mutableStateOf("") }
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            IconTitle("Come back to · ${comebacks.source}", style = MaterialTheme.typography.titleSmall)
            if (comebacks.items.isEmpty()) {
                Text("Nothing parked.", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            comebacks.items.forEach { item ->
                val ticked = target != null && pending.any { it.target == target && it.item == item.id && it.kind == SyncAction.TICK_COMEBACK }
                Row(verticalAlignment = Alignment.CenterVertically) {
                    if (target != null) {
                        Checkbox(checked = ticked, enabled = !ticked, onCheckedChange = { app.actions.add(SyncAction.TICK_COMEBACK, target, item = item.id) })
                    }
                    Text(
                        if (target == null) "• ${item.text}" else item.text,
                        style = MaterialTheme.typography.bodyMedium,
                        textDecoration = if (ticked) TextDecoration.LineThrough else null,
                    )
                }
            }
            // Parked here, it waits on that PC under Come back to.
            pending.filter { it.target == target && it.kind == SyncAction.PARK }.forEach { parked ->
                Text("⚑ ${parked.text} · sending…", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            if (target != null) {
                fun park() {
                    val thought = text.trim()
                    if (thought.isNotEmpty()) {
                        app.actions.add(SyncAction.PARK, target, text = thought)
                        text = ""
                    }
                }
                Row(verticalAlignment = Alignment.CenterVertically) {
                    OutlinedTextField(
                        value = text, onValueChange = { text = it }, singleLine = true, modifier = Modifier.weight(1f),
                        placeholder = { Text("Park a thought for ${comebacks.source}") },
                        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
                        keyboardActions = KeyboardActions(onDone = { park() }),
                    )
                    TextButton(onClick = { park() }, enabled = text.isNotBlank()) { Text("Park") }
                }
            }
        }
    }
}
