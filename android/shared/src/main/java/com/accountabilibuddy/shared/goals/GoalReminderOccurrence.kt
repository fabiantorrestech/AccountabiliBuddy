package com.accountabilibuddy.shared.goals

import java.time.Instant
import java.time.OffsetDateTime
import java.util.UUID

/**
 * One goal reminder occurrence the phone may ring ([GoalReminderPlan]): a reminder's on a day, or the morning-after nudge.
 *
 * @property reminderId The reminder's id; the goal's id for a nudge.
 * @property due When it was due; [ringAt] is that or a snooze's end.
 * @property key Its mark key in [GoalReminderState].
 */
data class GoalReminderOccurrence(
    val goalId: UUID,
    val reminderId: UUID,
    val nudge: Boolean,
    val due: OffsetDateTime,
    val ringAt: Instant,
    val key: String,
    val snoozeCount: Int = 0,
)
