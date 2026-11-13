package com.accountabilibuddy.shared.tasks

import kotlinx.serialization.json.JsonObject

/**
 * A view's "Show only" part (ADR 76; the PC's TaskViewFilter). Values are plain strings so a newer app's value survives.
 *
 * @property due "overdue", "today", "tomorrow", "thisWeek", "none", or "range:yyyy-MM-dd..yyyy-MM-dd".
 * @property time Time limit buckets: "le10", "le25", "le50", "ge50", "none".
 * @property old Untouched for: "1w", "2w", "4w", "8w".
 */
data class TaskViewFilter(
    val due: List<String>? = null,
    val time: List<String>? = null,
    val old: List<String>? = null,
    val highlightedOnly: Boolean = false,
    val extra: JsonObject = TaskUnknown.NONE,
) {
    companion object {
        val NONE = TaskViewFilter()
    }
}
