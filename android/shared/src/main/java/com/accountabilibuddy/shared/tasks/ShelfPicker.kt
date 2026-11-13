package com.accountabilibuddy.shared.tasks

/**
 * The picker a drag opens by holding the bottom bar (round 2 R7.3b; change note §4c-1; board Tasks-Phone-Drag-Pick): every place of
 * the grouping in use to drop on, however far away its page is.
 *
 * - Bins (and one list, Group by None): To sort, Next and Later.
 * - Labels: every label with a page in the pages' order, then the labels no shown task has (a page exists only for a label with a
 *   task, but a task can still be given any label), then "No label".
 * - Tags: every tag page. "No tag" is left out: a drop there would change nothing (a drop only ever adds a tag).
 */
object ShelfPicker {
    fun tiles(grouping: String?, pages: List<ShelfPage>, labels: List<TaskLabel>, shownKey: String?): List<ShelfPickerTile> {
        val targets: List<ShelfPage> = when (grouping) {
            TaskViewConfig.GROUP_LABEL -> {
                val withPage = pages.filter { it.labelId != null }
                val shown = withPage.map { it.labelId!!.lowercase() }.toSet()
                val others = labels.filter { it.isLive && it.id.lowercase() !in shown }.sortedBy { it.position }
                    .map { ShelfPage("label:" + it.id.lowercase(), it.name, null, it.id, it.color, null, emptyList()) }
                val none = pages.firstOrNull { it.key == TaskViewRules.LABEL_NONE } ?: ShelfPage(TaskViewRules.LABEL_NONE, "No label", null, null, null, null, emptyList())
                withPage + others + none
            }

            TaskViewConfig.GROUP_TAG -> pages.filter { it.tag != null }
            TaskViewConfig.GROUP_BINS -> pages.filter { it.bin != null }
            else -> {
                val all = pages.flatMap { it.tasks }
                TaskBins.SHELF.map { bin -> ShelfPage("bin:" + bin.name.lowercase(), TaskBins.name(bin), bin, null, null, null, all.filter { it.bin == bin }) }
            }
        }

        return targets.map { page ->
            val here = page.key == shownKey
            ShelfPickerTile(page, if (here) "You are here" else countText(page.count), here)
        }
    }

    /** "2 tasks", "1 task", "No tasks". */
    fun countText(count: Int): String = when (count) {
        0 -> "No tasks"
        1 -> "1 task"
        else -> "$count tasks"
    }

    /** What the grouping's pages are called in the drag's words: "bin", "label", "tag". */
    fun noun(grouping: String?): String = when (grouping) {
        TaskViewConfig.GROUP_LABEL -> "label"
        TaskViewConfig.GROUP_TAG -> "tag"
        else -> "bin"
    }

    /** The bottom bar's left zone: "Hold to pick a bin"; "Release on a bin above" while the picker is open. */
    fun barText(grouping: String?, open: Boolean): String = if (open) "Release on a ${noun(grouping)} above" else "Hold to pick a ${noun(grouping)}"

    /** The picker's title: "Drop on a label". */
    fun title(grouping: String?): String = "Drop on a ${noun(grouping)}"

    /** The picker's foot note. */
    fun note(grouping: String?): String = when (grouping) {
        TaskViewConfig.GROUP_LABEL -> "In bin view this shows To sort, Next and Later. Changed your mind? Drop on Cancel."
        TaskViewConfig.GROUP_TAG -> "The tag is added and the task keeps its other tags. Changed your mind? Drop on Cancel."
        else -> "Grouped by label this shows your labels. Changed your mind? Drop on Cancel."
    }
}
