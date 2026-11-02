package com.accountabilibuddy.companion.ui.tasks

import com.accountabilibuddy.companion.sync.TaskFields

/**
 * What the detail sheet is opened on: one task as it is now, and what the phone can do with it.
 *
 * @property label The list it is on ("This session", "Work PC session", "Later").
 * @property fields The task's fields now. The sheet edits a copy and hands back only what the user changed.
 * @property editsDetails The estimate, note and category can be changed (a running task's, which the PC owns, has no such fields here).
 * @property hasDueDate A Later item: it has a due date.
 * @property sendingSteps The ids of steps the user ticked that the PC has not confirmed yet (a running task).
 * @property pcNote A muted line saying what changes on the PC instead; null for none.
 */
class TaskDetail(
    val ref: TaskRef,
    val label: String,
    val fields: TaskFields,
    val isDone: Boolean,
    val editsDetails: Boolean,
    val hasDueDate: Boolean,
    val sendingSteps: Set<String> = emptySet(),
    val pcNote: String? = null,
)
