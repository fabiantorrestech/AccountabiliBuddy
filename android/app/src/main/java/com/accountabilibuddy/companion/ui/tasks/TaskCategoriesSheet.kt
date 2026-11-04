package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.sync.TaskCategoryChoices.Choice
import com.accountabilibuddy.companion.ui.kit.AbQuietIconButton
import com.accountabilibuddy.companion.ui.kit.AbSheet
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The Tasks tab's categories: add, rename, recolour and delete. A rename or delete relabels this phone's lists on every device, but a
 * PC keeps its own list of categories, so the question before a delete says so. No lock applies, as on the PC: naming a group of tasks
 * loosens nothing.
 */
@Composable
fun TaskCategoriesSheet(
    categories: List<Choice>,
    controller: TasksController,
    onDismiss: () -> Unit,
) {
    var coloring by remember { mutableStateOf<Choice?>(null) }
    var deleting by remember { mutableStateOf<Choice?>(null) }
    val newName = remember { mutableStateOf("") }
    val colors = Ab.colors

    coloring?.let { choice ->
        CategoryColorSheet(
            choice.name, choice.color,
            onDone = { color ->
                controller.recolorCategory(choice.name, color)
                coloring = null
            },
            onDismiss = { coloring = null },
        )
    }

    AbSheet(onDismiss = onDismiss) {
        Text("Categories", style = Ab.type.bodyMedium, color = colors.ink, modifier = Modifier.padding(horizontal = 16.dp))
        if (categories.isEmpty()) {
            Text("No categories yet. Add one below.", style = Ab.type.body, color = colors.muted, modifier = Modifier.padding(16.dp))
        }
        LazyColumn(Modifier.heightIn(max = 380.dp)) {
            items(categories, key = { it.name.lowercase() }) { choice ->
                Column {
                    CategoryRow(
                        choice,
                        onColor = { coloring = choice },
                        onRename = { name -> controller.renameCategory(choice, name) },
                        onDelete = { deleting = if (deleting == choice) null else choice },
                    )
                    if (deleting == choice) {
                        val count = controller.categoryUseCount(choice.name)
                        Column(Modifier.padding(horizontal = 16.dp, vertical = 4.dp)) {
                            Text(
                                "Delete ${choice.name}? " + when (count) {
                                    0 -> "Nothing here uses it."
                                    1 -> "1 item loses this category, on all your devices."
                                    else -> "$count items lose this category, on all your devices."
                                } + " A PC keeps its own list and still offers the name.",
                                style = Ab.type.small, color = colors.muted,
                            )
                            Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                                TextButton(onClick = {
                                    controller.deleteCategory(choice.name)
                                    deleting = null
                                }) { Text("Delete", color = colors.ink) }
                                TextButton(onClick = { deleting = null }) { Text("Keep it", color = colors.muted) }
                            }
                        }
                    }
                }
            }
        }
        SheetField(
            value = newName.value, onValueChange = { newName.value = it }, placeholder = "New category, e.g. Work",
            singleLine = true,
            onDone = {
                if (newName.value.isNotBlank()) {
                    controller.createCategory(newName.value)
                    newName.value = ""
                }
            },
            modifier = Modifier.padding(16.dp),
        )
    }
}

@Composable
private fun CategoryRow(choice: Choice, onColor: () -> Unit, onRename: (String) -> Unit, onDelete: () -> Unit) {
    var name by remember(choice.name) { mutableStateOf(choice.name) }
    Row(
        Modifier.fillMaxWidth().padding(horizontal = 8.dp, vertical = 2.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        Box(
            Modifier.size(44.dp).clickable(onClick = onColor).semantics { contentDescription = "Colour of ${choice.name}" },
            contentAlignment = Alignment.Center,
        ) {
            Box(Modifier.size(22.dp).background(parseCategoryColor(choice.color) ?: Ab.colors.faint, CircleShape))
        }
        SheetField(
            value = name, onValueChange = { name = it }, placeholder = "Category name", singleLine = true,
            // A blank name keeps the old one.
            onDone = { if (name.isNotBlank() && name.trim() != choice.name) onRename(name.trim()) else name = choice.name },
            modifier = Modifier.weight(1f),
        )
        AbQuietIconButton(Icons.Outlined.Close, "Delete ${choice.name}", onClick = onDelete)
    }
}
