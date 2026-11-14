package com.accountabilibuddy.shared.sync

import java.time.Duration
import java.time.Instant

// ADR 72: every task list syncs. The phone's twins of the PC's SyncAutoSavedItem, SyncPastSettled, SyncTaskHistoryEntry,
// SyncKeptPast, SyncHandover and SyncHandoverAck (same JSON; older apps ignore all of it).

/** An Auto-saved item: a shutdown card's "tomorrow's first task", shared item by item; gone 30 days after [savedAt] on every device. */
data class SyncAutoSavedItem(
    override val id: String,
    val title: String,
    val wrapUpOf: String,
    val savedAt: Instant,
    override val editedAt: Instant,
    override val deletedAt: Instant?,
    override val position: Double,
    val category: String? = null,
    val categoryColor: String? = null,
    val labels: List<String>? = null,
    override val localTo: String? = null,
) : SharedItem {
    override val tieBreak: String
        get() = (listOf(title, wrapUpOf, savedAt.ticks().toString(), category ?: "", categoryColor ?: "") + TaskLabels.tieExtras(labels, localTo))
            .joinToString("\u0001")

    fun isExpired(now: Instant): Boolean = Duration.between(savedAt, now) >= KEPT

    override fun withStamps(editedAt: Instant, deletedAt: Instant?): SyncAutoSavedItem = copy(editedAt = editedAt, deletedAt = deletedAt)

    override fun asLocalStub(): SyncAutoSavedItem = deleted(editedAt).copy(localTo = localTo)

    fun deleted(at: Instant): SyncAutoSavedItem = copy(title = "", category = null, categoryColor = null, labels = null, localTo = null, editedAt = at, deletedAt = at)

    companion object {
        val KEPT: Duration = Duration.ofDays(30)
    }
}

/** What a device did with a past session's open task ([id] is the task's own id): moved, done, open, deleted or local. Newest edit wins. */
data class SyncPastSettled(
    override val id: String,
    val state: String,
    override val editedAt: Instant,
    override val deletedAt: Instant? = null,
    override val position: Double = 0.0,
    override val localTo: String? = null,
) : SharedItem {
    val hidesTask: Boolean get() = state == MOVED || state == DONE || state == DELETED || state == LOCAL

    override val tieBreak: String get() = (listOf(state) + TaskLabels.tieExtras(null, localTo)).joinToString("\u0001")

    override fun withStamps(editedAt: Instant, deletedAt: Instant?): SyncPastSettled = copy(editedAt = editedAt, deletedAt = deletedAt)

    companion object {
        const val MOVED = "moved"
        const val DONE = "done"
        const val OPEN = "open"
        const val DELETED = "deleted"
        const val LOCAL = "local"
        val KEPT: Duration = Duration.ofDays(90)
    }
}

/** A task of a PC's history as its file carries it (ADR 72): no session note or reason ever; a local one is a stub (empty title, [localTo]). */
data class SyncHistoryTask(
    val id: String,
    val title: String,
    val doneAt: Instant?,
    val subtasks: List<SessionTaskItem>,
    val category: String? = null,
    val categoryColor: String? = null,
    val estimateMinutes: Int? = null,
    val note: String? = null,
    val labels: List<String>? = null,
    val localTo: String? = null,
) {
    val isDone: Boolean get() = doneAt != null
}

/** One checklist of a PC's 90-day task history: a session's tasks ([sessionStart]), or a day's tasks ticked outside any session. */
data class SyncTaskHistoryEntry(
    val sessionStart: Instant?,
    val tasks: List<SyncHistoryTask>,
    val outside: Boolean = false,
    /** AR1b review: past tasks of this session its owner deleted in the last 30 days (optional; an older app ignores it). */
    val deleted: List<SyncDeletedPastTask>? = null,
) {
    val at: Instant? get() = sessionStart ?: tasks.mapNotNull { it.doneAt }.minOrNull()
}

/**
 * A past task its owning PC deleted in the last 30 days, which the owner keeps sending in [SyncTaskHistoryEntry.deleted] so every device can
 * list it in Recently deleted and Restore it (a newer settle record). The PC's twin is `SyncDeletedPastTask`.
 */
data class SyncDeletedPastTask(val task: SyncHistoryTask, val deletedAt: Instant)

/** A past task kept on one device as a local item, with the PC it came from. */
data class SyncKeptPast(val sourceDeviceId: String, val sourceName: String, val sessionStart: Instant?, val task: SyncHistoryTask, val editedAt: Instant)

/** An item on its way to the device chosen to keep it; exactly one item field is set, the one [list] names. */
data class SyncHandover(
    val to: String,
    val id: String,
    val list: String,
    val editedAt: Instant,
    val sentAt: Instant,
    val nextTask: SyncNextTask? = null,
    val laterItem: SyncLaterItem? = null,
    val parkedItem: SyncParked? = null,
    val autoSavedItem: SyncAutoSavedItem? = null,
    val pastTask: SyncKeptPast? = null,
) {
    companion object {
        const val NEXT_TASKS = "nextTasks"
        const val LATER = "later"
        const val PARKED = "parked"
        const val AUTO_SAVED = "autoSaved"
        const val HISTORY = "history"
        val KEPT: Duration = Duration.ofDays(30)
    }
}

/** The keeper's receipt for a handover it took in. */
data class SyncHandoverAck(val id: String, val editedAt: Instant)
