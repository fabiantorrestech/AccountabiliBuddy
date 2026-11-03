package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbSheet
import com.accountabilibuddy.companion.ui.theme.Ab

/** The tasks that match [search]: the title, the line under it or the category. */
fun matchingTasks(rows: List<TaskRowModel>, search: String): List<TaskRowModel> =
    rows.filter { row ->
        row.title.contains(search, ignoreCase = true) || row.line.contains(search, ignoreCase = true) ||
            row.category?.contains(search, ignoreCase = true) == true
    }

/** A task to choose: its text and the line under it. */
@Composable
fun TaskPickRow(row: TaskRowModel, onClick: () -> Unit) {
    Column(Modifier.fillMaxWidth().heightIn(min = 48.dp).clickable(onClick = onClick).padding(horizontal = 16.dp, vertical = 8.dp)) {
        Text(row.title, style = Ab.type.body, color = Ab.colors.ink)
        if (row.line.isNotEmpty()) Text(row.line, style = Ab.type.small, color = Ab.colors.muted)
    }
}

/**
 * "From your lists" on the phone: its Come back to and Later items, with a search. Choosing one adds it the way "Add to Today" does.
 * Finished tasks, Unfinished and the Archive are kept in the PC's records, so they aren't listed here.
 */
@Composable
fun TaskPickerSheet(rows: List<TaskRowModel>, onPick: (TaskRowModel) -> Unit, onDismiss: () -> Unit) {
    var search by remember { mutableStateOf("") }
    val shown = if (search.isBlank()) rows else matchingTasks(rows, search.trim())
    AbSheet(onDismiss = onDismiss) {
        Text("From your lists", style = Ab.type.bodyMedium, color = Ab.colors.ink, modifier = Modifier.padding(horizontal = 16.dp))
        OutlinedTextField(
            value = search, onValueChange = { search = it }, singleLine = true,
            label = { Text("Search your lists") },
            modifier = Modifier.fillMaxWidth().padding(16.dp),
        )
        if (shown.isEmpty()) {
            Text(
                if (search.isBlank()) "Nothing in your lists." else "Nothing matches.",
                style = Ab.type.body, color = Ab.colors.muted, modifier = Modifier.padding(16.dp),
            )
        }
        LazyColumn(Modifier.heightIn(max = 420.dp)) {
            items(shown, key = { "${it.ref.kind}:${it.ref.id}" }) { row ->
                TaskPickRow(row, onClick = {
                    onPick(row)
                    onDismiss()
                })
            }
        }
    }
}
