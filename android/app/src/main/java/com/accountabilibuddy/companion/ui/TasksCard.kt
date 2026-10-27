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

                group.tasks.forEach { task -> TaskLine(task, indent = false, markedId = running.markedId, ticking, onTick, number = numbers[task.id]) }
            }
            if (done.isNotEmpty()) {
                TextButton(onClick = { showDone = !showDone }) { Text(if (showDone) "Hide done (${done.size})" else "Done (${done.size})") }
                if (showDone) {
                    done.forEach { task -> TaskLine(task, indent = false, markedId = null, ticking, null) }
                }
            }
        }
    }
}

// [markedId]: the task or subtask the PC's bar shows, marked with a pin.
// [onTick]: ticks an open line on the PC (null: read-only); a line ticked here shows done until the PC catches up.
@Composable
private fun TaskLine(task: SessionTaskItem, indent: Boolean, markedId: String?, ticking: (String?) -> Boolean, onTick: ((String) -> Unit)?, number: Int? = null, letter: String? = null) {
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
        )
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
private fun parseCategoryColor(hex: String?): Color? {
    if (hex == null || hex.length != 7 || hex[0] != '#') return null
    val rgb = hex.substring(1).toLongOrNull(16) ?: return null
    return Color(0xFF000000 or rgb)
}

/** 1 = "a.", 26 = "z.", 27 = "aa.", 28 = "ab." */
private fun subtaskLetter(place: Int): String {
    var rest = place
    val letters = StringBuilder()
    while (rest > 0) {
        rest--
        letters.insert(0, 'a' + rest % 26)
        rest /= 26
    }
    return "$letters."
}
