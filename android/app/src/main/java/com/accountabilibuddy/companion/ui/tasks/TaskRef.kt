package com.accountabilibuddy.companion.ui.tasks

/** One row of the Tasks pane: its list and its id there (a running task's id is the PC's). */
data class TaskRef(val kind: TaskKind, val id: String)
