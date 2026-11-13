package com.accountabilibuddy.shared.tasks

/**
 * What the task migration (ADR 76) made: the new lists in their sync form, the view searches that stay local, and the counts (the
 * PC's TaskMigrationResult).
 *
 * @property tasks Every task, tombstones included.
 * @property views The built-in views and the old saved filters.
 * @property history History entries (finished).
 * @property viewSearches An old saved filter's search words by view id: device-local, never synced.
 */
data class TaskMigrationResult(
    val tasks: List<SyncTaskItem>,
    val labels: List<SyncTaskLabel>,
    val views: List<SyncTaskView>,
    val history: List<SyncTaskLogItem>,
    val viewSearches: Map<String, String>,
    val counts: TaskMigrationCounts,
)
