package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.sync.TaskFilterPreset
import com.accountabilibuddy.companion.ui.kit.AbQuietIconButton
import com.accountabilibuddy.companion.ui.kit.AbSheet
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The Tasks tab's saved filters: keep the chip and the search words now in force under a name, use one, or delete one. They stay on this
 * phone. The phone has no status, due or sort filters, so that is all a saved filter here holds.
 */
@Composable
fun SavedFiltersSheet(
    presets: List<TaskFilterPreset>,
    category: String?,
    search: String,
    onSave: (String) -> Unit,
    onApply: (TaskFilterPreset) -> Unit,
    onDelete: (TaskFilterPreset) -> Unit,
    onDismiss: () -> Unit,
    categoryShown: (String?) -> Boolean = { true },
) {
    var name by remember { mutableStateOf("") }
    val colors = Ab.colors
    AbSheet(onDismiss = onDismiss) {
        Text("Saved filters", style = Ab.type.bodyMedium, color = colors.ink, modifier = Modifier.padding(horizontal = 16.dp))
        val current = listOfNotNull(category, search.trim().takeIf { it.isNotEmpty() }?.let { "“$it”" }).joinToString(" · ").ifEmpty { "All tasks" }
        Text("Now showing: $current", style = Ab.type.small, color = colors.muted, modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp))
        SheetField(
            value = name, onValueChange = { name = it }, placeholder = "Name, to save this filter", singleLine = true,
            onDone = {
                if (name.isNotBlank()) {
                    onSave(name)
                    name = ""
                }
            },
            modifier = Modifier.padding(16.dp),
        )
        if (presets.isEmpty()) {
            Text("Nothing saved yet.", style = Ab.type.body, color = colors.muted, modifier = Modifier.padding(16.dp))
        }
        LazyColumn(Modifier.heightIn(max = 340.dp)) {
            items(presets, key = { it.id }) { preset ->
                Row(Modifier.fillMaxWidth().padding(end = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                    Column(
                        Modifier.weight(1f).heightIn(min = 48.dp).clickable {
                            onApply(preset)
                            onDismiss()
                        }.padding(horizontal = 16.dp, vertical = 8.dp),
                    ) {
                        Text(preset.name, style = Ab.type.body, color = colors.ink)
                        val detail = listOfNotNull(preset.category?.takeIf { categoryShown(it) }, preset.search.takeIf { it.isNotEmpty() }?.let { "“$it”" }).joinToString(" · ")
                        if (detail.isNotEmpty()) Text(detail, style = Ab.type.small, color = colors.muted)
                    }
                    AbQuietIconButton(Icons.Outlined.Close, "Delete saved filter ${preset.name}", onClick = { onDelete(preset) })
                }
            }
        }
    }
}
