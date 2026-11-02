package com.accountabilibuddy.companion.ui.tasks

/**
 * The buttons of the detail sheet. An action that is null is not offered for this task (for example "Move to Later" for a
 * running task, which only the PC can do); each runs and the sheet closes after it.
 */
class TaskDetailActions(
    val onMarkDone: (() -> Unit)? = null,
    val onMoveToLater: (() -> Unit)? = null,
    val onAddToToday: (() -> Unit)? = null,
    val onDelete: (() -> Unit)? = null,
    /** Ticks a step of a running task on the PC (the other lists tick steps in the sheet's own copy). */
    val onTickRunningStep: ((String) -> Unit)? = null,
)
