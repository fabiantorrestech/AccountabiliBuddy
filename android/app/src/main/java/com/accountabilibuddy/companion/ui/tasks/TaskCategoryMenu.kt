package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Add
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
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.sync.TaskCategoryChoices.Choice
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * A pill showing a task's category (a small coloured dot and the name, or "No category") that opens the categories to pick
 * from: this phone's, the other devices', and any seen on tasks, come-backs and Later items. A receiving PC creates one it lacks with the
 * name and colour the task carries (a category's id is local to a device, ADR 48).
 *
 * @param onCreate Makes a category of the name given ("New category…"); no such entry when null.
 */
@Composable
fun TaskCategoryMenu(
    choices: List<Choice>,
    selectedName: String?,
    onSelect: (Choice?) -> Unit,
    modifier: Modifier = Modifier,
    onCreate: ((String) -> Choice)? = null,
) {
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
        CategoryDropdown(open, { open = false }, choices, onSelect, onCreate)
    }
}

/**
 * The add field's dot: which category new tasks go in, the same choices as [TaskCategoryMenu]. Empty for none.
 */
@Composable
fun TaskCategoryDotMenu(choices: List<Choice>, selectedName: String?, onSelect: (Choice?) -> Unit, onCreate: ((String) -> Choice)?) {
    var open by remember { mutableStateOf(false) }
    val colors = Ab.colors
    val selected = choices.firstOrNull { it.name.equals(selectedName, ignoreCase = true) }
    Box {
        Box(
            Modifier.size(44.dp).clickable { open = true }.semantics {
                contentDescription = if (selected != null) "New tasks go in ${selected.name}" else "New tasks have no category"
            },
            contentAlignment = Alignment.Center,
        ) {
            val dot = Modifier.size(14.dp).border(1.dp, colors.faint, CircleShape)
            val fill = parseCategoryColor(selected?.color)
            Box(if (fill != null) dot.background(fill, CircleShape) else dot)
        }
        CategoryDropdown(open, { open = false }, choices, onSelect, onCreate)
    }
}

@Composable
private fun CategoryDropdown(
    open: Boolean,
    onClose: () -> Unit,
    choices: List<Choice>,
    onSelect: (Choice?) -> Unit,
    onCreate: ((String) -> Choice)?,
) {
    var naming by remember { mutableStateOf(false) }
    val colors = Ab.colors
    DropdownMenu(expanded = open, onDismissRequest = onClose, containerColor = colors.surface2, shape = Ab.shapes.mediumShape) {
        DropdownMenuItem(
            text = { Text("No category", style = Ab.type.bodyMedium, color = colors.ink) },
            colors = MenuDefaults.itemColors(textColor = colors.ink),
            onClick = {
                onClose()
                onSelect(null)
            },
        )
        choices.forEach { choice ->
            DropdownMenuItem(
                text = { Text(choice.name, style = Ab.type.bodyMedium, color = colors.ink) },
                leadingIcon = { CategoryDot(choice) },
                colors = MenuDefaults.itemColors(textColor = colors.ink),
                onClick = {
                    onClose()
                    onSelect(choice)
                },
            )
        }
        if (onCreate != null) {
            DropdownMenuItem(
                text = { Text("New category…", style = Ab.type.bodyMedium, color = colors.ink) },
                leadingIcon = { Icon(Icons.Outlined.Add, contentDescription = null, tint = colors.muted, modifier = Modifier.size(18.dp)) },
                colors = MenuDefaults.itemColors(textColor = colors.ink),
                onClick = {
                    onClose()
                    naming = true
                },
            )
        }
    }
    if (naming && onCreate != null) {
        var name by remember { mutableStateOf("") }
        AbDialog(
            title = "New category", onDismiss = { naming = false }, confirmText = "Add",
            onConfirm = {
                naming = false
                if (name.isNotBlank()) onSelect(onCreate(name))
            },
        ) {
            SheetField(value = name, onValueChange = { name = it }, placeholder = "Category name", singleLine = true, onDone = {})
        }
    }
}

@Composable
private fun CategoryDot(choice: Choice) {
    Box(Modifier.size(8.dp).background(parseCategoryColor(choice.color) ?: Ab.colors.faint, CircleShape))
}
