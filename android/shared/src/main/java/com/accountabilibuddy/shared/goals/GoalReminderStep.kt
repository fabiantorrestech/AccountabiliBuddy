package com.accountabilibuddy.shared.goals

import java.time.Instant

/**
 * What the phone does with its goal reminders now ([GoalReminderPlan.step]).
 *
 * @property ring Occurrences to post now, in ring order.
 * @property skip Keys to mark skipped ("Skip when the goal is already done" was true when they came due).
 * @property held Occurrences due now but held: a focus session runs (they ring once it ends) or the phone's guiding is paused.
 * @property nextAt The next moment one comes due (for the alarm); null when none is ahead today.
 */
data class GoalReminderStep(
    val ring: List<GoalReminderOccurrence>,
    val skip: List<String>,
    val held: List<GoalReminderOccurrence>,
    val nextAt: Instant?,
)
