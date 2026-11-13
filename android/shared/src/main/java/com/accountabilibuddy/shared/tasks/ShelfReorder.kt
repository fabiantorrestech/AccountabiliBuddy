package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.SharedListMerge
import java.time.OffsetDateTime

/**
 * Reordering on the phone's Tasks page (round 2 R7.3b; change note §2: "dragging up or down still reorders within the bin; nothing
 * in the reorder logic changes"). A task's place is its position among the tasks of its own bin, changed as a bin edit
 * ([TaskField.BIN]) exactly as the PC's `TaskActions.Reorder` does: a place between the two neighbours, nothing else.
 *
 * A page may show only some of a bin (a filter, a label's page) or several bins (a label's page, one list): a dragged task then goes
 * between the nearest rows of its own bin above and below where it was let go.
 */
object ShelfReorder {
    /** Rows can only be put in an order of their own in "Your order": any other sort would move them straight back. */
    fun canReorder(config: TaskViewConfig): Boolean = config.sort == TaskViewConfig.SORT_DEFAULT

    /**
     * Puts the task [id] before [beforeId] in its bin (at the end for null): the PC's `TaskActions.Reorder`. Nothing changes when
     * the task isn't live, [beforeId] isn't in its bin, or it is at that place already.
     */
    fun before(lists: TaskLists, id: String, beforeId: String?, self: String, now: OffsetDateTime): ShelfChange {
        val none = ShelfChange(lists, emptyList(), emptyList())
        val index = lists.tasks.indexOfFirst { it.id.equals(id, ignoreCase = true) && it.task.isLive }
        if (index < 0) return none
        val held = lists.tasks[index]
        val others = othersOf(lists, held.task)
        val at = if (beforeId == null) others.size else others.indexOfFirst { it.id.equals(beforeId, ignoreCase = true) }
        if (at < 0) return none
        val position = SharedListMerge.positionBetween(others.getOrNull(at - 1)?.position, others.getOrNull(at)?.position)
        val edited = TaskEdits.edit(held, TaskField.BIN, held.task.copy(position = position), now, self)
        if (edited === held) return none
        return ShelfChange(lists.copy(tasks = lists.tasks.toMutableList().also { it[index] = edited }), listOf(held.id), listOf(held.task))
    }

    /** The rows as they are drawn while [ids] are dragged over the list with [insertAt] of the other rows above them. */
    fun <T> displayed(shown: List<T>, ids: Collection<String>, insertAt: Int, idOf: (T) -> String): List<T> {
        val dragged = ids.map { it.lowercase() }.toSet()
        val moving = shown.filter { idOf(it).lowercase() in dragged }
        val rest = shown.filter { idOf(it).lowercase() !in dragged }
        val at = insertAt.coerceIn(0, rest.size)
        return rest.take(at) + moving + rest.drop(at)
    }

    /**
     * The drop: the tasks [ids] of the page (whose rows are [shown], in order) let go with [insertAt] of the other rows above them.
     * Each goes, in the rows' order, before the next row of its own bin below that place; with none below, right after the nearest
     * one above. A task with no other row of its own bin on the page stays where it is. A drop that leaves the rows as they were
     * changes nothing. [ShelfChange.before] is what Undo puts back ([ShelfEdits.restore] with [TaskField.BIN]).
     */
    fun place(lists: TaskLists, shown: List<String>, ids: List<String>, insertAt: Int, self: String, now: OffsetDateTime): ShelfChange {
        val none = ShelfChange(lists, emptyList(), emptyList())
        val order = shown.map { it.lowercase() }
        if (displayed(order, ids, insertAt) { it } == order) return none
        val dragged = ids.map { it.lowercase() }.toSet()
        val moving = order.filter { it in dragged }
        val rest = order.filter { it !in dragged }
        val at = insertAt.coerceIn(0, rest.size)
        var working = lists
        val touched = ArrayList<String>()
        val before = ArrayList<TaskItem>()
        val placed = ArrayList<String>()
        for (id in moving) {
            val task = live(working, id) ?: continue
            val sameBin = { other: String -> live(working, other)?.bin == task.bin }
            val next = rest.drop(at).firstOrNull(sameBin)
            val previous = (rest.take(at) + placed).lastOrNull(sameBin)
            placed += id
            val result = when {
                next != null -> before(working, id, next, self, now)
                previous != null -> {
                    // Right after the row above: before whatever follows it in the whole bin (shown or not), or at the end.
                    val others = othersOf(working, task)
                    before(working, id, others.getOrNull(others.indexOfFirst { it.id.equals(previous, ignoreCase = true) } + 1)?.id, self, now)
                }

                else -> none
            }
            if (!result.changed) continue
            working = result.lists
            touched += result.ids
            before += result.before
        }

        return if (touched.isEmpty()) none else ShelfChange(working, touched, before)
    }

    /** One row up ([by] -1) or down (1) among the page's rows: the same move without dragging (a screen reader's action). */
    fun step(lists: TaskLists, shown: List<String>, id: String, by: Int, self: String, now: OffsetDateTime): ShelfChange {
        val at = shown.indexOfFirst { it.equals(id, ignoreCase = true) }
        if (at < 0) return ShelfChange(lists, emptyList(), emptyList())
        return place(lists, shown, listOf(id), (at + by).coerceIn(0, shown.size - 1), self, now)
    }

    private fun live(lists: TaskLists, id: String): TaskItem? = lists.tasks.firstOrNull { it.id.equals(id, ignoreCase = true) && it.task.isLive }?.task

    // The other live tasks of the task's bin in their order (a stable sort by position, as the PC's).
    private fun othersOf(lists: TaskLists, task: TaskItem): List<TaskItem> =
        lists.tasks.map { it.task }.filter { it.isLive && it.bin == task.bin && !it.id.equals(task.id, ignoreCase = true) }.sortedBy { it.position }
}
