package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.SyncAutoSavedItem
import com.accountabilibuddy.shared.sync.SyncCategory
import com.accountabilibuddy.shared.sync.SyncLaterItem
import com.accountabilibuddy.shared.sync.SyncNextTask
import com.accountabilibuddy.shared.sync.SyncParked
import java.time.OffsetDateTime

/**
 * The phone's old lists (the next session's list, Later, come-backs, Auto-saved, categories) as views of the one task list (ADR 76;
 * the PC's TaskFacadeLists, the same rules), so the phone's screens keep working until R7.3 replaces them. Unlike the old sync fields
 * these show what the phone showed before: a ticked task stays in the next session's list whatever day it was ticked, History's
 * finished Later items and come-backs show ticked in their list, a local item kept here keeps its text. No tombstones; a change the
 * screens make goes back through [TaskLocalEdits]. Positions are the old lists' ([OldTaskFieldsWriter.oldPosition]).
 */
object TaskFacadeLists {
    /** The old list a live task shows in; a finished Shelf task (on its way to History) shows in its Shelf bin's list. */
    fun listOf(task: TaskItem, now: OffsetDateTime): OldTaskList = OldTaskFieldsWriter.listOf(task.copy(bin = shownBin(task)), now)

    /** The bin whose place in its old list a task takes: a finished Shelf task's own Shelf bin, else its bin. */
    fun shownBin(task: TaskItem): TaskBin {
        val previous = task.prevBin
        return if (task.bin == TaskBin.DONE && (previous == TaskBin.SORT || previous == TaskBin.NEXT || previous == TaskBin.LATER)) previous else task.bin
    }

    /** The next session's list: This session, Queued and Finished (ticked ones of any day), in order. */
    fun draft(lists: TaskLists, now: OffsetDateTime): List<SyncNextTask> {
        val labels = labelsOf(lists)
        return liveIn(lists, OldTaskList.NEXT_TASKS, now).map { OldTaskFieldsWriter.toNextTask(it, labels) }.sortedWith(compareBy<SyncNextTask> { it.position }.thenBy { it.id.lowercase() })
    }

    /** Later and Next (marked), with History's finished ones of the last 90 days ticked. */
    fun later(lists: TaskLists, now: OffsetDateTime): List<SyncLaterItem> {
        val labels = labelsOf(lists)
        return (liveIn(lists, OldTaskList.LATER, now).map { OldTaskFieldsWriter.toLaterItem(shown(it), labels) } +
            finished(lists, OldTaskList.LATER, now).map { OldTaskFieldsWriter.toLaterItem(it, labels) })
            .sortedWith(compareBy<SyncLaterItem> { it.position }.thenBy { it.id.lowercase() })
    }

    /** The come-backs: To sort (except Auto-saved's, within its 30 days), with History's finished ones of the last 90 days ticked. */
    fun parked(lists: TaskLists, now: OffsetDateTime): List<SyncParked> {
        val labels = labelsOf(lists)
        return (liveIn(lists, OldTaskList.PARKED, now).map { OldTaskFieldsWriter.toParked(shown(it), labels) } +
            finished(lists, OldTaskList.PARKED, now).map { OldTaskFieldsWriter.toParked(it, labels) })
            .sortedWith(compareBy<SyncParked> { it.position }.thenBy { it.id.lowercase() })
    }

    /** Auto-saved: the open To sort tasks that came from it, within its 30 days. */
    fun autoSaved(lists: TaskLists, now: OffsetDateTime): List<SyncAutoSavedItem> {
        val labels = labelsOf(lists)
        return liveIn(lists, OldTaskList.AUTO_SAVED, now).map { OldTaskFieldsWriter.toAutoSaved(it, labels) }
            .sortedWith(compareBy<SyncAutoSavedItem> { it.position }.thenBy { it.id.lowercase() })
    }

    /** The live labels as the phone's task categories, in order: name, opaque colour and, once chosen, when. */
    fun categories(lists: TaskLists): List<SyncCategory> = lists.labels.map { it.label }
        .filter { it.isLive && it.name.trim().isNotEmpty() }
        .sortedWith(compareBy<TaskLabel> { it.position }.thenBy { it.id })
        .map { label ->
            SyncCategory(label.name, TaskLabelColors.opaque(label.color), label.editedAt.takeIf { DotNetTime.compare(it, TaskMigration.UNSTAMPED_AT) > 0 }?.toInstant())
        }

    private fun labelsOf(lists: TaskLists): List<TaskLabel> = lists.labels.map { it.label }

    private fun liveIn(lists: TaskLists, list: OldTaskList, now: OffsetDateTime): List<TaskItem> =
        lists.tasks.map { it.task }.filter { it.isLive && listOf(it, now) == list }

    private fun shown(task: TaskItem): TaskItem {
        val shown = shownBin(task)
        return if (shown == task.bin) task else task.copy(bin = shown)
    }

    // History's finished Later tasks and come-backs, ticked in their list for 90 days.
    private fun finished(lists: TaskLists, list: OldTaskList, now: OffsetDateTime): List<TaskItem> {
        val live = lists.tasks.filter { it.task.isLive }.map { it.id.lowercase() }.toSet()
        val shownTicks = OldTaskFieldsWriter.FINISHED_SHOWN.seconds * 10_000_000L
        return lists.log.map { it.entry }
            .filter { entry ->
                entry.isLive && entry.kind == TaskHistoryKind.FINISHED && entry.task.doneAt != null &&
                    DotNetTime.ticks(now) - DotNetTime.ticks(entry.at) < shownTicks && entry.id.lowercase() !in live
            }
            .map { entry ->
                entry.task.copy(
                    id = entry.id,
                    bin = entry.task.prevBin ?: TaskBin.LATER,
                    // An Undo or untick must be newer than the entry, not only than the task it holds.
                    editedAt = if (DotNetTime.compare(entry.editedAt, entry.task.editedAt) > 0) entry.editedAt else entry.task.editedAt,
                    deletedAt = null,
                )
            }
            .filter { OldTaskFieldsWriter.listOf(it.bin) == list }
    }
}
