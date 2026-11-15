package com.accountabilibuddy.shared.goals

import java.time.OffsetDateTime
import java.util.UUID

/**
 * One task that carries a goal (GS5, ADR 76 amended; the PC's `GoalTaskLink`): what the goal sources "a task carries this goal" and
 * "milestone tasks" and the goal page's task list read, taken from the task list ([GoalTaskLinks.from]) and never stored or synced on
 * its own.
 *
 * @property taskId The task's id (the id of its checklist task in a session).
 * @property goalId The goal it carries (a goal that may no longer exist: nothing counts for it then).
 * @property title The task's title, for the goal page's list only. Private text: never logged, never stored here.
 * @property doneAt When it was finished; null while open.
 */
data class GoalTaskLink(val taskId: UUID, val goalId: UUID, val title: String, val doneAt: OffsetDateTime?) {
    val isDone: Boolean get() = doneAt != null
}
