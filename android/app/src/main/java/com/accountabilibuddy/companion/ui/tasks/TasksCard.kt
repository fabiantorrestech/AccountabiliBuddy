package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * "This session" while a PC runs one (ADR 24, 48): its checklist in the PC's order, numbered, the task the PC shows now
 * highlighted. The checklist belongs to the PC: a tick, an added task, an edit or a new order goes to it as a sync action and it
 * applies them under its own rules (three tasks, a 4th is parked), so a change shows here after the PC's next file. Nothing
 * here ends a session.
 *
 * @param total How many open tasks the checklist holds before a chip or the search narrows [rows] (for the empty text).
 * @param filtering A chip or the search is on.
 */
@Composable
fun TasksCard(rows: List<TaskRowModel>, total: Int, pc: TasksPc, filtering: Boolean, actions: TasksPaneActions) {
    AbCard(contentPadding = PaddingValues(horizontal = 10.dp, vertical = 14.dp), verticalSpacing = 4.dp) {
        AbSectionLabel("This session · ${pc.name}", modifier = Modifier.padding(horizontal = 10.dp))
        when {
            rows.isNotEmpty() -> TaskRows(rows, actions, pc, reorder = !filtering)
            total == 0 -> EmptyLine("No open tasks in this session.")
            else -> EmptyLine("Nothing matches.")
        }
    }
}

/** A muted line in a card with nothing to list ("Nothing parked."). */
@Composable
internal fun EmptyLine(text: String) {
    Text(text, style = Ab.type.bodyMedium, color = Ab.colors.muted, modifier = Modifier.padding(horizontal = 10.dp, vertical = 8.dp))
}
