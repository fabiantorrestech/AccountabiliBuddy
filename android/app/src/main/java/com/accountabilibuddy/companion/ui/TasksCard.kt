package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.clickable
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.material3.Card
import androidx.compose.material3.Icon
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.PushPin
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.outlined.ArrowDownward
import androidx.compose.material.icons.outlined.ArrowUpward
import androidx.compose.material.icons.outlined.Edit
import androidx.compose.material3.OutlinedTextField
import com.accountabilibuddy.companion.sync.TaskCategoryChoices
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
 * The running session's checklist (ADR 24; add, edit and reorder go to the PC as actions once wired in, ADR 48): open tasks with their subtasks (the one the PC's bar shows
 * marked with a pin), the done ones behind "Done (n)". Once any open task has a category, the open ones sit under their
 * category's name and colour dot in the PC's order, "No category" last.
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
    // Adding, editing and reordering go to the PC as sync actions (ADR 48), which applies them under its own rules.
    val edits = app.runningTaskEdits
    val canEdit = edits.isAvailable && target != null
    var editing by remember { mutableStateOf<SessionTaskItem?>(null) }
    var newTitle by remember { mutableStateOf("") }
    var newCategory by remember { mutableStateOf<TaskCategoryChoices.Choice?>(null) }
    var sent by remember { mutableStateOf(false) }
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            IconTitle("Tasks · ${running.source} · ${running.doneCount}/${running.tasks.size}", style = MaterialTheme.typography.titleSmall)
            if (open.isEmpty()) {
                Text("All tasks done", style = MaterialTheme.typography.bodyMedium)
            }

            val groups = categoryGroups(open)
            // One sequence over the open tasks in the order shown (the PC's numbering); done tasks aren't numbered.
            val numbers = groups.flatMap { it.tasks }.mapIndexed { index, task -> task.id to index + 1 }.toMap()
            groups.forEach { group ->
                if (groups.any { it.name != null }) {
                    CategoryHeader(group)
                }

                group.tasks.forEachIndexed { index, task ->
                    // Up and down move a task among its category's tasks, in the order the PC lists them.
                    val trailing: (@Composable () -> Unit)? = if (canEdit && open.all { it.id != null }) {
                        {
                            SmallIconButton(Icons.Outlined.ArrowUpward, "Move task up", enabled = index > 0) {
                                edits.reorderTasks(target!!, swapped(groups, group, index, -1)); sent = true
                            }
                            SmallIconButton(Icons.Outlined.ArrowDownward, "Move task down", enabled = index < group.tasks.lastIndex) {
                                edits.reorderTasks(target!!, swapped(groups, group, index, 1)); sent = true
                            }
                            SmallIconButton(Icons.Outlined.Edit, "Edit task") { editing = task }
                        }
                    } else null
                    TaskLine(task, indent = false, markedId = running.markedId, ticking, onTick, number = numbers[task.id], trailing = trailing)
                }
            }
            if (canEdit) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    OutlinedTextField(
                        value = newTitle, onValueChange = { newTitle = it }, singleLine = true, modifier = Modifier.weight(1f),
                        placeholder = { Text("Add a task on ${running.source}") },
                    )
                    SmallIconButton(Icons.Outlined.Add, "Add task", enabled = newTitle.isNotBlank()) {
                        edits.addTask(target!!, newTitle.trim(), newCategory?.id)
                        newTitle = ""
                        sent = true
                    }
                }
                val categories = TaskCategoryChoices.forChecklist(running.tasks)
                if (categories.isNotEmpty()) {
                    TaskCategoryMenu(categories, newCategory?.name, onSelect = { newCategory = it })
                }
                Text(
                    if (sent) "Sent to ${running.source}. It shows here after its next sync. It keeps the session to 3 tasks and parks a 4th."
                    else "${running.source} keeps the session to 3 tasks and parks a 4th.",
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            if (done.isNotEmpty()) {
                TextButton(onClick = { showDone = !showDone }) { Text(if (showDone) "Hide done (${done.size})" else "Done (${done.size})") }
                if (showDone) {
                    done.forEach { task -> TaskLine(task, indent = false, markedId = null, ticking, null) }
                }
            }
        }
    }

    editing?.let { task ->
        TaskEditDialog(
            title = task.title, steps = task.subtasks, categories = null, categoryName = null,
            onSave = { title, _, steps ->
                val stepTitles = steps.filter { it.title.isNotBlank() }.map { it.title.trim() }
                // Only what changed is sent; a task that was deleted on the PC meanwhile is answered by the PC.
                edits.editTask(
                    target!!, task.id!!, title.trim().takeIf { it != task.title },
                    stepTitles.takeIf { it != task.subtasks.map { step -> step.title } },
                )
                sent = true
                editing = null
            },
            onDismiss = { editing = null },
        )
    }
}

// The open tasks' ids in the order the PC lists them, with the task at [index] of [group] swapped with its neighbour.
private fun swapped(groups: List<CategoryGroup>, group: CategoryGroup, index: Int, by: Int): List<String> {
    val ids = groups.flatMap { it.tasks }.mapNotNull { it.id }.toMutableList()
    val first = ids.indexOf(group.tasks[index].id)
    val second = ids.indexOf(group.tasks[index + by].id)
    ids[first] = group.tasks[index + by].id!!
    ids[second] = group.tasks[index].id!!
    return ids
}

// [markedId]: the task or subtask the PC's bar shows, marked with a pin.
// [onTick]: ticks an open line on the PC (null: read-only); a line ticked here shows done until the PC catches up.
@Composable
private fun TaskLine(
    task: SessionTaskItem, indent: Boolean, markedId: String?, ticking: (String?) -> Boolean, onTick: ((String) -> Unit)?,
    number: Int? = null, letter: String? = null, trailing: (@Composable () -> Unit)? = null,
) {
    val done = task.isDone || ticking(task.id)
    val canTick = !done && onTick != null && task.id != null
    Row(
        Modifier.padding(start = if (indent) 20.dp else 0.dp).fillMaxWidth()
            .clickable(enabled = canTick) { onTick?.invoke(task.id!!) }.padding(vertical = 2.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (task.id != null && task.id == markedId) {
            Icon(Icons.Filled.PushPin, contentDescription = "Current task", modifier = Modifier.padding(end = 4.dp).size(14.dp))
        }
        Text(
            (if (done) "☑ " else "☐ ") + (if (number != null) "$number. " else "") + (if (letter != null) "$letter " else "") + task.title,
            style = MaterialTheme.typography.bodyMedium,
            textDecoration = if (done) TextDecoration.LineThrough else null,
            color = if (done) MaterialTheme.colorScheme.onSurfaceVariant else MaterialTheme.colorScheme.onSurface,
            modifier = if (trailing != null) Modifier.weight(1f) else Modifier,
        )
        trailing?.invoke()
    }
    // Only the open subtasks of an open (numbered) task are lettered a., b., … (aa. after z), as on the PC.
    var place = 0
    task.subtasks.forEach { subtask ->
        val letter = if (number != null && !subtask.isDone) subtaskLetter(++place) else null
        TaskLine(subtask, indent = true, markedId = markedId, ticking, onTick, letter = letter)
    }
}

/** Open tasks of one category; [name] null = no category. */
private class CategoryGroup(val name: String?, val color: String?, val tasks: List<SessionTaskItem>)

// Groups by category id (else name) in the order they first appear, which is the PC's order; tasks with no category last.
private fun categoryGroups(tasks: List<SessionTaskItem>): List<CategoryGroup> {
    val keyOf: (SessionTaskItem) -> String? = { it.categoryId ?: it.category }
    val named = tasks.filter { keyOf(it) != null }.groupBy(keyOf).values.map { inGroup ->
        val first = inGroup.first()
        CategoryGroup(first.category ?: "Category", first.categoryColor, inGroup)
    }
    val uncategorized = tasks.filter { keyOf(it) == null }
    return if (uncategorized.isEmpty()) named else named + CategoryGroup(null, null, uncategorized)
}

@Composable
private fun CategoryHeader(group: CategoryGroup) {
    Row(Modifier.fillMaxWidth().padding(top = 6.dp), verticalAlignment = Alignment.CenterVertically) {
        if (group.name != null) {
            val dot = parseCategoryColor(group.color) ?: MaterialTheme.colorScheme.secondary
            Box(Modifier.size(8.dp).background(dot, CircleShape))
            Spacer(Modifier.width(6.dp))
        }

        Text(
            group.name ?: "No category",
            style = MaterialTheme.typography.labelMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
    }
}

// "#RRGGBB" as the PC sends it; anything else is null, so the caller falls back to the theme's colour.
internal fun parseCategoryColor(hex: String?): Color? {
    if (hex == null || hex.length != 7 || hex[0] != '#') return null
    val rgb = hex.substring(1).toLongOrNull(16) ?: return null
    return Color(0xFF000000 or rgb)
}

/** 1 = "a.", 26 = "z.", 27 = "aa.", 28 = "ab." */
internal fun subtaskLetter(place: Int): String {
    var rest = place
    val letters = StringBuilder()
    while (rest > 0) {
        rest--
        letters.insert(0, 'a' + rest % 26)
        rest /= 26
    }
    return "$letters."
}
