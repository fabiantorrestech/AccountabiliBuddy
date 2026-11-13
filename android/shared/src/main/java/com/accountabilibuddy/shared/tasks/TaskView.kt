package com.accountabilibuddy.shared.tasks

import kotlinx.serialization.json.JsonObject
import java.time.OffsetDateTime

/**
 * A saved view of the task list (ADR 76; the PC's TaskView), shared between the user's devices. Never carries search words. Three
 * built-in views have fixed ids.
 *
 * @property icon A Material icon name; null for the default bookmark.
 * @property config What it shows (the sync file's `cfg`).
 */
data class TaskView(
    val id: String,
    val name: String,
    val icon: String?,
    val config: TaskViewConfig,
    val position: Double,
    val editedAt: OffsetDateTime,
    val builtIn: Boolean = false,
    val deletedAt: OffsetDateTime? = null,
    val extra: JsonObject = TaskUnknown.NONE,
) {
    val isLive: Boolean get() = deletedAt == null

    companion object {
        const val DEFAULT_ID = "0e4f6a52-1b7d-4c39-9f0e-7a2d5c8b1e01"
        const val QUICK_WINS_ID = "0e4f6a52-1b7d-4c39-9f0e-7a2d5c8b1e02"
        const val DEEP_WORK_ID = "0e4f6a52-1b7d-4c39-9f0e-7a2d5c8b1e03"

        /** The three built-in views, stamped [editedAt]: Default (everything), Quick wins (25 min or less), Deep work (50 min or more). */
        fun builtIns(editedAt: OffsetDateTime, defaultConfig: TaskViewConfig? = null): List<TaskView> = listOf(
            TaskView(DEFAULT_ID, "Default", "star", defaultConfig ?: TaskViewConfig.EVERYTHING, 1.0, editedAt, builtIn = true),
            TaskView(QUICK_WINS_ID, "Quick wins", "lightning-bolt", TaskViewConfig(TaskViewFilter(time = listOf("le25"))), 2.0, editedAt, builtIn = true),
            TaskView(DEEP_WORK_ID, "Deep work", "head-cog", TaskViewConfig(TaskViewFilter(time = listOf("ge50"))), 3.0, editedAt, builtIn = true),
        )
    }
}
