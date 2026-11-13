package com.accountabilibuddy.shared.tasks

/**
 * The task list as a device holds it (ADR 76; the PC's TaskLists): every task with its stamps, the labels, the saved views, History
 * and the account's Tasks settings, tombstones included.
 */
data class TaskLists(
    val tasks: List<SyncTaskItem>,
    val labels: List<SyncTaskLabel>,
    val views: List<SyncTaskView>,
    val log: List<SyncTaskLogItem>,
    val account: SyncTaskAccountSettings? = null,
) {
    companion object {
        val EMPTY = TaskLists(emptyList(), emptyList(), emptyList(), emptyList())

        /** The lists a migration made. */
        fun from(result: TaskMigrationResult): TaskLists = TaskLists(result.tasks, result.labels, result.views, result.history)
    }
}
