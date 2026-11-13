package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.tasks.SheetField
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * "Add a task" in R7.3a's short form: the title "Add a task", the subtitle naming where it goes ([place]: "Shelf · Next", with the
 * page's label or tag when it has one), the field and **Add**. It starts on the page you are looking at. The board's full sheet
 * (Tasks-Phone-Add: @label / #tag chips, the Send to grid, Add another, the session cap's swap) replaces this in R7.3b.
 *
 * @param onAdd Adds the task; true when it was added (the sheet closes).
 */
@Composable
fun QuickAddSheet(place: String, onAdd: (String) -> Boolean, onDismiss: () -> Unit) {
    var title by remember { mutableStateOf("") }
    val focus = remember { FocusRequester() }
    val add = { if (title.isNotBlank() && onAdd(title)) onDismiss() }
    LaunchedEffect(Unit) { runCatching { focus.requestFocus() } }
    AbSheet(
        title = "Add a task",
        subtitle = place,
        onDismiss = onDismiss,
        height = AbSheetHeight.Half,
        footer = { AbPrimaryButton("Add", onClick = add, enabled = title.isNotBlank(), modifier = Modifier.fillMaxWidth()) },
    ) {
        Column(Modifier.padding(horizontal = 24.dp, vertical = 8.dp)) {
            SheetField(value = title, onValueChange = { title = it }, placeholder = "What needs doing?", onDone = add, modifier = Modifier.focusRequester(focus))
            Text("Labels, tags, the time limit and reminders are in the task's ⋮ menu.", style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.padding(top = 10.dp))
        }
    }
}
