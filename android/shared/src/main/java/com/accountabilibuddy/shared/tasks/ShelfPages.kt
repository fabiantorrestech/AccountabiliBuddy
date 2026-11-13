package com.accountabilibuddy.shared.tasks

import java.time.OffsetDateTime
import java.time.ZoneId

/**
 * The phone Shelf's pages (round 2 R7.3, change note §2): what [TaskViewRules] shows of the task list for the view in use, one page
 * per group, in this device's page order ([TaskPageOrder]).
 *
 * - Group by Bins: always the three Shelf bins (an empty bin is still a page, so the pages never come and go while you plan).
 * - Group by Label or Tag: one page per group that has a task; the open tasks of Today (This session, Queued) are in them too, each
 *   row naming its bin (board Tasks-Phone-Label). While a PC's session runs ([sessionRuns]) This session is that PC's checklist and
 *   is left out: its tasks are ticked and moved on the PC (or from Today), under the PC's rules.
 * - Group by None: one list of the Shelf, no pages to swipe.
 *
 * The chip row narrows what the pages hold without changing the view: a label chip in the bin grouping ([chipLabel]: a label's id, or
 * [NO_LABEL]), a bin chip in the label and tag groupings ([chipBin]).
 */
object ShelfPages {
    /** The chip for tasks with no label. */
    const val NO_LABEL = "none"

    private val WITH_TODAY: List<TaskBin> = listOf(TaskBin.SESSION, TaskBin.QUEUED) + TaskBins.SHELF

    fun build(
        lists: TaskLists,
        config: TaskViewConfig,
        now: OffsetDateTime,
        zone: ZoneId,
        search: String? = null,
        storedOrder: List<String>? = null,
        chipLabel: String? = null,
        chipBin: TaskBin? = null,
        sessionRuns: Boolean = false,
    ): List<ShelfPage> {
        val labels = lists.labels.map { it.label }
        val grouping = TaskPageOrder.groupingOf(config.group)
        val byBins = grouping == TaskViewConfig.GROUP_BINS
        val scope = when {
            byBins || grouping == null -> TaskBins.SHELF
            chipBin != null -> listOf(chipBin)
            sessionRuns -> WITH_TODAY - TaskBin.SESSION
            else -> WITH_TODAY
        }
        val live = labels.filter { it.isLive }.map { it.id.lowercase() }.toSet()
        val tasks = lists.tasks.map { it.task }.filter { task ->
            when {
                // The label chips show for bins and for one list (Group by None); the label and tag groupings have bin chips.
                !(byBins || grouping == null) || chipLabel == null -> true
                chipLabel == NO_LABEL -> task.labelId == null || task.labelId.lowercase() !in live
                else -> task.labelId.equals(chipLabel, ignoreCase = true)
            }
        }
        val result = TaskViewRules.apply(tasks, config, TaskViewContext(now, zone, labels, search, scope, showEmptyBins = byBins))
        val pages = result.groups.map { group ->
            val color = group.labelId?.let { id -> labels.firstOrNull { it.id.equals(id, ignoreCase = true) }?.color }
            ShelfPage(group.key, group.title, group.bin, group.labelId, color, group.tag, group.tasks)
        }
        return TaskPageOrder.apply(pages, storedOrder) { it.key }
    }

    /** How many tasks the pages hold (a task on two tag pages counts once): the Options sheet's "Show 10 tasks". */
    fun count(pages: List<ShelfPage>): Int = pages.flatMap { it.tasks }.distinctBy { it.id }.size

    /** "Show 10 tasks" / "Show 1 task". */
    fun showText(count: Int): String = "Show $count " + (if (count == 1) "task" else "tasks")
}
