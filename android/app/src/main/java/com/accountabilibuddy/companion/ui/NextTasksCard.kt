package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.outlined.ArrowDownward
import androidx.compose.material.icons.outlined.ArrowUpward
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material3.Card
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
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
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.sync.OpenComebacks
import com.accountabilibuddy.companion.sync.OverLimitStore
import com.accountabilibuddy.companion.sync.TaskCategoryChoices
import com.accountabilibuddy.companion.sync.TaskCategoryChoices.Choice
import com.accountabilibuddy.shared.sync.SyncNextTask

/**
 * The next session's task list (ADR 24, 48), the same list on every device: add, tap a task to edit its title, category
 * and steps, move it up or down among its category's tasks, delete it. Numbered 1., 2., 3. with lettered steps a., b.,
 * as on the PC. A 4th task asks "Keep it to 3: park this for later?" (Park, or Add anyway with a reason that stays on
 * this phone). Changes are in this phone's copy at once and reach the other devices with the next sync, where the newest
 * edit of each task wins.
 */
@Composable
fun NextTasksCard() {
    val app = LocalContext.current.applicationContext as CompanionApp
    val tasks by app.sharedLists.nextTasks.collectAsState()
    val received by app.sync.received.collectAsState()
    // A PC that predates the shared lists (ADR 48) never gets this list, as with the come-backs.
    val oldPc = remember(received) { OpenComebacks.pcWithoutSharedLists(received, java.time.Instant.now()) }
    val categories = remember(tasks, received) { TaskCategoryChoices.forNextTasks(tasks, received) }
    val groups = remember(tasks) { groupedByCategory(tasks) }
    val numbers = remember(groups) { groups.flatMap { it.second }.mapIndexed { index, task -> task.id to index + 1 }.toMap() }
    var title by remember { mutableStateOf("") }
    var chosen by remember { mutableStateOf<Choice?>(null) }
    // The task being edited, and the 4th task held on the "Keep it to 3" prompt (its text and category).
    var editing by remember { mutableStateOf<SyncNextTask?>(null) }
    var held by remember { mutableStateOf<Pair<String, Choice?>?>(null) }

    fun add(text: String, category: Choice?) {
        app.sharedLists.addNextTask(text, category?.name, category?.color)
    }

    fun submit() {
        val text = title.trim()
        if (text.isEmpty()) {
            return
        }

        if (tasks.size >= OverLimitStore.MAX_TASKS) {
            held = text to chosen
        } else {
            add(text, chosen)
        }
        title = ""
    }

    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            IconTitle("Next session", style = MaterialTheme.typography.titleSmall)
            if (oldPc != null) {
                Text(
                    "Update AccountabiliBuddy on $oldPc to share this list",
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            if (tasks.isEmpty()) {
                Text("No tasks yet.", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            groups.forEach { (category, inGroup) ->
                if (groups.any { it.first != null }) {
                    CategoryLabel(category?.name, category?.color)
                }
                inGroup.forEachIndexed { index, task ->
                    NextTaskRow(
                        task, numbers.getValue(task.id),
                        canMoveUp = index > 0, canMoveDown = index < inGroup.lastIndex,
                        onEdit = { editing = task },
                        onMove = { by -> app.sharedLists.moveNextTask(task.id, index + by) },
                        onDelete = { app.sharedLists.deleteNextTask(task.id) },
                    )
                }
            }
            Row(verticalAlignment = Alignment.CenterVertically) {
                OutlinedTextField(
                    value = title, onValueChange = { title = it }, singleLine = true, modifier = Modifier.weight(1f),
                    placeholder = { Text("Add a task") },
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
                    keyboardActions = KeyboardActions(onDone = { submit() }),
                )
                SmallIconButton(Icons.Outlined.Add, "Add task", enabled = title.isNotBlank()) { submit() }
            }
            if (categories.isNotEmpty()) {
                TaskCategoryMenu(categories, chosen?.name, onSelect = { chosen = it })
            }
        }
    }

    editing?.let { task ->
        // The task may have been deleted by a sync while the dialog was open.
        TaskEditDialog(
            title = task.title, steps = task.subtasks.orEmpty(), categories = categories, categoryName = task.category,
            onSave = { newTitle, category, steps ->
                app.sharedLists.editNextTask(task.id, newTitle, category?.name, category?.color, steps)
                editing = null
            },
            onDismiss = { editing = null },
        )
    }

    held?.let { (text, category) ->
        OverLimitDialog(
            title = text,
            onPark = { app.sharedLists.addParked(text); held = null },
            onAddAnyway = { reason ->
                if (app.overLimit.record(reason)) {
                    add(text, category)
                    held = null
                }
            },
            onCancel = { held = null },
        )
    }
}

// The tasks grouped by category (name) in the order each first appears, tasks with no category last; the numbers run on
// through the groups (the PC's order, ADR 24).
private fun groupedByCategory(tasks: List<SyncNextTask>): List<Pair<Choice?, List<SyncNextTask>>> {
    val named = tasks.filter { it.category != null }.groupBy { it.category }.map { (name, inGroup) -> Choice(name!!, inGroup.first().categoryColor) to inGroup }
    val none = tasks.filter { it.category == null }
    return if (none.isEmpty()) named else named + (null to none)
}

@Composable
private fun CategoryLabel(name: String?, color: String?) {
    Row(Modifier.fillMaxWidth().padding(top = 6.dp), verticalAlignment = Alignment.CenterVertically) {
        if (name != null) {
            Box(Modifier.size(8.dp).background(parseCategoryColor(color) ?: MaterialTheme.colorScheme.secondary, CircleShape))
            Spacer(Modifier.width(6.dp))
        }
        Text(
            name ?: "No category", style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant,
            maxLines = 1, overflow = TextOverflow.Ellipsis,
        )
    }
}

@Composable
private fun NextTaskRow(
    task: SyncNextTask,
    number: Int,
    canMoveUp: Boolean,
    canMoveDown: Boolean,
    onEdit: () -> Unit,
    onMove: (by: Int) -> Unit,
    onDelete: () -> Unit,
) {
    Column {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text("$number. ${task.title}", style = MaterialTheme.typography.bodyMedium, modifier = Modifier.weight(1f).clickable(onClick = onEdit).padding(vertical = 6.dp))
            SmallIconButton(Icons.Outlined.ArrowUpward, "Move task up", enabled = canMoveUp) { onMove(-1) }
            SmallIconButton(Icons.Outlined.ArrowDownward, "Move task down", enabled = canMoveDown) { onMove(1) }
            SmallIconButton(Icons.Outlined.Delete, "Delete task", onClick = onDelete)
        }
        task.subtasks.orEmpty().forEachIndexed { index, step ->
            Text("${subtaskLetter(index + 1)} ${step.title}", style = MaterialTheme.typography.bodyMedium, modifier = Modifier.padding(start = 20.dp))
        }
    }
}
