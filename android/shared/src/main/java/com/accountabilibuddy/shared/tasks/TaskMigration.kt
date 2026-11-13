package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.SessionTaskItem
import com.accountabilibuddy.shared.sync.SyncAutoSavedItem
import com.accountabilibuddy.shared.sync.SyncLaterItem
import com.accountabilibuddy.shared.sync.SyncNextTask
import com.accountabilibuddy.shared.sync.SyncParked
import com.accountabilibuddy.shared.sync.TaskLabels
import com.accountabilibuddy.shared.sync.TaskPlannedFor
import java.time.Instant
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.ZoneOffset
import java.time.format.DateTimeParseException

/**
 * The one-time move of the phone's old task lists into the one shared list (ADR 76; the PC's TaskMigration, the same rule). Pure:
 * it reads the old stores' contents and the time, and gives the new lists; the app backs up first, runs it once behind a marker
 * and leaves the old stores untouched as the way back (R7.2).
 * - Categories become labels (id from the name, same name and colour); a category only an item names becomes one too.
 * - The next session's list: open tasks fill This session up to the session's actions in order (a task's first open steps are its
 *   actions), the rest go to Queued; tasks done today go to Finished; tasks done before today go to History.
 * - Later stays Later (Next when a newer PC marked it); ticked Later items go to History.
 * - Come-backs and parked thoughts go to To sort ("Parked" and when); ticked ones go to History.
 * - Auto-saved goes to To sort ("Auto-saved" and its day); past its 30 days it is left out.
 * - The saved filters become views (their search words stay local).
 * - Numbers go in creation order; ADR 72 labels become tags; a local item's keeper becomes its one device.
 *
 * Tombstones and local stubs stay tombstones. Ids are kept (labels and built-in views have fixed ids), nothing is random and no
 * stamp comes from [TaskMigrationInput.now], so the same input always gives the same result, and merging a result with another
 * run's adds nothing. The old stores keep moments, not offsets, so every time it writes is in UTC.
 */
object TaskMigration {
    const val NEXT_TASKS_SOURCE = "nextTasks"
    const val LATER_SOURCE = "later"
    const val COMEBACKS_SOURCE = "comebacks"
    const val AUTO_SAVED_SOURCE = "autoSaved"

    /** Stamps on what the user never stamped (a category's colour, the built-in views): older than any real edit (the PC's upgrade stamp). */
    val UNSTAMPED_AT: OffsetDateTime = OffsetDateTime.of(2000, 1, 1, 0, 0, 0, 0, ZoneOffset.UTC)

    private class Draft(val source: String, val sourceOrder: Int, val task: TaskItem)

    fun run(input: TaskMigrationInput): TaskMigrationResult {
        val today = localDate(input.now, input.zone)

        val book = TaskLabelBook(emptyList())
        var labelsFromCategories = 0
        for (category in input.categories) {
            if (category.name.trim().isEmpty() || book.named(category.name) != null) {
                continue
            }

            book.idForName(category.name, category.color, category.colorEditedAt?.let(::time) ?: UNSTAMPED_AT)
            labelsFromCategories++
        }

        val labelsBeforeItems = book.made
        val sources = linkedMapOf(NEXT_TASKS_SOURCE to 0, LATER_SOURCE to 0, COMEBACKS_SOURCE to 0, AUTO_SAVED_SOURCE to 0)
        val sourceTombstones = LinkedHashMap(sources)
        val drafts = ArrayList<Draft>()
        var expired = 0

        fun count(deletedAt: Instant?, source: String) {
            val counts = if (deletedAt == null) sources else sourceTombstones
            counts[source] = counts.getValue(source) + 1
        }

        for (item in input.nextTasks) {
            count(item.deletedAt, NEXT_TASKS_SOURCE)
            drafts += Draft(NEXT_TASKS_SOURCE, 0, fromNextTask(item, book))
        }

        for (item in input.later) {
            count(item.deletedAt, LATER_SOURCE)
            drafts += Draft(LATER_SOURCE, 1, fromLaterItem(item, book))
        }

        for (item in input.comebacks) {
            count(item.deletedAt, COMEBACKS_SOURCE)
            drafts += Draft(COMEBACKS_SOURCE, 2, fromParked(item, book, input.zone))
        }

        for (item in input.autoSaved) {
            count(item.deletedAt, AUTO_SAVED_SOURCE)
            if (item.deletedAt == null && item.isExpired(input.now.toInstant())) {
                expired++
                continue
            }

            drafts += Draft(AUTO_SAVED_SOURCE, 3, fromAutoSaved(item, book))
        }

        // One task per id: a live copy over a tombstone, then the newest, then the earlier list.
        var duplicates = 0
        val winners = ArrayList<Draft>()
        for (group in drafts.groupBy { it.task.id }.values) {
            val ordered = group.sortedWith(
                compareByDescending<Draft> { it.task.isLive }.thenByDescending { DotNetTime.ticks(it.task.editedAt) }.thenBy { it.sourceOrder },
            )
            winners += Draft(ordered[0].source, ordered[0].sourceOrder, withLoserConflicts(ordered[0].task, ordered.drop(1)))
            duplicates += ordered.drop(1).count { it.task.isLive }
        }

        val tasks = ArrayList<TaskItem>()
        val history = ArrayList<TaskHistoryEntry>()
        val openNext = ArrayList<TaskItem>()
        for (draft in winners) {
            val task = draft.task
            if (!task.isLive) {
                tasks += task
                continue
            }

            val doneAt = task.doneAt
            if (doneAt != null) {
                val doneToday = localDate(doneAt, input.zone) == today
                val done = task.copy(bin = TaskBin.DONE, prevBin = task.bin)
                if (draft.source == NEXT_TASKS_SOURCE && doneToday) {
                    tasks += done
                } else {
                    history += TaskHistoryEntry(task.id, TaskHistoryKind.FINISHED, doneAt, done, task.editedAt)
                    tasks += task.deleted(task.editedAt)
                }

                continue
            }

            if (draft.source == NEXT_TASKS_SOURCE) openNext += task else tasks += task
        }

        tasks += fillSession(openNext, input.sessionActions)

        // Numbers in creation order over every live task and History snapshot.
        var nextNumber = maxOf(1, input.firstNumber)
        val numbered = HashMap<String, TaskNumber>()
        for (task in (tasks.filter { it.isLive } + history.map { it.task }).sortedWith(compareBy<TaskItem> { DotNetTime.ticks(it.createdAt) }.thenBy { it.id })) {
            numbered[task.id] = TaskNumber(nextNumber++, temporary = !input.isNumberingDevice)
        }

        val syncTasks = tasks
            .map { if (it.isLive) it.copy(number = numbered.getValue(it.id)) else it }
            .map { SyncTaskItem(it, if (it.isLive) stampsAt(it.editedAt) else TaskFieldStamps.NONE) }
            .sortedWith(compareBy<SyncTaskItem> { it.task.bin.ordinal }.thenBy { it.position }.thenBy { it.id })
        val syncHistory = history
            .map { it.copy(task = it.task.copy(number = numbered.getValue(it.id))) }
            .map { SyncTaskLogItem(it) }
            .sortedWith(compareBy<SyncTaskLogItem> { it.position }.thenBy { it.id })

        val searches = LinkedHashMap<String, String>()
        val views = views(input, book, searches)

        val bins = TaskBin.entries.associateWith { bin -> syncTasks.count { it.task.isLive && it.task.bin == bin } }
        val counts = TaskMigrationCounts(
            sources,
            sourceTombstones,
            bins,
            syncHistory.size,
            expired,
            duplicates,
            syncTasks.count { !it.task.isLive },
            labelsFromCategories,
            book.made - labelsBeforeItems,
            views.size,
            0,
        )

        return TaskMigrationResult(syncTasks, book.labels.map { SyncTaskLabel(it) }, views.map { SyncTaskView(it) }, syncHistory, searches, counts)
    }

    /**
     * The next session's open tasks in their order: each goes to This session while its actions fit (its first open steps take the
     * free places and are focused; a task without open steps takes one), the rest to Queued.
     */
    fun fillSession(openTasks: List<TaskItem>, sessionActions: Int): List<TaskItem> {
        var free = maxOf(0, sessionActions)
        val filled = ArrayList<TaskItem>(openTasks.size)
        for (task in openTasks.sortedWith(compareBy<TaskItem> { it.position }.thenBy { it.id })) {
            if (free == 0) {
                filled += task.copy(bin = TaskBin.QUEUED)
                continue
            }

            val steps = task.steps.orEmpty()
            val openSteps = steps.count { !it.isDone }
            if (openSteps == 0) {
                free--
                filled += task.copy(bin = TaskBin.SESSION)
                continue
            }

            var toFocus = minOf(free, openSteps)
            free -= toFocus
            val focused = steps.map { step ->
                val focus = !step.isDone && toFocus > 0
                if (focus) {
                    toFocus--
                }

                step.copy(focus = focus)
            }
            filled += task.copy(bin = TaskBin.SESSION, steps = focused)
        }

        return filled
    }

    // The live copy that lost keeps its title and note on the winner as "Changed on both devices" when they differ.
    private fun withLoserConflicts(winner: TaskItem, losers: List<Draft>): TaskItem {
        if (!winner.isLive) {
            return winner
        }

        val conflicts = ArrayList(winner.conflicts.orEmpty())
        for (loser in losers.map { it.task }.filter { it.isLive }) {
            if (loser.title != winner.title) {
                conflicts += TaskConflict(TaskConflict.TITLE, loser.title, FieldStamp.NOBODY, loser.editedAt)
            }

            if ((loser.note ?: "") != (winner.note ?: "")) {
                conflicts += TaskConflict(TaskConflict.NOTE, loser.note, FieldStamp.NOBODY, loser.editedAt)
            }
        }

        return if (conflicts.size == (winner.conflicts?.size ?: 0)) winner else winner.copy(conflicts = conflicts)
    }

    private fun stampsAt(at: OffsetDateTime): TaskFieldStamps = TaskFieldStamps.allAt(FieldStamp(at, FieldStamp.NOBODY))

    private fun fromNextTask(item: SyncNextTask, book: TaskLabelBook): TaskItem {
        val editedAt = time(item.editedAt)
        item.deletedAt?.let { return tombstone(item.id, TaskBin.QUEUED, item.position, editedAt, time(it), item.localTo) }
        return TaskItem(
            item.id.lowercase(), item.title, TaskBin.QUEUED, item.position, editedAt, editedAt, editedAt,
            labelId = book.idForName(item.category, item.categoryColor, editedAt),
            tags = TaskLabels.clean(item.labels),
            steps = stepsFrom(item.subtasks),
            estimateMinutes = item.estimateMinutes,
            note = item.note,
            doneAt = item.doneAt?.let(::time),
            devices = devicesOf(item.localTo),
            plannedFor = planOf(item.plannedFor),
        )
    }

    private fun fromLaterItem(item: SyncLaterItem, book: TaskLabelBook): TaskItem {
        val bin = if (item.bin == SyncLaterItem.NEXT_BIN) TaskBin.NEXT else TaskBin.LATER
        val editedAt = time(item.editedAt)
        item.deletedAt?.let { return tombstone(item.id, bin, item.position, editedAt, time(it), item.localTo) }
        return TaskItem(
            item.id.lowercase(), item.title, bin, item.position, editedAt, editedAt, editedAt,
            labelId = book.idForName(item.category, item.categoryColor, editedAt),
            tags = TaskLabels.clean(item.labels),
            steps = stepsFrom(item.subtasks),
            estimateMinutes = item.estimateMinutes,
            note = item.note,
            dueOn = dateOf(item.dueOn),
            doneAt = item.doneAt?.let(::time),
            devices = devicesOf(item.localTo),
            plannedFor = planOf(item.plannedFor),
        )
    }

    private fun fromParked(item: SyncParked, book: TaskLabelBook, zone: ZoneId): TaskItem {
        val editedAt = time(item.editedAt)
        val createdAt = time(item.createdAt)
        item.deletedAt?.let { return tombstone(item.id, TaskBin.SORT, item.position, editedAt, time(it), item.localTo).copy(createdAt = createdAt) }
        return TaskItem(
            item.id.lowercase(), item.text, TaskBin.SORT, item.position, createdAt, editedAt, editedAt,
            labelId = book.idForName(item.category, item.categoryColor, editedAt),
            tags = TaskLabels.clean(item.labels),
            origin = TaskOrigins.parked(createdAt, zone),
            doneAt = item.doneAt?.let(::time),
            devices = devicesOf(item.localTo),
        )
    }

    private fun fromAutoSaved(item: SyncAutoSavedItem, book: TaskLabelBook): TaskItem {
        val editedAt = time(item.editedAt)
        val savedAt = time(item.savedAt)
        item.deletedAt?.let { return tombstone(item.id, TaskBin.SORT, item.position, editedAt, time(it), item.localTo).copy(createdAt = savedAt) }
        // The day the shutdown card wrapped up; one the store can't read falls back to the day it was saved.
        val day = dateOf(item.wrapUpOf) ?: savedAt.toLocalDate()
        return TaskItem(
            item.id.lowercase(), item.title, TaskBin.SORT, item.position, savedAt, editedAt, editedAt,
            labelId = book.idForName(item.category, item.categoryColor, editedAt),
            tags = TaskLabels.clean(item.labels),
            origin = TaskOrigins.autoSaved(day),
            devices = devicesOf(item.localTo),
            autoSavedFor = day,
        )
    }

    private fun tombstone(id: String, bin: TaskBin, position: Double, editedAt: OffsetDateTime, deletedAt: OffsetDateTime, localTo: String?): TaskItem =
        TaskItem(id.lowercase(), "", bin, position, editedAt, editedAt, editedAt, devices = devicesOf(localTo), deletedAt = deletedAt)

    private fun devicesOf(localTo: String?): List<String>? = localTo?.let { listOf(it.lowercase()) }

    // The old subtasks as steps (none has a focus or an estimate yet); null for none.
    private fun stepsFrom(subtasks: List<SessionTaskItem>?): List<TaskStep>? =
        subtasks?.filter { it.id != null }?.takeIf { it.isNotEmpty() }?.map { TaskStep(it.id!!.lowercase(), it.title, it.doneAt?.let(::time)) }

    private fun planOf(slot: TaskPlannedFor?): TaskPlannedFor? = slot?.copy(partId = slot.partId.lowercase())

    private fun dateOf(text: String?): LocalDate? = text?.let {
        try {
            LocalDate.parse(it)
        } catch (e: DateTimeParseException) {
            null
        }
    }

    private fun views(input: TaskMigrationInput, book: TaskLabelBook, searches: MutableMap<String, String>): List<TaskView> {
        val views = TaskView.builtIns(UNSTAMPED_AT).toMutableList()
        var position = views.size.toDouble()
        for (preset in input.presets) {
            val id = preset.id.lowercase()
            if (views.any { it.id == id }) {
                continue
            }

            position++
            val label = preset.category?.let { book.named(it)?.id }
            views += TaskView(id, preset.name, null, TaskViewConfig(TaskViewFilter.NONE, labels = label?.let { listOf(it) }), position, UNSTAMPED_AT)
            if (preset.search.trim().isNotEmpty()) {
                searches[id] = preset.search
            }
        }

        return views
    }

    private fun localDate(at: OffsetDateTime, zone: ZoneId): LocalDate = at.atZoneSameInstant(zone).toLocalDate()

    private fun time(instant: Instant): OffsetDateTime = DotNetTime.cut(instant.atOffset(ZoneOffset.UTC))
}
