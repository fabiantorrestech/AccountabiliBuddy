package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.clickable
import androidx.compose.material3.Card
import androidx.compose.runtime.collectAsState
import androidx.compose.ui.Alignment
import androidx.compose.ui.platform.LocalContext
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.sync.deviceIdOf
import com.accountabilibuddy.shared.sync.SyncAction
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.sync.RunningTasks
import com.accountabilibuddy.shared.sync.SessionTaskItem

/**
 * The running session's checklist (ADR 24), read only: open tasks with their subtasks (the one the PC's bar shows
 * marked ▸), the done ones behind "Done (n)".
 */
@Composable
fun TasksCard(running: RunningTasks) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val received by app.sync.received.collectAsState()
    val pending by app.actions.pending.collectAsState()
    // Ticking goes to the PC through the sync files (ADR 26); without them (no device group) the list is read-only.
    val target = deviceIdOf(running.source, received)
    val ticking: (String?) -> Boolean = { id -> target != null && id != null && pending.any { it.target == target && it.item == id && it.kind == SyncAction.TICK_TASK } }
    val onTick: ((String) -> Unit)? = target?.let { pc -> { id -> app.actions.add(SyncAction.TICK_TASK, pc, item = id) } }
    var showDone by remember { mutableStateOf(false) }
    val open = running.tasks.filter { !it.isDone }
    val done = running.tasks.filter { it.isDone }
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            IconTitle("Tasks · ${running.source} · ${running.doneCount}/${running.tasks.size}", style = MaterialTheme.typography.titleSmall)
            if (open.isEmpty()) {
                Text("All tasks done", style = MaterialTheme.typography.bodyMedium)
            }

            open.forEach { task -> TaskLine(task, indent = false, markedId = running.markedId, ticking, onTick) }
            if (done.isNotEmpty()) {
                TextButton(onClick = { showDone = !showDone }) { Text(if (showDone) "Hide done (${done.size})" else "Done (${done.size})") }
                if (showDone) {
                    done.forEach { task -> TaskLine(task, indent = false, markedId = null, ticking, null) }
                }
            }
        }
    }
}

// [markedId]: the task or subtask the PC's bar shows, marked ▸.
// [onTick]: ticks an open line on the PC (null: read-only); a line ticked here shows done until the PC catches up.
@Composable
private fun TaskLine(task: SessionTaskItem, indent: Boolean, markedId: String?, ticking: (String?) -> Boolean, onTick: ((String) -> Unit)?) {
    val done = task.isDone || ticking(task.id)
    val canTick = !done && onTick != null && task.id != null
    Row(
        Modifier.padding(start = if (indent) 20.dp else 0.dp).fillMaxWidth()
            .clickable(enabled = canTick) { onTick?.invoke(task.id!!) }.padding(vertical = 2.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(
            (if (done) "☑ " else "☐ ") + (if (task.id != null && task.id == markedId) "▸ " else "") + task.title,
            style = MaterialTheme.typography.bodyMedium,
            textDecoration = if (done) TextDecoration.LineThrough else null,
            color = if (done) MaterialTheme.colorScheme.onSurfaceVariant else MaterialTheme.colorScheme.onSurface,
        )
    }
    task.subtasks.forEach { subtask -> TaskLine(subtask, indent = true, markedId = markedId, ticking, onTick) }
}
