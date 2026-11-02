package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.ExpandMore
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.MenuDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.sync.TaskCategoryChoices.Choice
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * A pill showing a task's category (a small coloured dot and the name, or "No category") that opens the categories to pick
 * from. The categories are the ones the phone has seen on tasks, come-backs and Later items; a receiving PC creates one it lacks
 * with the name and colour the task carries (a category's id is local to a device, ADR 48).
 */
@Composable
fun TaskCategoryMenu(choices: List<Choice>, selectedName: String?, onSelect: (Choice?) -> Unit, modifier: Modifier = Modifier) {
    var open by remember { mutableStateOf(false) }
    val colors = Ab.colors
    val selected = choices.firstOrNull { it.name.equals(selectedName, ignoreCase = true) }
    Box(modifier) {
        Surface(onClick = { open = true }, shape = Ab.shapes.pill, color = colors.surface2, contentColor = colors.ink) {
            Row(
                Modifier.heightIn(min = 44.dp).padding(start = 14.dp, end = 10.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                if (selected != null) CategoryDot(selected)
                Text(selectedName ?: "No category", style = Ab.type.small, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f, fill = false))
                Icon(Icons.Outlined.ExpandMore, contentDescription = null, tint = colors.muted, modifier = Modifier.size(20.dp))
            }
        }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }, containerColor = colors.surface2, shape = Ab.shapes.mediumShape) {
            DropdownMenuItem(
                text = { Text("No category", style = Ab.type.bodyMedium, color = colors.ink) },
                colors = MenuDefaults.itemColors(textColor = colors.ink),
                onClick = { open = false; onSelect(null) },
            )
            choices.forEach { choice ->
                DropdownMenuItem(
                    text = { Text(choice.name, style = Ab.type.bodyMedium, color = colors.ink) },
                    leadingIcon = { CategoryDot(choice) },
                    colors = MenuDefaults.itemColors(textColor = colors.ink),
                    onClick = { open = false; onSelect(choice) },
                )
            }
        }
    }
}

@Composable
private fun CategoryDot(choice: Choice) {
    Box(Modifier.size(8.dp).background(parseCategoryColor(choice.color) ?: Ab.colors.faint, CircleShape))
}
