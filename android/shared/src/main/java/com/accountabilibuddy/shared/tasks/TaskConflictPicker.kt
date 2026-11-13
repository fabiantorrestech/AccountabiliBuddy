package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import java.time.OffsetDateTime

/** One task "Changed on both devices" in the conflicts list (SYNC1c). The title is the user's own text: shown on screen only, never logged or exported. */
data class TaskConflictRow(val id: String, val title: String, val count: Int)

/**
 * One version in the picker ([TaskConflictPicker.view]): its [text] ("" for a removed note), the device that made it ([deviceId], null for an
 * earlier copy), when ([at]) and whether it is the one the task shows now ([shown]).
 */
data class TaskConflictVersion(val text: String?, val deviceId: String?, val at: OffsetDateTime, val shown: Boolean, val isNewer: Boolean)

/** The picker for one conflict: [head] ("Keep one version" / "Keep one version of the note"), "1 of 2" in [countText] when the title and the note both wait, and the two [versions], the newer first. */
data class TaskConflictView(val conflict: TaskConflict, val taskTitle: String, val head: String, val countText: String, val versions: List<TaskConflictVersion>)

/** What the user picks: this version (the one shown), the other one, or both as two tasks (the other in Queued). */
enum class TaskConflictPick { KEEP_THIS, KEEP_OTHER, KEEP_BOTH }

/**
 * The phone's "Changed on both devices" picker (SYNC1c; spec 05 section 11, the PC's `ConflictDialogViewModel` and `TaskActions`): the list of
 * tasks that wait, the picker of the task's first open conflict (the title before the note, the newer version first and marked), and the answers
 * through [TaskConflictActions] (the same results as the PC, in the fixtures `sync1a/keep-both.json`). Pure; the caller saves the lists. An
 * answer is not undoable (an Undo would bring the old text back without the choice, so the other version would be lost): no Undo is offered, as
 * on the PC. Titles and notes are shown only, never logged.
 */
object TaskConflictPicker {
    /** Every live task with a conflict, newest edit first. */
    fun rows(lists: TaskLists): List<TaskConflictRow> =
        lists.tasks.filter { it.task.isLive && it.task.conflicts.orEmpty().isNotEmpty() }
            .sortedWith { a, b -> DotNetTime.compare(b.task.editedAt, a.task.editedAt) }
            .map { TaskConflictRow(it.id, it.task.title, it.task.conflicts.orEmpty().size) }

    /**
     * The picker for the task [id]'s first open conflict; [picked] answers already given in this go (for "2 of 2"). Null when the task left the
     * list or the conflict closed.
     */
    fun view(lists: TaskLists, id: String, picked: Int = 0): TaskConflictView? {
        val item = lists.tasks.firstOrNull { it.id.equals(id, ignoreCase = true) && it.task.isLive } ?: return null
        val open = item.task.conflicts.orEmpty()
            .filter { it.field == TaskConflict.TITLE || it.field == TaskConflict.NOTE }
            .sortedBy { if (it.field == TaskConflict.TITLE) 0 else 1 }
        val conflict = open.firstOrNull() ?: return null
        val isTitle = conflict.field == TaskConflict.TITLE
        val field = if (isTitle) TaskField.TITLE else TaskField.NOTE
        val shownText = if (isTitle) item.task.title else item.task.note
        val stamp = item.stamps.of(field)
        val shownAt = stamp?.at ?: item.task.editedAt
        val shownIsNewer = DotNetTime.compare(shownAt, conflict.otherEditedAt) >= 0
        val shown = TaskConflictVersion(shownText, stamp?.by?.takeIf { it.isNotEmpty() && it != EMPTY_ID }, shownAt, true, shownIsNewer)
        val other = TaskConflictVersion(conflict.otherValue, conflict.otherDeviceId.takeIf { it.isNotEmpty() && it != EMPTY_ID }, conflict.otherEditedAt, false, !shownIsNewer)
        val total = picked + open.size
        return TaskConflictView(
            conflict, item.task.title, if (isTitle) "Keep one version" else "Keep one version of the note",
            if (total > 1) "${picked + 1} of $total" else "", if (shownIsNewer) listOf(shown, other) else listOf(other, shown),
        )
    }

    /**
     * The answer: keep this version, keep the other, or keep both as two tasks (the other in Queued as the new task [newId], its steps given ids by
     * [newStepId]). [TaskConflictChange.changed] is false when the task or the conflict is gone.
     */
    fun answer(
        lists: TaskLists, id: String, conflict: TaskConflict, pick: TaskConflictPick, newId: String, self: String, now: OffsetDateTime, newStepId: (Int) -> String,
    ): TaskConflictChange = when (pick) {
        TaskConflictPick.KEEP_THIS -> TaskConflictActions.resolveConflict(lists, id, conflict, false, self, now)
        TaskConflictPick.KEEP_OTHER -> TaskConflictActions.resolveConflict(lists, id, conflict, true, self, now)
        TaskConflictPick.KEEP_BOTH -> TaskConflictActions.keepBothVersions(lists, id, conflict, newId, self, now, newStepId)
    }

    private const val EMPTY_ID = "00000000-0000-0000-0000-000000000000"
}
