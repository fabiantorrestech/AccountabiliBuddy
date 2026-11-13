package com.accountabilibuddy.companion.tasks

import com.accountabilibuddy.shared.tasks.TaskPageOrder
import com.accountabilibuddy.shared.tasks.TaskViewConfig

/**
 * What the phone's Tasks page remembers on this device (round 2 R7.3; never synced, plan §3b item 3): the view in use (its settings
 * as they are now, and the saved view they came from), and per grouping ("bins", "label", "tag") the page order, the start page and
 * wrap around (off unless turned on).
 *
 * @property config The view's settings in use: grouping, sort, rows, filters.
 * @property viewId The saved view [config] started from; null for the default view.
 * @property hideShelfInSession The Shelf hides behind "Show the Shelf" while a session runs (Settings › Tasks › During a session).
 */
data class TasksUiState(
    val config: TaskViewConfig = TaskViewConfig.EVERYTHING.copy(pinHighlights = true),
    val viewId: String? = null,
    val orders: Map<String, List<String>> = emptyMap(),
    val starts: Map<String, String> = emptyMap(),
    val wraps: Map<String, Boolean> = emptyMap(),
    val hideShelfInSession: Boolean = true,
) {
    /** The grouping the pages are stored under now; null for Group by None. */
    val grouping: String? get() = TaskPageOrder.groupingOf(config.group)

    fun orderOf(grouping: String?): List<String>? = grouping?.let { orders[it] }

    fun startOf(grouping: String?): String? = grouping?.let { starts[it] }

    fun wrapOf(grouping: String?): Boolean = grouping?.let { wraps[it] } ?: false
}
