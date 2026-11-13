package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.SharedListMerge
import java.time.OffsetDateTime

/**
 * Editing a task's steps from the open task on the phone (round 2 R7.3b; bundle spec §4 C and the step menu): add a step at the
 * end, take one out, move one up or down, and "Make it its own task". Every change is a stamped [TaskField.STEPS] edit through
 * [ShelfEdits] (a new task also carries every field stamped by this phone), and [ShelfChange.before] holds the task as it was, so
 * Undo ([ShelfEdits.restore] with [TaskField.STEPS]) puts its steps back. Pure, so the checks call it.
 */
object ShelfSteps {
    /** The longest step title kept (as the PC's add box keeps a title: a soft limit, cut only far past it). */
    const val MAX_TITLE = 500

    /** "Steps · 1/3", with "· add up to 35 min of 50" when the task has a time limit and its steps have times. */
    fun heading(task: TaskItem): String? {
        val steps = task.steps.orEmpty()
        if (steps.isEmpty()) return null
        val done = "Steps · ${steps.count { it.isDone }}/${steps.size}"
        val limit = task.estimateMinutes ?: return done
        val timed = steps.mapNotNull { it.estimateMinutes }
        return if (timed.isEmpty()) done else "$done · add up to ${timed.sum()} min of $limit"
    }

    /** A new step [title] (blank: nothing) at the end of the task [id]'s steps, with the id [stepId]. */
    fun add(lists: TaskLists, id: String, stepId: String, title: String, self: String, now: OffsetDateTime): ShelfChange {
        val trimmed = title.trim().take(MAX_TITLE)
        if (trimmed.isEmpty()) return ShelfChange(lists, emptyList(), emptyList())
        return ShelfEdits.edit(lists, id, TaskField.STEPS, self, now) { task -> task.copy(steps = task.steps.orEmpty() + TaskStep(stepId.lowercase(), trimmed)) }
    }

    /** The step [stepId] taken out of the task [id] (its last one leaves no steps). */
    fun remove(lists: TaskLists, id: String, stepId: String, self: String, now: OffsetDateTime): ShelfChange =
        ShelfEdits.edit(lists, id, TaskField.STEPS, self, now) { task ->
            val left = task.steps.orEmpty().filterNot { it.id.equals(stepId, ignoreCase = true) }
            if (left.size == task.steps.orEmpty().size) task else task.copy(steps = left.ifEmpty { null })
        }

    /** The step [stepId] one place up ([by] -1) or down (1); at an end nothing changes. */
    fun move(lists: TaskLists, id: String, stepId: String, by: Int, self: String, now: OffsetDateTime): ShelfChange =
        ShelfEdits.edit(lists, id, TaskField.STEPS, self, now) { task ->
            val steps = task.steps.orEmpty().toMutableList()
            val at = steps.indexOfFirst { it.id.equals(stepId, ignoreCase = true) }
            val to = at + by
            if (at < 0 || to < 0 || to >= steps.size) {
                task
            } else {
                steps.add(to, steps.removeAt(at))
                task.copy(steps = steps)
            }
        }

    /**
     * "Make it its own task": the step [stepId] leaves the task [id] and becomes the task [newId], right after it in the same bin, with
     * its label and tags (and the step's time as its time limit); a ticked step stays a step. A task in This session hands its step
     * to Queued (the new task would need room there). [ShelfChange.ids] holds the new task first, then the old one; [ShelfChange.before]
     * the old one as it was.
     */
    fun ownTask(lists: TaskLists, id: String, stepId: String, newId: String, self: String, now: OffsetDateTime): ShelfChange {
        val none = ShelfChange(lists, emptyList(), emptyList())
        val parent = lists.tasks.firstOrNull { it.id.equals(id, ignoreCase = true) && it.task.isLive }?.task ?: return none
        val step = parent.steps.orEmpty().firstOrNull { it.id.equals(stepId, ignoreCase = true) } ?: return none
        if (step.isDone || parent.isDone || lists.tasks.any { it.id.equals(newId, ignoreCase = true) }) return none
        val taken = remove(lists, parent.id, step.id, self, now)
        if (!taken.changed) return none
        val bin = if (parent.bin == TaskBin.SESSION) TaskBin.QUEUED else parent.bin
        // Right after its old task, before whatever followed it in that bin (at the end when it was in This session).
        val following = taken.lists.tasks.map { it.task }.filter { it.isLive && it.bin == bin && it.position > parent.position }.minOfOrNull { it.position }
        val position = if (bin == parent.bin) SharedListMerge.positionBetween(parent.position, following) else TaskListRules.endOf(taken.lists.tasks, bin)
        val task = TaskItem(
            newId.lowercase(), step.title, bin, position, now, now, now,
            labelId = parent.labelId, tags = parent.tags, origin = TaskOrigins.FROM_PHONE, estimateMinutes = step.estimateMinutes,
        )
        val item = SyncTaskItem(task, TaskFieldStamps.allAt(FieldStamp(now, self.lowercase())))
        return ShelfChange(taken.lists.copy(tasks = taken.lists.tasks + item), listOf(task.id, parent.id), listOf(parent))
    }

    /** "Made “…” its own task" (the toast after [ownTask]). */
    fun ownTaskText(title: String): String = "Made “${ShelfToasts.short(title)}” its own task"
}
