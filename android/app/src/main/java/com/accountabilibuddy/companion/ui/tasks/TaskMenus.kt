package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.ArrowDownward
import androidx.compose.material.icons.outlined.ArrowUpward
import androidx.compose.material.icons.outlined.CalendarToday
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material.icons.outlined.Edit
import androidx.compose.material.icons.outlined.Flag
import androidx.compose.material.icons.outlined.Restore
import androidx.compose.material.icons.outlined.Schedule

/**
 * The ⋯ menu of each kind of row, "Add to Today" first as on the PC, each entry with a Material icon (the same meaning has the
 * same icon on the PC: Add to Today = calendar, Later = clock, Come back to = flag). An entry the phone cannot do now is left
 * out rather than shown and refused.
 */
internal object TaskMenus {
    /**
     * @param pc The PC with a running session, or null.
     * @param moves Where the row can move in This session: whether it has a neighbour above and below to swap with (null: no
     * moving, e.g. while a chip or the search narrows the list).
     */
    fun of(row: TaskRowModel, actions: TasksPaneActions, pc: TasksPc?, moves: Pair<Boolean, Boolean>?): List<TaskMenuItem> {
        val items = mutableListOf<TaskMenuItem>()
        val canAddToPc = pc == null || pc.canEdit
        when (row.ref.kind) {
            TaskKind.Next -> {
                items += TaskMenuItem("Details", Icons.Outlined.Edit) { actions.onOpen(row) }
                items += TaskMenuItem("Mark done", Icons.Outlined.Check) { actions.onTick(row) }
                items += TaskMenuItem("Move to Later", Icons.Outlined.Schedule) { actions.onMoveToLater(row) }
                addMoves(items, row, actions, moves)
                items += TaskMenuItem("Delete", Icons.Outlined.Delete) { actions.onDelete(row) }
            }
            TaskKind.Running -> {
                items += TaskMenuItem("Details", Icons.Outlined.Edit) { actions.onOpen(row) }
                if (!row.sending) items += TaskMenuItem("Mark done", Icons.Outlined.Check) { actions.onTick(row) }
                if (pc?.canEdit == true) addMoves(items, row, actions, moves)
            }
            TaskKind.ComeBack -> if (!row.readOnly) {
                if (canAddToPc) items += TaskMenuItem("Add to Today", Icons.Outlined.CalendarToday) { actions.onAddToToday(row) }
                items += TaskMenuItem("Edit", Icons.Outlined.Edit) { actions.onEditComeback(row) }
                items += TaskMenuItem("Move to Later", Icons.Outlined.Schedule) { actions.onMoveToLater(row) }
                items += TaskMenuItem("Delete", Icons.Outlined.Delete) { actions.onDelete(row) }
            }
            TaskKind.Later -> {
                if (canAddToPc) items += TaskMenuItem("Add to Today", Icons.Outlined.CalendarToday) { actions.onAddToToday(row) }
                items += TaskMenuItem("Details", Icons.Outlined.Edit) { actions.onOpen(row) }
                items += TaskMenuItem("Move to Come back to", Icons.Outlined.Flag) { actions.onMoveToComeBack(row) }
                items += TaskMenuItem("Delete", Icons.Outlined.Delete) { actions.onDelete(row) }
            }
            TaskKind.DoneNext -> {
                items += TaskMenuItem("Put back", Icons.Outlined.Restore) { actions.onPutBack(row) }
                items += TaskMenuItem("Delete", Icons.Outlined.Delete) { actions.onDelete(row) }
            }
            TaskKind.DoneLater -> items += TaskMenuItem("Put back", Icons.Outlined.Restore) { actions.onPutBack(row) }
            TaskKind.DoneRunning -> Unit
        }
        return items
    }

    private fun addMoves(items: MutableList<TaskMenuItem>, row: TaskRowModel, actions: TasksPaneActions, moves: Pair<Boolean, Boolean>?) {
        if (moves == null) return
        if (moves.first) items += TaskMenuItem("Move up", Icons.Outlined.ArrowUpward) { actions.onMove(row, -1) }
        if (moves.second) items += TaskMenuItem("Move down", Icons.Outlined.ArrowDownward) { actions.onMove(row, 1) }
    }
}
