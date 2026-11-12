package com.accountabilibuddy.shared.goals

import java.time.Duration
import java.time.Instant

/**
 * Goal reminders' state on the phone (the PC's `GoalReminderState`): each occurrence's mark by its key ([GoalReminderPlan.reminderKey],
 * [GoalReminderPlan.nudgeKey], or the review's). Ids and times only. Marks older than [KEPT] are pruned on every change.
 */
data class GoalReminderState(val marks: Map<String, GoalReminderMark> = emptyMap()) {
    fun markOf(key: String): GoalReminderMark? = marks[key]

    /** The state with [key] marked, and marks older than [KEPT] (from [now]) dropped. */
    fun with(key: String, mark: GoalReminderMark, now: Instant): GoalReminderState {
        val kept = marks.filter { (k, v) -> k != key && Duration.between(v.at, now) <= KEPT }
        return GoalReminderState(kept + (key to mark))
    }

    companion object {
        /** How long a mark is kept (a snooze into the next day still finds its mark; a review week's marks last the week). */
        val KEPT: Duration = Duration.ofDays(8)

        val EMPTY = GoalReminderState()
    }
}
