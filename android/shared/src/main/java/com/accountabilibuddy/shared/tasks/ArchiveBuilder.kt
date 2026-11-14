package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.SyncDeletedPastTask
import com.accountabilibuddy.shared.sync.SyncHistoryTask
import com.accountabilibuddy.shared.sync.SyncKeptPast
import com.accountabilibuddy.shared.sync.SyncPastSettled
import com.accountabilibuddy.shared.sync.SyncSessionNote
import com.accountabilibuddy.shared.sync.SyncTaskHistoryEntry
import java.time.Duration
import java.time.Instant

/** Where an Archive row came from (the PC's ArchiveSource). */
enum class ArchiveSource { OWN, KEPT, RECEIVED, TASK_LIST }

// The PC's Guid.Empty: a step without an id, a note without a writing device, an input without a self id.
internal const val EMPTY_ID = "00000000-0000-0000-0000-000000000000"

/** A step of an Archive task, after the settle records are applied. */
data class ArchiveStepRow(val id: String, val title: String, val doneAt: Instant?)

/** One other PC's task history as its last sync file carried it. */
data class ArchiveReceivedDevice(val deviceId: String, val name: String, val entries: List<SyncTaskHistoryEntry>)

/** Everything [ArchiveBuilder] builds the Archive's tasks from; the same on every device. */
data class ArchiveInput(
    val own: List<SyncTaskHistoryEntry>,
    val received: List<ArchiveReceivedDevice>,
    val kept: List<SyncKeptPast>,
    val settled: List<SyncPastSettled>,
    val lists: TaskLists,
    val now: Instant,
    /** Tasks of the running session's checklist: one ticked there isn't "From your task list" until its session ends. */
    val running: Set<String> = emptySet(),
    /** AR1c: every device's session notes (ADR 87) as the notes store holds them, tombstones included. */
    val notes: List<SyncSessionNote> = emptyList(),
    /** This device's id: a note by it (or by none yet) isn't marked "from". */
    val self: String = EMPTY_ID,
    /** The last known name of each other device by id (lower case); an id missing here reads "another device". */
    val deviceNames: Map<String, String> = emptyMap(),
)

/** One task of the Archive (done, or left open when its session ended); holds the user's task text: shown, never logged. */
data class ArchiveTaskRow(
    val id: String,
    val title: String,
    val doneAt: Instant?,
    val sessionStart: Instant?,
    val outside: Boolean,
    val source: ArchiveSource,
    val deviceId: String?,
    val deviceName: String?,
    val categoryId: String?,
    val category: String?,
    val categoryColor: String?,
    val steps: List<ArchiveStepRow>,
    val sort: Instant,
    val canTick: Boolean,
    val canDelete: Boolean,
    val canEdit: Boolean,
) {
    val isDone: Boolean get() = doneAt != null
}

/** One line of the Archive's Recently deleted that every device can build; [entryId] is the History entry "Bring back" revives. */
data class ArchiveDeletedRow(
    val id: String,
    val title: String,
    val deletedAt: Instant,
    val source: ArchiveSource,
    val deviceName: String?,
    val entryId: String?,
    /** A past task's settle state for Restore: DONE when it was done when deleted, else OPEN; null for a History entry's row. */
    val restoreAs: String? = null,
) {
    val canRestore: Boolean get() = entryId != null

    /** Restore stamps a newer settle record over the delete (never a raw restore). */
    val canRestoreByStamp: Boolean get() = entryId == null && restoreAs != null
}

/**
 * One session note of the Archive (AR1b, ADR 87; the PC's `ArchiveNoteRow`), the same on every device. The note's text is the user's private
 * text: shown, never logged.
 */
data class ArchiveNoteRow(val id: String, val text: String, val at: Instant, val by: String, val deviceName: String?, val fromOther: Boolean) {
    /** The words after the time: "from Work PC", "from another device" (a removed device, or one never named), or null for this device's own note. */
    val fromText: String? get() = if (fromOther) "from ${deviceName ?: "another device"}" else null
}

/** What [ArchiveBuilder] makes: the Archive's tasks, its synced Recently deleted lines (newest first) and every device's notes (newest first). */
data class ArchiveRows(val tasks: List<ArchiveTaskRow>, val deleted: List<ArchiveDeletedRow>, val notes: List<ArchiveNoteRow> = emptyList())

/**
 * The Archive's tasks, pure: the phone's twin of the PC's Core ArchiveBuilder (AR1-fix), so both build the same rows from the same
 * synced data. Sources, in the order that wins when one task id is in several: this device's own history, past tasks kept here, other
 * PCs' histories, then the one task list's finished tasks. Each id shows once; ADR 72's settle records are laid over the own and
 * received rows; a task deleted from the task list leaves the Archive and is listed in Recently deleted, as is a past task deleted
 * anywhere. Received and kept rows can be ticked and deleted, never renamed. Task text is shown, never logged.
 */
object ArchiveBuilder {
    val DELETED_LISTED: Duration = Duration.ofDays(30)
    val LISTED: Duration = Duration.ofDays(90)

    // DateTimeOffset's default (0001-01-01), which the PC uses when a record has no time at all.
    // The PC's Guid.Empty, for a step without an id.

    private val DEFAULT: Instant = Instant.parse("0001-01-01T00:00:00Z")

    private class Finished(val task: TaskItem, val at: Instant)

    fun build(input: ArchiveInput): ArchiveRows {
        val settled = latestRecords(input.settled)
        val deletedInList = deletedInList(input.lists)
        val finished = finishedInList(input.lists)
        val seen = HashSet<String>()
        val rows = ArrayList<ArchiveTaskRow>()
        val settledDeleted = ArrayList<ArchiveDeletedRow>()

        fun past(source: ArchiveSource, deviceId: String?, deviceName: String?, sessionStart: Instant?, outside: Boolean, start: Instant, task: SyncHistoryTask, settleApplies: Boolean) {
            if (task.title.isEmpty() || !seen.add(task.id)) return
            val record = if (settleApplies) settled[task.id] else null
            if (record?.state == SyncPastSettled.DELETED) {
                if (Duration.between(record.editedAt, input.now) < DELETED_LISTED) {
                    settledDeleted.add(ArchiveDeletedRow(task.id, task.title, record.editedAt, source, deviceName, null, restoreStateOf(task)))
                }
                return
            }

            val deletedAt = deletedInList[task.id]
            if (deletedAt != null && (task.doneAt == null || deletedAt >= task.doneAt)) return

            var doneAt = task.doneAt
            var steps = task.subtasks.map { ArchiveStepRow(it.id ?: EMPTY_ID, it.title, it.doneAt) }
            // Own copy already carries every tick (settle records are applied to it): an own row takes only a delete. A received copy may
            // be older than a tick made since: the record settles the task as a whole when the copy disagrees, never its steps.
            if (source == ArchiveSource.RECEIVED && record?.state == SyncPastSettled.DONE && doneAt == null) {
                doneAt = record.editedAt
            } else if (source == ArchiveSource.RECEIVED && record?.state == SyncPastSettled.OPEN) {
                doneAt = null
            }

            var title = task.title
            var categoryId: String? = null
            var ownsText = source == ArchiveSource.OWN
            // A "done outside a session" record is a per-PC copy of a task that lives in the one task list: that list's text and ticks are the shared truth.
            val live = if (outside) finished[task.id] else null
            if (live != null) {
                title = live.task.title
                doneAt = live.at
                categoryId = live.task.labelId ?: categoryId
                steps = live.task.steps.orEmpty().map { ArchiveStepRow(it.id, it.title, it.doneAt?.toInstant()) }
                ownsText = false
            }

            rows.add(
                ArchiveTaskRow(
                    task.id, title, doneAt, sessionStart, outside, source, deviceId, deviceName,
                    categoryId, task.category, task.categoryColor, steps, doneAt ?: start,
                    canTick = true, canDelete = true, canEdit = ownsText,
                ),
            )
        }

        for (entry in input.own.filter { it.sessionStart != null || it.outside }) {
            val start = startOf(entry)
            for (task in entry.tasks) past(ArchiveSource.OWN, null, null, entry.sessionStart, entry.outside, start, task, true)
        }

        for (item in input.kept.sortedWith(compareBy<SyncKeptPast> { it.task.id })) {
            past(ArchiveSource.KEPT, item.sourceDeviceId, item.sourceName, item.sessionStart, false, item.sessionStart ?: item.task.doneAt ?: DEFAULT, item.task, false)
        }

        for (device in input.received.sortedWith(compareBy<ArchiveReceivedDevice> { it.deviceId })) {
            for (entry in device.entries) {
                val start = startOf(entry)
                for (task in entry.tasks.filter { it.localTo == null }) {
                    past(ArchiveSource.RECEIVED, device.deviceId, device.name, entry.sessionStart, entry.outside, start, task, true)
                }
            }
        }

        for ((id, item) in finished.entries.sortedWith(compareBy<Map.Entry<String, Finished>> { it.key })) {
            if (Duration.between(item.at, input.now) >= LISTED || id in input.running || !seen.add(id)) continue
            rows.add(
                ArchiveTaskRow(
                    id, item.task.title, item.at, null, false, ArchiveSource.TASK_LIST, null, null,
                    item.task.labelId, null, null,
                    item.task.steps.orEmpty().map { ArchiveStepRow(it.id, it.title, it.doneAt?.toInstant()) },
                    item.at, canTick = false, canDelete = false, canEdit = false,
                ),
            )
        }

        // A past task its owner deleted and keeps sending as deleted for 30 days (AR1b review, SyncTaskHistoryEntry.deleted): listed on every
        // device that receives the owner's file, with the settle record's time, so the same line shows everywhere (restore = a newer record).
        // Once a newer Done or Open record restored it, it is that task again at once, before the owner's next file carries it.
        for (device in input.received.sortedWith(compareBy<ArchiveReceivedDevice> { it.deviceId })) {
            for (entry in device.entries) {
                for (gone in entry.deleted.orEmpty().sortedWith(compareBy<SyncDeletedPastTask> { it.task.id })) {
                    val record = settled[gone.task.id]
                    if (gone.task.title.isEmpty() || gone.task.id in seen || record == null) continue
                    if (record.state == SyncPastSettled.DELETED) {
                        if (Duration.between(record.editedAt, input.now) < DELETED_LISTED && seen.add(gone.task.id)) {
                            settledDeleted.add(ArchiveDeletedRow(gone.task.id, gone.task.title, record.editedAt, ArchiveSource.RECEIVED, device.name, null, restoreStateOf(gone.task)))
                        }
                    } else if (record.state == SyncPastSettled.OPEN || record.state == SyncPastSettled.DONE) {
                        past(ArchiveSource.RECEIVED, device.deviceId, device.name, entry.sessionStart, entry.outside, startOf(entry), gone.task, true)
                    }
                }
            }
        }

        val deleted = deletedFromList(input.lists, input.now).toMutableList()
        val listed = deleted.map { it.id }.toMutableSet()
        deleted.addAll(settledDeleted.filter { listed.add(it.id) })
        return ArchiveRows(
            rows.sortedWith(compareByDescending<ArchiveTaskRow> { it.sort }.thenBy { it.id }),
            deleted.sortedWith(compareByDescending<ArchiveDeletedRow> { it.deletedAt }.thenBy { it.id }),
            notes(input),
        )
    }

    // The session notes of every device (ADR 87), newest first: each one from another device says "from <device>" (a removed device's notes
    // stay, with its last known name, or "another device" when none is known); this device's own are unmarked.
    private fun notes(input: ArchiveInput): List<ArchiveNoteRow> {
        val names = input.deviceNames.mapKeys { it.key.lowercase() }
        return input.notes
            .filter { it.deletedAt == null && !it.text.isNullOrBlank() }
            .map { note ->
                val other = note.by != EMPTY_ID && !note.by.equals(input.self, ignoreCase = true)
                val name = if (other) names[note.by.lowercase()]?.takeIf { it.isNotBlank() } else null
                ArchiveNoteRow(note.id, note.text!!, note.at, note.by, name, other)
            }
            .sortedWith(compareByDescending<ArchiveNoteRow> { it.at }.thenBy { it.id })
    }

    /** The state Restore stamps for a deleted past task: DONE when it was done, else OPEN. */
    fun restoreStateOf(task: SyncHistoryTask): String = if (task.doneAt == null) SyncPastSettled.OPEN else SyncPastSettled.DONE

    // A session's start; a "done outside a session" record has none, so its latest tick stands in.
    private fun startOf(entry: SyncTaskHistoryEntry): Instant =
        entry.sessionStart ?: entry.tasks.mapNotNull { it.doneAt }.maxOrNull() ?: DEFAULT

    // The newest live record per past task.
    private fun latestRecords(records: List<SyncPastSettled>): Map<String, SyncPastSettled> {
        val latest = HashMap<String, SyncPastSettled>()
        for (record in records.filter { it.deletedAt == null }) {
            val held = latest[record.id]
            if (held == null || record.editedAt > held.editedAt) latest[record.id] = record
        }
        return latest
    }

    private fun deletedInList(lists: TaskLists): Map<String, Instant> =
        lists.log.map { it.entry }
            .filter { it.isLive && it.kind == TaskHistoryKind.DELETED }
            .groupBy { it.task.id }
            .mapValues { (_, entries) -> entries.maxOf { it.at.toInstant() } }

    // The tasks finished in the one task list: the live tasks with a done time first, then History's finished entries.
    private fun finishedInList(lists: TaskLists): Map<String, Finished> {
        val finished = HashMap<String, Finished>()
        val liveOpen = lists.tasks.filter { it.task.isLive && it.task.doneAt == null }.map { it.id }.toSet()
        for (item in lists.tasks.filter { it.task.isLive && it.task.doneAt != null }) {
            finished[item.id] = Finished(item.task, item.task.doneAt!!.toInstant())
        }
        for (entry in lists.log.map { it.entry }.filter { it.isLive && it.kind == TaskHistoryKind.FINISHED }) {
            if (!finished.containsKey(entry.task.id) && !liveOpen.contains(entry.task.id)) {
                finished[entry.task.id] = Finished(entry.task, (entry.task.doneAt ?: entry.at).toInstant())
            }
        }
        return finished
    }

    private fun deletedFromList(lists: TaskLists, now: Instant): List<ArchiveDeletedRow> =
        lists.log.map { it.entry }
            .filter { it.isLive && it.kind == TaskHistoryKind.DELETED && Duration.between(it.at.toInstant(), now) < DELETED_LISTED && it.task.title.isNotEmpty() }
            .map { ArchiveDeletedRow(it.task.id, it.task.title, it.at.toInstant(), ArchiveSource.TASK_LIST, null, it.id) }
}
