package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.foundation.layout.Column
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier

/**
 * The rows of one group, each with its menu. A row with a number (This session) is opened by a tap and ticked from its menu or
 * its sheet; the others tick from the box at their start. [reorder] is true when the rows are in their true order (no chip and
 * no search narrows them), which is when Move up and Move down make sense: a row moves only past a neighbour of its own category.
 */
@Composable
internal fun TaskRows(rows: List<TaskRowModel>, actions: TasksPaneActions, pc: TasksPc?, reorder: Boolean, modifier: Modifier = Modifier) {
    Column(modifier) {
        rows.forEachIndexed { index, row ->
            val moves = if (reorder && row.number != null) {
                (index > 0 && rows[index - 1].category.equals(row.category, ignoreCase = true)) to
                    (index < rows.lastIndex && rows[index + 1].category.equals(row.category, ignoreCase = true))
            } else {
                null
            }
            TaskRow(
                row = row,
                onOpen = { actions.onOpen(row) },
                onTick = if (row.number == null && row.ref.kind != TaskKind.DoneRunning && !row.sending && !(row.ref.kind == TaskKind.Later && row.done)) ({ actions.onTick(row) }) else null,
                menu = TaskMenus.of(row, actions, pc, moves),
            )
        }
    }
}
