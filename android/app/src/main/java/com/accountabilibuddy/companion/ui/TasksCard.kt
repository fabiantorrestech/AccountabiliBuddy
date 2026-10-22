package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Card
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

/** The running session's checklist (ADR 24), read only: open tasks with their subtasks, the done ones behind "Done (n)". */
@Composable
fun TasksCard(running: RunningTasks) {
    var showDone by remember { mutableStateOf(false) }
    val open = running.tasks.filter { !it.isDone }
    val done = running.tasks.filter { it.isDone }
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text("Tasks · ${running.source} · ${running.doneCount}/${running.tasks.size}", style = MaterialTheme.typography.titleSmall)
            if (open.isEmpty()) {
                Text("All tasks done", style = MaterialTheme.typography.bodyMedium)
            }

            open.forEach { task -> TaskLine(task, indent = false) }
            if (done.isNotEmpty()) {
                TextButton(onClick = { showDone = !showDone }) { Text(if (showDone) "Hide done (${done.size})" else "Done (${done.size})") }
                if (showDone) {
                    done.forEach { task -> TaskLine(task, indent = false) }
                }
            }
        }
    }
}

@Composable
private fun TaskLine(task: SessionTaskItem, indent: Boolean) {
    Row(Modifier.padding(start = if (indent) 20.dp else 0.dp)) {
        Text(
            (if (task.isDone) "☑ " else "☐ ") + task.title,
            style = MaterialTheme.typography.bodyMedium,
            textDecoration = if (task.isDone) TextDecoration.LineThrough else null,
            color = if (task.isDone) MaterialTheme.colorScheme.onSurfaceVariant else MaterialTheme.colorScheme.onSurface,
        )
    }
    task.subtasks.forEach { subtask -> TaskLine(subtask, indent = true) }
}
