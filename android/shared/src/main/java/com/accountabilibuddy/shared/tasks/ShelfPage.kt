package com.accountabilibuddy.shared.tasks

/**
 * One page of the phone's Shelf (round 2 R7.3): a bin, a label, a tag, or everything (Group by None). [key] is the group's key
 * ([TaskViewGroup.key]), which the page order is stored under.
 *
 * @property labelColor The label's colour for its dot; null for a bin, a tag, "No label" and everything.
 * @property isNone The "No label" / "No tag" page.
 */
data class ShelfPage(
    val key: String,
    val title: String,
    val bin: TaskBin?,
    val labelId: String?,
    val labelColor: String?,
    val tag: String?,
    val tasks: List<TaskItem>,
) {
    val count: Int get() = tasks.size
    val highlighted: Int get() = tasks.count { it.highlight }
    val isNone: Boolean get() = key == TaskViewRules.LABEL_NONE || key == TaskViewRules.TAG_NONE

    /** The header's count: "4 · ★1", or "4" with nothing highlighted. */
    val countText: String get() = if (highlighted > 0) "$count · ★$highlighted" else count.toString()

    /** What a "+" on this page says: "Add to Next". */
    val addText: String get() = "Add to $title"
}
