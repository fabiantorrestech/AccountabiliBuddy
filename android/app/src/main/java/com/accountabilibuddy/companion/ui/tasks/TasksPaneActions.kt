package com.accountabilibuddy.companion.ui.tasks

/**
 * What a row of the Tasks pane can ask for; the host wires each to [TasksController]. The pane draws and forwards taps and
 * decides nothing itself.
 */
class TasksPaneActions(
    val onOpen: (TaskRowModel) -> Unit,
    val onTick: (TaskRowModel) -> Unit,
    val onAddToToday: (TaskRowModel) -> Unit,
    val onMoveToLater: (TaskRowModel) -> Unit,
    val onMoveToComeBack: (TaskRowModel) -> Unit,
    val onMove: (TaskRowModel, Int) -> Unit,
    val onEditComeback: (TaskRowModel) -> Unit,
    val onPutBack: (TaskRowModel) -> Unit,
    val onDelete: (TaskRowModel) -> Unit,
    /** False next to a PC older than the shared lists: a Later item can't be moved to Come back to there (that list never reaches it). */
    val canMoveToComeBack: Boolean = true,
)
