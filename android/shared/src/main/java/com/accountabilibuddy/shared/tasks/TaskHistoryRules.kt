package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.FieldStamps
import java.time.Duration
import java.time.OffsetDateTime

/**
 * History (ADR 76), pure (the PC's TaskHistoryRules, the same rules): a task that is finished, archived or deleted is kept with its
 * snapshot (90 days stored, the last [LISTED] listed). [remove] files tasks there, [bringBack] revives an archived or deleted one under
 * its own id and number, [doAgain] makes a new Queued copy of a finished one, [reconciled] drops entries of tasks that are live again.
 */
object TaskHistoryRules {
    val LISTED: Duration = Duration.ofDays(30)

    /** A change's result: the lists and the tasks it touched (none: nothing changed). */
    data class Result(val lists: TaskLists, val ids: List<String>)

    /** The live entries of the last [LISTED], newest first. */
    fun list(lists: TaskLists, now: OffsetDateTime): List<TaskHistoryEntry> {
        val listedTicks = LISTED.seconds * 10_000_000L
        return lists.log.map { it.entry }
            .filter { it.isLive && DotNetTime.ticks(now) - DotNetTime.ticks(it.at) < listedTicks }
            .sortedByDescending { DotNetTime.ticks(it.at) }
    }

    /** Files live tasks in History as [kind] (archived or deleted) with everything they had, and takes them out of the list. */
    fun remove(lists: TaskLists, ids: List<String>, kind: TaskHistoryKind, now: OffsetDateTime, self: String): Result {
        val tasks = lists.tasks.toMutableList()
        val log = lists.log.toMutableList()
        val removed = ArrayList<String>()
        for (id in ids.map { it.lowercase() }.distinct()) {
            val index = tasks.indexOfFirst { it.id.equals(id, ignoreCase = true) && it.task.isLive }
            if (index < 0) {
                continue
            }

            val item = tasks[index]
            removed += item.id
            tasks[index] = item.deleted(nextStamp(now, item))
            putEntry(log, TaskHistoryEntry(item.id, kind, now, item.task, now), now)
        }

        return Result(if (removed.isEmpty()) lists else lists.copy(tasks = tasks, log = log), removed)
    }

    /**
     * "Do again": a finished task's entry makes a new task at the end of Queued with a new id ([newId]), its label, tags, time limit,
     * note and steps (open again, new ids), and the origin "Done again from 142"; reminders, due date and highlight are not copied.
     */
    fun doAgain(lists: TaskLists, entryId: String, now: OffsetDateTime, self: String, newId: () -> String): Result {
        val found = lists.log.firstOrNull { it.id.equals(entryId, ignoreCase = true) }?.entry
        if (found == null || !found.isLive || found.kind != TaskHistoryKind.FINISHED) {
            return Result(lists, emptyList())
        }

        val old = found.task
        val steps = old.steps?.map { TaskStep(newId(), it.title, estimateMinutes = it.estimateMinutes) }
        val copy = TaskItem(
            newId(), old.title, TaskBin.QUEUED, TaskListRules.endOf(lists, TaskBin.QUEUED), now, now, now,
            labelId = old.labelId, tags = old.tags, steps = steps?.ifEmpty { null }, estimateMinutes = old.estimateMinutes, effort = old.effort,
            note = old.note, origin = doneAgainOrigin(old.number), devices = old.devices, calendar = old.calendar,
        )
        return Result(lists.copy(tasks = lists.tasks + SyncTaskItem(copy, TaskFieldStamps.allAt(FieldStamp(now, self.lowercase())))), listOf(copy.id))
    }

    /**
     * "Bring back": an archived or deleted task comes back under its own id and number, in its last bin (Queued when that was This
     * session or Finished, as a place in a session is the cap's to give), at its old place, touched now. The entry goes.
     */
    fun bringBack(lists: TaskLists, entryId: String, now: OffsetDateTime, self: String): Result {
        val found = lists.log.firstOrNull { it.id.equals(entryId, ignoreCase = true) }
        if (found == null || !found.entry.isLive || found.entry.kind == TaskHistoryKind.FINISHED) {
            return Result(lists, emptyList())
        }

        val old = found.entry.task
        if (lists.tasks.any { it.id.equals(old.id, ignoreCase = true) && it.task.isLive }) {
            return Result(lists, emptyList())
        }

        val bin = if (old.bin == TaskBin.SESSION || old.bin == TaskBin.DONE) TaskBin.QUEUED else old.bin
        val tasks = lists.tasks.toMutableList()
        val index = tasks.indexOfFirst { it.id.equals(old.id, ignoreCase = true) }
        val at = if (index >= 0) nextStamp(now, tasks[index]) else now
        val back = old.copy(
            bin = bin,
            position = if (old.bin == bin) old.position else TaskListRules.endOf(lists, bin),
            steps = old.steps?.map { it.copy(focus = false) },
            doneAt = null,
            deletedAt = null,
            editedAt = at,
            touchedAt = at,
        )
        val revived = SyncTaskItem(back, TaskFieldStamps.allAt(FieldStamp(at, self.lowercase())))
        if (index >= 0) tasks[index] = revived else tasks += revived

        val log = lists.log.toMutableList()
        putEntry(log, found.entry.deleted(nextStamp(now, found)), now)
        return Result(lists.copy(tasks = tasks, log = log), listOf(old.id))
    }

    /** An archived or deleted entry whose task is live again goes: History never lists a task that is back. */
    fun reconciled(lists: TaskLists, now: OffsetDateTime): TaskLists {
        val live = lists.tasks.filter { it.task.isLive }.map { it.id.lowercase() }.toSet()
        fun stale(item: SyncTaskLogItem) = item.entry.isLive && item.entry.kind != TaskHistoryKind.FINISHED && item.id.lowercase() in live
        if (lists.log.none(::stale)) {
            return lists
        }

        return lists.copy(log = lists.log.map { if (stale(it)) SyncTaskLogItem(it.entry.deleted(nextStamp(now, it))) else it })
    }

    /** The live History entry for [id], if any. */
    fun liveEntry(lists: TaskLists, id: String): TaskHistoryEntry? =
        lists.log.firstOrNull { it.id.equals(id, ignoreCase = true) && it.entry.isLive }?.entry

    /** The entry [entry] filed for good: replaced in [lists] by its tombstone, stamped after it. */
    fun closed(lists: TaskLists, id: String, now: OffsetDateTime): TaskLists {
        val index = lists.log.indexOfFirst { it.id.equals(id, ignoreCase = true) && it.entry.isLive }
        if (index < 0) return lists
        val log = lists.log.toMutableList()
        putEntry(log, log[index].entry.deleted(nextStamp(now, log[index])), now)
        return lists.copy(log = log)
    }

    // "Done again from 142" (the PC's TaskOrigins.DoneAgain); "Done again" when the task had no number.
    private fun doneAgainOrigin(number: TaskNumber?): String = if (number == null) "Done again" else "Done again from ${number.value}"

    // The entry replaces the one with its id (stamped after it, so it wins) or is added.
    private fun putEntry(log: MutableList<SyncTaskLogItem>, entry: TaskHistoryEntry, now: OffsetDateTime) {
        val index = log.indexOfFirst { it.id.equals(entry.id, ignoreCase = true) }
        if (index < 0) {
            log += SyncTaskLogItem(entry.copy(editedAt = if (DotNetTime.compare(entry.editedAt, now) < 0) now else entry.editedAt))
            return
        }

        val at = nextStamp(now, log[index])
        log[index] = SyncTaskLogItem(entry.copy(editedAt = if (DotNetTime.compare(entry.editedAt, at) > 0) entry.editedAt else at))
    }

    // The shared lists' next stamp (the PC's SharedListMerge.NextStamp) with the times' offsets kept.
    internal fun nextStamp(now: OffsetDateTime, item: SyncTaskItem): OffsetDateTime =
        FieldStamps.next(now, newestOf(item.task.editedAt, item.task.deletedAt))

    private fun nextStamp(now: OffsetDateTime, item: SyncTaskLogItem): OffsetDateTime =
        FieldStamps.next(now, newestOf(item.entry.editedAt, item.entry.deletedAt))

    private fun newestOf(edited: OffsetDateTime, deleted: OffsetDateTime?): OffsetDateTime =
        if (deleted != null && DotNetTime.compare(deleted, edited) > 0) deleted else edited
}
