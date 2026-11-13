package com.accountabilibuddy.companion.ui.tasks.page

import com.accountabilibuddy.shared.tasks.TaskView
import com.accountabilibuddy.shared.tasks.TaskViewConfig
import com.accountabilibuddy.shared.tasks.TaskViewFilter
import com.accountabilibuddy.shared.tasks.TaskViewRules

/** The words the Options sheet uses for a view's settings, and the small rules of its footer (Clear, Save). */
object TaskViewWords {
    /** Group by's choices in the sheet's order: the stored value and its name. */
    val GROUPS: List<Pair<String, String>> = listOf(
        TaskViewConfig.GROUP_BINS to "Bins",
        TaskViewConfig.GROUP_LABEL to "Label",
        TaskViewConfig.GROUP_TAG to "Tag",
        TaskViewConfig.GROUP_NONE to "None · one list",
    )

    /** Sort's choices in the sheet's order (Shuffle last). */
    val SORTS: List<Pair<String, String>> = listOf(
        TaskViewConfig.SORT_DEFAULT to "Your order",
        TaskViewRules.SORT_DUE_ASCENDING to "Due date, soonest first",
        TaskViewRules.SORT_DUE_DESCENDING to "Due date, latest first",
        TaskViewRules.SORT_NEWEST to "Newest first",
        TaskViewRules.SORT_OLDEST to "Oldest first",
        TaskViewRules.SORT_UNTOUCHED to "Untouched the longest",
        TaskViewRules.SORT_LONGEST to "Longest time limit",
        TaskViewRules.SORT_SHORTEST to "Shortest time limit",
        TaskViewRules.SORT_SHUFFLE to "Shuffle",
    )

    val DUE: List<Pair<String, String>> = listOf("overdue" to "Overdue", "today" to "Today", "tomorrow" to "Tomorrow", "thisWeek" to "This week", "none" to "No due date")

    val TIME: List<Pair<String, String>> = listOf("le10" to "10 min or less", "le25" to "25 min or less", "le50" to "50 min or less", "ge50" to "50 min or more", "none" to "No time limit")

    val OLD: List<Pair<String, String>> = listOf("1w" to "1 week", "2w" to "2 weeks", "3w" to "3 weeks", "4w" to "4 weeks")

    fun group(value: String): String = GROUPS.firstOrNull { it.first == value }?.second ?: "Bins"

    fun sort(value: String): String = SORTS.firstOrNull { it.first == value }?.second ?: "Your order"

    /** A filter row's value: "Any", the one pick's name, or "2 picked". */
    fun picked(values: List<String>?, names: List<Pair<String, String>>): String = when {
        values.isNullOrEmpty() -> "Any"
        values.size == 1 -> names.firstOrNull { it.first == values[0] }?.second ?: "1 picked"
        else -> "${values.size} picked"
    }

    fun pickedCount(count: Int): String = if (count == 0) "Any" else "$count picked"

    /** A saved view's chip: "★ Default" for the default one, else its name. */
    fun chip(view: TaskView): String = if (view.id.equals(TaskView.DEFAULT_ID, ignoreCase = true)) "★ ${view.name}" else view.name

    /** [config] with every "Show only" filter cleared; the grouping, sort, rows and pinning stay (the footer's Clear). */
    fun cleared(config: TaskViewConfig): TaskViewConfig =
        config.copy(only = TaskViewFilter.NONE, labels = null, noLabel = false, tags = null, match = TaskViewConfig.MATCH_ANY, bins = null)

    /** Whether any "Show only" filter is on (the header's Options icon is filled then). */
    fun filters(config: TaskViewConfig): Boolean = cleared(config) != config

    /** [values] with [value] ticked or unticked; null for none. */
    fun toggled(values: List<String>?, value: String): List<String>? {
        val held = values.orEmpty()
        return (if (held.any { it.equals(value, ignoreCase = true) }) held.filterNot { it.equals(value, ignoreCase = true) } else held + value).ifEmpty { null }
    }
}
