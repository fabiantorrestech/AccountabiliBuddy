package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.tasks.TaskHistoryKind
import com.accountabilibuddy.shared.tasks.TaskLists
import java.util.UUID

/**
 * Reads the goal links off the task list (GS5; the PC's `GoalTaskLinks`): every live task with a goal id, plus the tasks History still
 * holds as finished (90 days), so a milestone keeps counting a task once it left the list. A task in both counts once (the list's copy).
 * A goal id no live goal has is kept here; whoever shows the link ignores it. An id that isn't a guid is left out.
 */
object GoalTaskLinks {
    fun from(lists: TaskLists): List<GoalTaskLink> {
        val links = ArrayList<GoalTaskLink>()
        val seen = HashSet<String>()
        for (item in lists.tasks) {
            val task = item.task
            if (!task.isLive) continue
            seen += task.id.lowercase()
            link(task.id, task.goalId, task.title, task.doneAt)?.let { links += it }
        }

        for (item in lists.log) {
            val entry = item.entry
            if (!entry.isLive || entry.kind != TaskHistoryKind.FINISHED) continue
            val task = entry.task
            if (task.goalId != null && seen.add(task.id.lowercase())) {
                link(task.id, task.goalId, task.title, task.doneAt ?: entry.at)?.let { links += it }
            }
        }

        return links
    }

    /** The goals a task can be linked to (the PC's `LinkableGoals`): the live goals in their order, never Deep work (it counts all focus). A paused goal can be linked. */
    fun linkable(lists: GoalLists): List<Goal> = lists.liveGoals.filter { !it.isDeepWork }

    /** The linkable goal a task's goal id names, or null for none: a link to a goal that no longer exists shows as none (the id stays on the task). */
    fun linkedGoal(goals: List<Goal>, goalId: String?): Goal? =
        goalId?.let { id -> goals.firstOrNull { it.id.toString().equals(id, ignoreCase = true) } }

    private fun link(taskId: String, goalId: String?, title: String, doneAt: java.time.OffsetDateTime?): GoalTaskLink? {
        if (goalId == null) return null
        return runCatching { GoalTaskLink(UUID.fromString(taskId), UUID.fromString(goalId), title, doneAt) }.getOrNull()
    }
}
