package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import java.time.OffsetDateTime

/** What a "Changed on both devices" answer did: the new [lists], the words for the toast ([toast], empty when nothing changed) and the tasks it touched ([ids]; none: nothing changed). */
data class TaskConflictChange(val lists: TaskLists, val toast: String, val ids: List<String>) {
    val changed: Boolean get() = ids.isNotEmpty()
}

/**
 * The phone's answers to "Changed on both devices" (spec 05 section 11; the PC's `TaskActions.ResolveConflict` / `KeepBothVersions`, same
 * results in the fixtures `sync1a/keep-both.json`): the user keeps one version, or both as two tasks. Pure; the caller saves the lists and
 * forgets the task's earlier Undo entries (the answer is not undoable, as on the PC).
 */
object TaskConflictActions {
    /**
     * Keeps one version of a title or note that two devices changed ([conflict] on the task [id]): the shown one, or the other one
     * ([keepOther]). Stamped as made from both versions ([TaskEdits.resolve]), so the conflict closes on every device once synced. A blank other
     * title keeps the shown one. Nothing happens when the task or the conflict is gone.
     */
    fun resolveConflict(lists: TaskLists, id: String, conflict: TaskConflict, keepOther: Boolean, self: String, now: OffsetDateTime): TaskConflictChange {
        val index = lists.tasks.indexOfFirst { it.id.equals(id, ignoreCase = true) && it.task.isLive }
        if (index < 0 || lists.tasks[index].task.conflicts.orEmpty().none { it.sameAs(conflict) }) return none(lists)
        val item = lists.tasks[index]
        val isTitle = conflict.field == TaskConflict.TITLE
        val shown = if (isTitle) item.task.title else item.task.note
        val keptOther = keepOther && !(isTitle && conflict.otherValue.isNullOrBlank())
        val kept = if (keptOther) conflict.otherValue else shown
        val value = if (isTitle) kept?.trim() else cleanNote(kept)
        val tasks = lists.tasks.toMutableList()
        tasks[index] = TaskEdits.resolve(item, conflict, value, now, self)
        return TaskConflictChange(lists.copy(tasks = tasks), if (keptOther) "Kept the other version" else "Kept this version", listOf(item.id))
    }

    /**
     * "Keep both as separate tasks": the shown version stays on the task (the conflict closes as in [resolveConflict]) and the other version
     * becomes a new task [newId] at the end of Queued, with the task's label, tags, time limit and steps (new step ids from [newStepId], none in
     * focus), and on the same devices (a local or limited task's copy stays where the task is, ADR 72). Reminders, due date and highlight stay
     * with the original only, so nothing rings twice.
     */
    fun keepBothVersions(
        lists: TaskLists, id: String, conflict: TaskConflict, newId: String, self: String, now: OffsetDateTime, newStepId: (Int) -> String,
    ): TaskConflictChange {
        if (newId.isEmpty() || newId == EMPTY_ID || lists.tasks.any { it.id.equals(newId, ignoreCase = true) }) return none(lists)
        val resolved = resolveConflict(lists, id, conflict, false, self, now)
        if (!resolved.changed) return none(lists)
        val source = resolved.lists.tasks.firstOrNull { it.id.equals(id, ignoreCase = true) }?.task?.takeIf { it.isLive } ?: return none(lists)
        val isTitle = conflict.field == TaskConflict.TITLE
        val title = conflict.otherValue?.trim()?.takeIf { isTitle && it.isNotEmpty() } ?: source.title
        val note = if (isTitle) source.note else cleanNote(conflict.otherValue)
        val steps = source.steps?.mapIndexed { index, step -> step.copy(id = newStepId(index).lowercase(), focus = false) }?.ifEmpty { null }
        val at = DotNetTime.cut(now)
        val copy = TaskItem(
            newId.lowercase(), title, TaskBin.QUEUED, TaskListRules.endOf(resolved.lists, TaskBin.QUEUED), at, at, at,
            labelId = source.labelId,
            tags = source.tags,
            steps = steps,
            estimateMinutes = source.estimateMinutes,
            note = note,
            origin = source.number?.let { "Other version of ${it.value}" } ?: "Other version",
            devices = source.devices,
        )
        val tasks = resolved.lists.tasks + SyncTaskItem(copy, TaskFieldStamps.allAt(FieldStamp(at, self.lowercase())))
        return TaskConflictChange(resolved.lists.copy(tasks = tasks), "Kept both · the other copy is in Queued", listOf(id, newId))
    }

    // The note as the PC keeps it (SessionChecklist.CleanNote): trimmed, none when blank, cut to the note length.
    private fun cleanNote(note: String?): String? = note?.trim()?.take(ShelfEdits.MAX_NOTE_LENGTH)?.takeIf { it.isNotEmpty() }

    // The PC's Guid.Empty: a new id that names nothing.
    private const val EMPTY_ID = "00000000-0000-0000-0000-000000000000"

    private fun none(lists: TaskLists) = TaskConflictChange(lists, "", emptyList())
}
