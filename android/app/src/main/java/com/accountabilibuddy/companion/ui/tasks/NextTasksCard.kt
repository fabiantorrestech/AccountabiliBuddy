package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel

/**
 * "This session" with no session running (ADR 24, 48): the next session's tasks, the same list on every device, numbered 1., 2.,
 * 3. A tap opens a task's details; its ⋯ menu ticks, moves and deletes it. A 4th task keeps the "Keep it to 3" prompt (the pane's
 * add field asks). Changes are in this phone's copy at once and reach the other devices with the next sync, where the newest edit
 * of each task wins.
 *
 * @param total How many open tasks the list holds before a chip or the search narrows [rows] (for the empty text).
 * @param filtering A chip or the search is on.
 */
@Composable
fun NextTasksCard(rows: List<TaskRowModel>, total: Int, filtering: Boolean, actions: TasksPaneActions) {
    AbCard(contentPadding = PaddingValues(horizontal = 10.dp, vertical = 14.dp), verticalSpacing = 4.dp) {
        AbSectionLabel("This session", modifier = Modifier.padding(horizontal = 10.dp))
        when {
            rows.isNotEmpty() -> TaskRows(rows, actions, pc = null, reorder = !filtering)
            total == 0 -> EmptyLine("No tasks yet. Add one below.")
            else -> EmptyLine("Nothing matches.")
        }
    }
}
