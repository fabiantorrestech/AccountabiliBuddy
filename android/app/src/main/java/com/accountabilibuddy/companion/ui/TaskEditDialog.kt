package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.outlined.ArrowDownward
import androidx.compose.material.icons.outlined.ArrowUpward
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.sync.TaskCategoryChoices.Choice
import com.accountabilibuddy.shared.sync.SessionTaskItem

/**
 * Edits one task: its title, its category (when [categories] is not null) and its steps, lettered a., b., … as on the PC,
 * which can be reworded, moved up or down, deleted, or added to. Save gives back the title, the chosen category (null:
 * none) and the steps in order (a new step has no id; a blank step is for the caller to drop).
 */
@Composable
fun TaskEditDialog(
    title: String,
    steps: List<SessionTaskItem>,
    categories: List<Choice>?,
    categoryName: String?,
    onSave: (title: String, category: Choice?, steps: List<SessionTaskItem>) -> Unit,
    onDismiss: () -> Unit,
) {
    var newTitle by remember { mutableStateOf(title) }
    var chosen by remember { mutableStateOf(categories?.firstOrNull { it.name == categoryName }) }
    var edited by remember { mutableStateOf(steps) }
    var newStep by remember { mutableStateOf("") }
    // A step typed in the "Add a step" box and not yet added still goes in on Save.
    fun withTypedStep(): List<SessionTaskItem> =
        if (newStep.isBlank()) edited else edited + SessionTaskItem(null, newStep.trim(), null, emptyList())

    fun move(index: Int, by: Int) {
        val target = index + by
        if (target in edited.indices) {
            edited = edited.toMutableList().apply { add(target, removeAt(index)) }
        }
    }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Edit task") },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(value = newTitle, onValueChange = { newTitle = it }, label = { Text("Task") }, modifier = Modifier.fillMaxWidth())
                if (categories != null) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("Category", style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        TaskCategoryMenu(categories, chosen?.name, onSelect = { chosen = it })
                    }
                }
                if (edited.isNotEmpty()) {
                    Text("Steps", style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                // Only the open steps are lettered (a done step keeps no letter), as on the PC.
                var place = 0
                edited.forEachIndexed { index, step ->
                    val letter = if (step.isDone) "" else subtaskLetter(++place)
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(letter, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.width(28.dp))
                        OutlinedTextField(
                            value = step.title, singleLine = true, modifier = Modifier.weight(1f),
                            onValueChange = { text -> edited = edited.mapIndexed { i, old -> if (i == index) old.copy(title = text) else old } },
                        )
                        SmallIconButton(Icons.Outlined.ArrowUpward, "Move step up", enabled = index > 0) { move(index, -1) }
                        SmallIconButton(Icons.Outlined.ArrowDownward, "Move step down", enabled = index < edited.lastIndex) { move(index, 1) }
                        SmallIconButton(Icons.Outlined.Delete, "Delete step") { edited = edited.filterIndexed { i, _ -> i != index } }
                    }
                }
                Row(verticalAlignment = Alignment.CenterVertically) {
                    OutlinedTextField(
                        value = newStep, onValueChange = { newStep = it }, singleLine = true, modifier = Modifier.weight(1f),
                        placeholder = { Text("Add a step") },
                    )
                    SmallIconButton(Icons.Outlined.Add, "Add step", enabled = newStep.isNotBlank()) {
                        edited = edited + SessionTaskItem(null, newStep.trim(), null, emptyList())
                        newStep = ""
                    }
                }
            }
        },
        confirmButton = { TextButton(onClick = { onSave(newTitle, chosen, withTypedStep()) }, enabled = newTitle.isNotBlank()) { Text("Save") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
    )
}

/** A 36 dp icon button, for rows that hold a text field and several of them. */
@Composable
fun SmallIconButton(icon: androidx.compose.ui.graphics.vector.ImageVector, description: String, enabled: Boolean = true, onClick: () -> Unit) {
    IconButton(onClick = onClick, enabled = enabled, modifier = Modifier.size(36.dp)) {
        Icon(icon, contentDescription = description, modifier = Modifier.size(20.dp))
    }
}
