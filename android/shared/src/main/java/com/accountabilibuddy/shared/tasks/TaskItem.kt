package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.TaskPlannedFor
import kotlinx.serialization.json.JsonObject
import java.time.LocalDate
import java.time.LocalTime
import java.time.OffsetDateTime

/**
 * A task in the one shared task list (ADR 76; the PC's TaskItem, field for field): every task the user has sits in one [TaskBin].
 * The text (title, steps, note, tags, origin, the other version of a conflict) is the user's private text: sealed wherever it is
 * stored or synced, never logged. The phone carries every field the PC writes, also the ones it has no screen for (calendar, the
 * planned day part, conflicts, numbers, devices), so a task read from the PC goes back as it came.
 *
 * @property position Its place within its bin: lower first.
 * @property editedAt Its newest change of any field: the shared lists' item stamp (ADR 48).
 * @property touchedAt Any edit, move, step tick or opening its details.
 * @property number Its number; null until one is given. [formerNumbers]: temporary numbers it had before.
 * @property prevBin Where it was before [TaskBin.DONE].
 * @property labelId Its label (at most one); null for "No label".
 * @property tags Its tags (ADR 72's labels, renamed).
 * @property effort Reserved ("Coming soon"): never set yet; a merge clears it, as the PC's does.
 * @property dueOn "Done by" date; [dueAt] its time, null for the whole day.
 * @property devices The devices it syncs to (ids, lower case); null for all. A single one is ADR 72's local item kept on that device.
 * @property calendar Its reminders and due date go to the calendar.
 * @property plannedFor The day part it was put in the plan for (ADR 50).
 * @property deletedAt Set on a tombstone, which keeps no text.
 * @property conflicts "Changed on both devices" versions waiting for the user's pick.
 * @property autoSavedFor For a To sort task that came from Auto-saved: the plan day its shutdown card wrapped up.
 * @property extra Fields a newer app wrote that this version doesn't know ([TaskUnknown]).
 */
data class TaskItem(
    val id: String,
    val title: String,
    val bin: TaskBin,
    val position: Double,
    val createdAt: OffsetDateTime,
    val editedAt: OffsetDateTime,
    val touchedAt: OffsetDateTime,
    val number: TaskNumber? = null,
    val formerNumbers: List<Int>? = null,
    val prevBin: TaskBin? = null,
    val labelId: String? = null,
    val tags: List<String>? = null,
    val steps: List<TaskStep>? = null,
    val estimateMinutes: Int? = null,
    val effort: Int? = null,
    val note: String? = null,
    val dueOn: LocalDate? = null,
    val dueAt: LocalTime? = null,
    val reminders: List<TaskReminder>? = null,
    val cantMiss: Boolean = false,
    val highlight: Boolean = false,
    val origin: String? = null,
    val doneAt: OffsetDateTime? = null,
    val devices: List<String>? = null,
    val calendar: Boolean = false,
    val plannedFor: TaskPlannedFor? = null,
    val deletedAt: OffsetDateTime? = null,
    val conflicts: List<TaskConflict>? = null,
    val autoSavedFor: LocalDate? = null,
    val extra: JsonObject = TaskUnknown.NONE,
) {
    /** Neither a tombstone nor a local item's stub. */
    val isLive: Boolean get() = deletedAt == null

    val isDone: Boolean get() = doneAt != null

    /** The one device that keeps it, when [devices] names exactly one (ADR 72's local item); null otherwise. */
    val localTo: String? get() = devices?.singleOrNull()

    /** How many of the session's places it takes: one per focused open step, or one for a task with none focused. */
    val actionCount: Int
        get() {
            val focused = steps.orEmpty().count { it.focus && !it.isDone }
            return if (focused > 0) focused else 1
        }

    /**
     * The task as a tombstone: every text and user field gone, deleted at [at]. It keeps its bin, previous bin and [autoSavedFor]
     * (so an old sync field knows which list to delete it from), its creation time and its devices.
     */
    fun deleted(at: OffsetDateTime): TaskItem =
        TaskItem(id, "", bin, position, createdAt, at, at, prevBin = prevBin, devices = devices, deletedAt = at, autoSavedFor = autoSavedFor)
}
