package com.accountabilibuddy.shared.tasks

import kotlinx.serialization.json.JsonObject

/**
 * What a saved view shows and how (ADR 76; the PC's TaskViewConfig): its filters, grouping, sort and density, as plain strings so
 * a newer app's value survives in an older one.
 *
 * @property labels Only tasks with one of these labels (ids); null for any. [noLabel]: tasks without a label match too.
 * @property match "any" or "all" for [tags].
 * @property group "bins", "label", "tag" or "none".
 * @property bins Only these bins; null for all.
 * @property seed The order Shuffle gave; null before the first shuffle.
 */
data class TaskViewConfig(
    val only: TaskViewFilter,
    val labels: List<String>? = null,
    val noLabel: Boolean = false,
    val tags: List<String>? = null,
    val match: String = MATCH_ANY,
    val group: String = GROUP_BINS,
    val sort: String = SORT_DEFAULT,
    val pinHighlights: Boolean = false,
    val density: String = DENSITY_NORMAL,
    val bins: List<TaskBin>? = null,
    val seed: Int? = null,
    val extra: JsonObject = TaskUnknown.NONE,
) {
    companion object {
        const val MATCH_ANY = "any"
        const val MATCH_ALL = "all"
        const val GROUP_BINS = "bins"
        const val GROUP_LABEL = "label"
        const val GROUP_TAG = "tag"
        const val GROUP_NONE = "none"
        const val SORT_DEFAULT = "default"
        const val DENSITY_NORMAL = "normal"
        const val DENSITY_COMPACT = "compact"

        /** Everything, grouped by bins, default sort. */
        val EVERYTHING = TaskViewConfig(TaskViewFilter.NONE)
    }
}
