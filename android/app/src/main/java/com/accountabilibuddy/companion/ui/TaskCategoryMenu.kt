package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
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

/** A button showing a task's category (colour dot and name, or "No category") that opens the categories to pick from. */
@Composable
fun TaskCategoryMenu(choices: List<Choice>, selectedName: String?, onSelect: (Choice?) -> Unit, modifier: Modifier = Modifier) {
    var open by remember { mutableStateOf(false) }
    val selected = choices.firstOrNull { it.name == selectedName }
    Box(modifier) {
        TextButton(onClick = { open = true }) {
            if (selected != null) {
                CategoryDot(selected)
                Spacer(Modifier.width(6.dp))
            }
            Text(selectedName ?: "No category", maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            DropdownMenuItem(text = { Text("No category") }, onClick = { open = false; onSelect(null) })
            choices.forEach { choice ->
                DropdownMenuItem(
                    text = { Row(verticalAlignment = Alignment.CenterVertically) { CategoryDot(choice); Spacer(Modifier.width(8.dp)); Text(choice.name) } },
                    onClick = { open = false; onSelect(choice) },
                )
            }
        }
    }
}

@Composable
private fun CategoryDot(choice: Choice) {
    Box(Modifier.size(8.dp).background(parseCategoryColor(choice.color) ?: MaterialTheme.colorScheme.secondary, CircleShape))
}
