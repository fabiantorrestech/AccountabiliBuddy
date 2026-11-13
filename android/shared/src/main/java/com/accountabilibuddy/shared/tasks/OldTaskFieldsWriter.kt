package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.SessionTaskItem
import com.accountabilibuddy.shared.sync.SharedItem
import com.accountabilibuddy.shared.sync.SharedListMerge
import com.accountabilibuddy.shared.sync.SyncAutoSavedItem
import com.accountabilibuddy.shared.sync.SyncCategory
import com.accountabilibuddy.shared.sync.SyncComeback
import com.accountabilibuddy.shared.sync.SyncLaterItem
import com.accountabilibuddy.shared.sync.SyncNextTask
import com.accountabilibuddy.shared.sync.SyncParked
import com.accountabilibuddy.shared.sync.TaskLabels
import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.ZoneOffset

/**
 * Rebuilds the old sync fields from the task list (ADR 76; the PC's OldTaskFieldsWriter, the same result), for apps older than it,
 * on every write; they are never stored on their own. Each task travels in its bin's old list ([listOf]) with what that list can
 * carry; a deleted task as a tombstone there. A task that changed bins in the last 30 days also leaves a tombstone in the other old
 * lists, so an older app takes it out of the list it was in. Next goes into `later` with the [SyncLaterItem.NEXT_BIN] marker.
 * Positions keep each bin's tasks together in the shared old list ([oldPosition]). Labels go as categories with an opaque colour.
 * An open To sort task from Auto-saved goes in `autoSaved` for its 30 days, then in `parked`, never in both.
 */
object OldTaskFieldsWriter {
    /** How far a later bin's positions are moved in an old list that holds two or three bins. */
    const val BIN_SPACING = 1_000_000.0

    /** How long History's finished tasks still show ticked in `later` and `parked`, as ticked Later items did. */
    val FINISHED_SHOWN: Duration = Duration.ofDays(90)

    /**
     * The old list a task travels in at [now]: its bin's, except an open To sort task that came from Auto-saved and is within
     * Auto-saved's 30 days, which travels in `autoSaved` (and only there).
     */
    fun listOf(task: TaskItem, now: OffsetDateTime): OldTaskList {
        val autoSavedHome = task.bin == TaskBin.SORT && task.autoSavedFor != null && task.doneAt == null &&
            isWithin(task.createdAt, now, SyncAutoSavedItem.KEPT)
        return if (autoSavedHome) OldTaskList.AUTO_SAVED else listOf(task.bin)
    }

    /** Whether two old lists are the same bin's (`parked` and `autoSaved` both hold To sort). */
    fun sameHome(first: OldTaskList, second: OldTaskList): Boolean = first == second || (isSortList(first) && isSortList(second))

    fun listOf(bin: TaskBin): OldTaskList = when (bin) {
        TaskBin.SESSION, TaskBin.QUEUED, TaskBin.DONE -> OldTaskList.NEXT_TASKS
        TaskBin.NEXT, TaskBin.LATER -> OldTaskList.LATER
        TaskBin.SORT -> OldTaskList.PARKED
    }

    /** A bin's position in its old list: Queued after This session, Finished after Queued; Later after Next. */
    fun oldPosition(bin: TaskBin, position: Double): Double = when (bin) {
        TaskBin.QUEUED, TaskBin.LATER -> position + BIN_SPACING
        TaskBin.DONE -> position + 2 * BIN_SPACING
        else -> position
    }

    /** The inverse of [oldPosition]. */
    fun newPosition(bin: TaskBin, oldPosition: Double): Double = when (bin) {
        TaskBin.QUEUED, TaskBin.LATER -> oldPosition - BIN_SPACING
        TaskBin.DONE -> oldPosition - 2 * BIN_SPACING
        else -> oldPosition
    }

    /** [stubLocalItems]: write a task kept on one device as ADR 72's stub (the sync file); false keeps its text (a backup). */
    fun write(
        tasks: List<SyncTaskItem>,
        labels: List<SyncTaskLabel>,
        history: List<SyncTaskLogItem>,
        now: OffsetDateTime,
        zone: ZoneId,
        stubLocalItems: Boolean = true,
    ): OldTaskFields {
        val labelList = labels.map { it.label }
        val today = localDate(now, zone)
        val next = ListBuilder<SyncNextTask>()
        val later = ListBuilder<SyncLaterItem>()
        val parked = ListBuilder<SyncParked>()
        val autoSaved = ListBuilder<SyncAutoSavedItem>()

        fun addTombstone(list: OldTaskList, id: String, at: OffsetDateTime, localTo: String?, createdAt: OffsetDateTime) {
            val time = at.toInstant()
            when (list) {
                OldTaskList.NEXT_TASKS -> next.add(SyncNextTask(id, "", null, null, null, null, time, time, 0.0, localTo = localTo))
                OldTaskList.LATER -> later.add(SyncLaterItem(id, "", null, null, null, null, null, null, time, time, 0.0, localTo = localTo))
                OldTaskList.AUTO_SAVED ->
                    autoSaved.add(SyncAutoSavedItem(id, "", utcDate(createdAt).toString(), createdAt.toInstant(), time, time, 0.0, localTo = localTo))

                OldTaskList.PARKED -> parked.add(SyncParked(id, "", createdAt.toInstant(), null, time, time, 0.0, localTo = localTo))
            }
        }

        for (item in tasks) {
            val task = item.task
            val list = if (task.isLive) listOf(task, now) else listOf(task.bin)
            if (!task.isLive) {
                addTombstone(list, task.id, task.deletedAt ?: task.editedAt, task.localTo, task.createdAt)
                if (task.bin == TaskBin.SORT && task.autoSavedFor != null) {
                    // An older app may still hold it under Auto-saved.
                    addTombstone(OldTaskList.AUTO_SAVED, task.id, task.deletedAt ?: task.editedAt, task.localTo, task.createdAt)
                }

                continue
            }

            if (task.bin == TaskBin.DONE && (task.doneAt == null || localDate(task.doneAt, zone) != today)) {
                // Finished before today: on its way to History; older apps keep the copy they have.
                continue
            }

            when (list) {
                OldTaskList.NEXT_TASKS -> next.add(toNextTask(task, labelList))
                OldTaskList.LATER -> later.add(toLaterItem(task, labelList))
                OldTaskList.AUTO_SAVED -> autoSaved.add(toAutoSaved(task, labelList))
                OldTaskList.PARKED -> parked.add(toParked(task, labelList))
            }

            val moved = item.stamps.of(TaskField.BIN)
            if (moved != null && isWithin(moved.at, now, SharedListMerge.TOMBSTONES_KEPT)) {
                for (other in OldTaskList.entries.filter { it != list }) {
                    addTombstone(other, task.id, moved.at, null, task.createdAt)
                }
            }
        }

        for (entry in history.map { it.entry }.filter { it.isLive && it.kind == TaskHistoryKind.FINISHED }) {
            if (!isWithin(entry.at, now, FINISHED_SHOWN) || entry.task.doneAt == null) {
                continue
            }

            val task = entry.task.copy(bin = entry.task.prevBin ?: TaskBin.LATER)
            when (listOf(task.bin)) {
                OldTaskList.LATER -> later.add(toLaterItem(task, labelList))
                OldTaskList.PARKED -> parked.add(toParked(task, labelList))
                else -> Unit
            }
        }

        val comebacks = tasks
            .map { it.task }
            .filter { it.isLive && listOf(it, now) == OldTaskList.PARKED && it.doneAt == null && it.devices == null }
            .sortedBy { it.position }
            .map { SyncComeback(it.id, it.title, it.createdAt.toInstant()) }
        val categories = labelList
            .filter { it.isLive }
            .sortedBy { it.position }
            .map { SyncCategory(it.name, TaskLabelColors.opaque(it.color), it.editedAt.toInstant()) }

        return OldTaskFields(
            finish(next.items, stubLocalItems),
            finish(later.items, stubLocalItems),
            finish(parked.items, stubLocalItems),
            comebacks,
            categories,
            finish(autoSaved.items, stubLocalItems),
        )
    }

    fun toNextTask(task: TaskItem, labels: List<TaskLabel>): SyncNextTask {
        val label = labelOf(task, labels)
        return SyncNextTask(
            task.id,
            task.title,
            task.doneAt?.toInstant(),
            label?.name,
            label?.let { TaskLabelColors.opaque(it.color) },
            subtasksOf(task),
            task.editedAt.toInstant(),
            null,
            oldPosition(task.bin, task.position),
            task.estimateMinutes,
            task.note,
            task.plannedFor,
            TaskLabels.clean(task.tags),
            task.localTo,
        )
    }

    fun toLaterItem(task: TaskItem, labels: List<TaskLabel>): SyncLaterItem {
        val label = labelOf(task, labels)
        return SyncLaterItem(
            task.id,
            task.title,
            subtasksOf(task),
            label?.name,
            label?.let { TaskLabelColors.opaque(it.color) },
            task.estimateMinutes,
            task.note,
            task.dueOn?.toString(),
            task.editedAt.toInstant(),
            null,
            oldPosition(task.bin, task.position),
            task.plannedFor,
            task.doneAt?.toInstant(),
            TaskLabels.clean(task.tags),
            task.localTo,
            if (task.bin == TaskBin.NEXT) SyncLaterItem.NEXT_BIN else null,
        )
    }

    fun toParked(task: TaskItem, labels: List<TaskLabel>): SyncParked {
        val label = labelOf(task, labels)
        return SyncParked(
            task.id,
            task.title,
            task.createdAt.toInstant(),
            task.doneAt?.toInstant(),
            task.editedAt.toInstant(),
            null,
            oldPosition(task.bin, task.position),
            label?.name,
            label?.let { TaskLabelColors.opaque(it.color) },
            TaskLabels.clean(task.tags),
            task.localTo,
        )
    }

    /** The task as an Auto-saved item: saved at its creation, for its plan day. */
    fun toAutoSaved(task: TaskItem, labels: List<TaskLabel>): SyncAutoSavedItem {
        val label = labelOf(task, labels)
        return SyncAutoSavedItem(
            task.id,
            task.title,
            (task.autoSavedFor ?: utcDate(task.createdAt)).toString(),
            task.createdAt.toInstant(),
            task.editedAt.toInstant(),
            null,
            oldPosition(task.bin, task.position),
            label?.name,
            label?.let { TaskLabelColors.opaque(it.color) },
            TaskLabels.clean(task.tags),
            task.localTo,
        )
    }

    private fun isSortList(list: OldTaskList): Boolean = list == OldTaskList.PARKED || list == OldTaskList.AUTO_SAVED

    private fun subtasksOf(task: TaskItem): List<SessionTaskItem>? =
        task.steps?.takeIf { it.isNotEmpty() }?.map { SessionTaskItem(it.id, it.title, it.doneAt?.toInstant(), emptyList()) }

    private fun labelOf(task: TaskItem, labels: List<TaskLabel>): TaskLabel? =
        task.labelId?.let { id -> labels.firstOrNull { it.id == id && it.isLive } }

    private fun localDate(at: OffsetDateTime, zone: ZoneId): LocalDate = at.atZoneSameInstant(zone).toLocalDate()

    private fun utcDate(at: OffsetDateTime): LocalDate = at.withOffsetSameInstant(ZoneOffset.UTC).toLocalDate()

    // now - since < length, in the PC's ticks.
    private fun isWithin(since: OffsetDateTime, now: OffsetDateTime, length: Duration): Boolean =
        DotNetTime.ticks(now) - DotNetTime.ticks(since) < length.seconds * 10_000_000L

    @Suppress("UNCHECKED_CAST")
    private fun <T : SharedItem> finish(items: Collection<T>, stubLocalItems: Boolean): List<T> {
        val ordered = items.sortedWith(compareBy<T> { it.position }.thenBy { it.id })
        return if (stubLocalItems) SharedListMerge.forFile(ordered) else ordered
    }

    // One entry per id in an old list: a live one over a tombstone, then the newest.
    private class ListBuilder<T : SharedItem> {
        private val byId = LinkedHashMap<String, T>()

        val items: Collection<T> get() = byId.values

        fun add(item: T) {
            val held = byId[item.id]
            if (held == null || better(item, held)) {
                byId[item.id] = item
            }
        }

        private fun better(candidate: T, held: T): Boolean {
            val candidateLive = candidate.deletedAt == null
            val heldLive = held.deletedAt == null
            if (candidateLive != heldLive) {
                return candidateLive
            }

            return candidate.editedAt > held.editedAt
        }
    }
}
