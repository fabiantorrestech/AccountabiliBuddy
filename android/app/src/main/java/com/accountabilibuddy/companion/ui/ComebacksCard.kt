package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.MoreVert
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Card
import androidx.compose.material3.Checkbox
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
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
import com.accountabilibuddy.companion.sync.OverLimitStore
import com.accountabilibuddy.companion.sync.deviceIdOf
import com.accountabilibuddy.shared.sync.SyncAction

/**
 * "Come back to" (ADR 30, 48): the user's parked thoughts, one list on every device. Add one, tick it, edit its text,
 * delete it or move it to the next session's tasks (past 3 tasks it asks for a reason, like adding a 4th). Changes are in
 * this phone's copy at once and reach the PCs with the next sync, where the newest edit of each thought wins.
 *
 * Next to a PC that predates the shared list ([OpenComebacks.legacySource]) the card shows that PC's open come-backs, and
 * ticking or parking goes to it through the sync files (ADR 26) as before; it asks to update that PC.
 */
@Composable
fun ComebacksCard(comebacks: OpenComebacks) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val received by app.sync.received.collectAsState()
    val pending by app.actions.pending.collectAsState()
    val nextTasks by app.sharedLists.nextTasks.collectAsState()
    val legacySource = comebacks.legacySource
    val target = if (legacySource == null) null else deviceIdOf(legacySource, received)
    var text by remember { mutableStateOf("") }
    var editing by remember { mutableStateOf<OpenComebacks.Row?>(null) }
    var moving by remember { mutableStateOf<OpenComebacks.Row?>(null) }

    fun moveToNextTasks(row: OpenComebacks.Row) {
        app.sharedLists.addNextTask(row.text)
        app.sharedLists.tickParked(row.id)
    }

    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            IconTitle(if (legacySource == null) "Come back to" else "Come back to · $legacySource", style = MaterialTheme.typography.titleSmall)
            if (legacySource != null) {
                Text(
                    "Update AccountabiliBuddy on $legacySource to edit come-backs here",
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            if (comebacks.items.isEmpty()) {
                Text("Nothing parked.", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            comebacks.items.forEach { item ->
                val ticked = target != null && pending.any { it.target == target && it.item == item.id && it.kind == SyncAction.TICK_COMEBACK }
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Checkbox(
                        checked = ticked, enabled = !ticked,
                        onCheckedChange = {
                            if (legacySource == null) app.sharedLists.tickParked(item.id)
                            else if (target != null) app.actions.add(SyncAction.TICK_COMEBACK, target, item = item.id)
                        },
                    )
                    Text(
                        item.text, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.weight(1f),
                        textDecoration = if (ticked) TextDecoration.LineThrough else null,
                    )
                    if (legacySource == null) {
                        ComebackMenu(
                            onEdit = { editing = item },
                            onMoveToNext = { if (nextTasks.size >= OverLimitStore.MAX_TASKS) moving = item else moveToNextTasks(item) },
                            onDelete = { app.sharedLists.deleteParked(item.id) },
                        )
                    }
                }
            }
            // Parked here for an older PC, it waits on that PC under Come back to.
            pending.filter { it.target == target && it.kind == SyncAction.PARK }.forEach { parked ->
                Text("⚑ ${parked.text} · sending…", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            if (legacySource == null || target != null) {
                fun park() {
                    val thought = text.trim()
                    if (thought.isNotEmpty()) {
                        if (legacySource == null) app.sharedLists.addParked(thought) else app.actions.add(SyncAction.PARK, target!!, text = thought)
                        text = ""
                    }
                }
                Row(verticalAlignment = Alignment.CenterVertically) {
                    OutlinedTextField(
                        value = text, onValueChange = { text = it }, singleLine = true, modifier = Modifier.weight(1f),
                        placeholder = { Text(if (legacySource == null) "Park a thought" else "Park a thought for $legacySource") },
                        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
                        keyboardActions = KeyboardActions(onDone = { park() }),
                    )
                    TextButton(onClick = { park() }, enabled = text.isNotBlank()) { Text("Park") }
                }
            }
        }
    }

    editing?.let { row ->
        EditComebackDialog(
            row.text,
            onSave = { newText -> app.sharedLists.editParked(row.id, newText); editing = null },
            onDismiss = { editing = null },
        )
    }

    // Past 3 tasks a come-back moves to the list only with a reason (it stays a come-back otherwise).
    moving?.let { row ->
        OverLimitDialog(
            title = row.text,
            onPark = null,
            onAddAnyway = { reason ->
                if (app.overLimit.record(reason)) {
                    moveToNextTasks(row)
                    moving = null
                }
            },
            onCancel = { moving = null },
        )
    }
}

@Composable
private fun ComebackMenu(onEdit: () -> Unit, onMoveToNext: () -> Unit, onDelete: () -> Unit) {
    var open by remember { mutableStateOf(false) }
    Box {
        SmallIconButton(Icons.Outlined.MoreVert, "More") { open = true }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            DropdownMenuItem(text = { Text("Edit") }, onClick = { open = false; onEdit() })
            DropdownMenuItem(text = { Text("Move to next session") }, onClick = { open = false; onMoveToNext() })
            DropdownMenuItem(text = { Text("Delete") }, onClick = { open = false; onDelete() })
        }
    }
}

@Composable
private fun EditComebackDialog(text: String, onSave: (String) -> Unit, onDismiss: () -> Unit) {
    var edited by remember { mutableStateOf(text) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Edit thought") },
        text = { OutlinedTextField(value = edited, onValueChange = { edited = it }, modifier = Modifier.fillMaxWidth()) },
        confirmButton = { TextButton(onClick = { onSave(edited) }, enabled = edited.isNotBlank()) { Text("Save") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
    )
}
