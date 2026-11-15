package com.accountabilibuddy.shared.session

import com.accountabilibuddy.shared.sync.SessionTaskItem
import java.time.Duration
import java.time.Instant

/**
 * A phone session's checklist as the rules need it (ADR 24, 25, R9): the tasks of This session (ids are GUID text), when the session
 * started, whether it had tasks then, and how many open tasks left the list while it ran (B6). The twin of Core's `SessionChecklist`
 * ("All tasks done"), parity-checked against the `ps1/end-at-once` fixture.
 *
 * @property droppedOpenIds The ids of those tasks (an Undo takes a drop back only for one of them).
 * @property droppedOpen Open tasks that left This session (removed, parked, moved out, here or by a received edit) since the start.
 * Above 0 the checklist is never "all done", so taking tasks off can't end a session at once: ticking is the only way.
 */
data class PhoneSessionChecklist(
    val tasks: List<SessionTaskItem> = emptyList(),
    val sessionStart: Instant? = null,
    val madeBeforeStart: Boolean = false,
    val droppedOpen: Int = 0,
    val droppedOpenIds: List<String> = emptyList(),
) {
    val hasTasks: Boolean get() = tasks.isNotEmpty()

    /**
     * Every task ticked and none taken off open (B6). Only each TASK's tick is looked at, not its steps: a ticked task with an open step
     * passes, as the PC's (owner question Q1154); the tick helpers below keep a task and its steps consistent.
     */
    val allDone: Boolean get() = hasTasks && droppedOpen == 0 && tasks.all { it.isDone }

    /** ADR 25: every task done, the checklist had tasks when the session started, and the session has run at least [END_AT_ONCE_AFTER]. */
    fun canEndAtOnce(now: Instant): Boolean {
        val start = sessionStart ?: return false
        return allDone && madeBeforeStart && Duration.between(start, now) >= END_AT_ONCE_AFTER
    }

    /** The checklist as its session starts at [at]. */
    fun started(at: Instant): PhoneSessionChecklist = copy(sessionStart = at, madeBeforeStart = hasTasks, droppedOpen = 0, droppedOpenIds = emptyList())

    /**
     * The checklist after the This session list changed to [newTasks] (the shared task model: a tick, an add, a removal, a park, a
     * received edit). Each task that was open here and is not in [newTasks] counts as dropped (B6), and its id is kept ([droppedOpenIds]); a
     * task that was done and left does not.
     */
    fun withTaskList(newTasks: List<SessionTaskItem>): PhoneSessionChecklist {
        val kept = newTasks.mapNotNull { it.id }.toSet()
        val gone = tasks.filter { !it.isDone && it.id != null && it.id !in kept }.mapNotNull { it.id }
        return copy(tasks = newTasks, droppedOpen = droppedOpen + gone.size, droppedOpenIds = (droppedOpenIds + gone).distinct())
    }

    /**
     * An Undo of a removed or parked task (the C# `WithOneLessDropped`): the list is [newTasks] again, and each task that is back and is one of the
     * DROPPED OPEN ones gives its drop back. Decided by the ids kept at the drop, never by a caller's flag, so an Undo of a ticked task, or a plain
     * add, can't give back a drop (V-N1 of the PS1b verify).
     */
    fun withUndone(newTasks: List<SessionTaskItem>): PhoneSessionChecklist {
        val back = newTasks.mapNotNull { it.id }.filter { it in droppedOpenIds }.toSet()
        return withTaskList(newTasks).let { it.copy(droppedOpen = maxOf(0, it.droppedOpen - back.size), droppedOpenIds = it.droppedOpenIds - back) }
    }

    /** Ticks the task [id] at [at], and all its open steps with it. */
    fun tickTask(id: String, at: Instant): PhoneSessionChecklist = withTask(id) { task ->
        task.copy(doneAt = task.doneAt ?: at, subtasks = task.subtasks.map { it.copy(doneAt = it.doneAt ?: at) })
    }

    /** Unticks the task [id] and all its steps. */
    fun untickTask(id: String): PhoneSessionChecklist = withTask(id) { task ->
        task.copy(doneAt = null, subtasks = task.subtasks.map { it.copy(doneAt = null) })
    }

    /** Ticks the step [stepId] at [at]; its task is done, at that time, when it was the last open step. */
    fun tickStep(stepId: String, at: Instant): PhoneSessionChecklist = withStep(stepId) { task ->
        val steps = task.subtasks.map { if (it.id == stepId) it.copy(doneAt = it.doneAt ?: at) else it }
        task.copy(subtasks = steps, doneAt = if (steps.all { it.isDone }) task.doneAt ?: at else task.doneAt)
    }

    /** Unticks the step [stepId]; its task is open again. */
    fun untickStep(stepId: String): PhoneSessionChecklist = withStep(stepId) { task ->
        task.copy(subtasks = task.subtasks.map { if (it.id == stepId) it.copy(doneAt = null) else it }, doneAt = null)
    }

    private fun withTask(id: String, change: (SessionTaskItem) -> SessionTaskItem): PhoneSessionChecklist =
        copy(tasks = tasks.map { if (it.id == id) change(it) else it })

    private fun withStep(stepId: String, change: (SessionTaskItem) -> SessionTaskItem): PhoneSessionChecklist =
        copy(tasks = tasks.map { task -> if (task.subtasks.any { it.id == stepId }) change(task) else task })

    companion object {
        /** How long a session runs before a finished checklist may end it at once (ADR 25). */
        val END_AT_ONCE_AFTER: Duration = Duration.ofMinutes(10)
    }
}
