package com.accountabilibuddy.shared.goals

import java.time.Instant

/**
 * One goal reminder occurrence's state on the phone (the PC's `GoalReminderMark`): ids and times only, no goal text; device-local,
 * never synced or backed up.
 *
 * @property at When it was marked (old marks are pruned).
 * @property snoozedUntil For [GoalReminderMarkKind.Snoozed]: when it rings again.
 */
data class GoalReminderMark(val kind: GoalReminderMarkKind, val at: Instant, val snoozedUntil: Instant? = null, val snoozeCount: Int = 0) {
    /** It doesn't ring again (dismissed, skipped or already shown). */
    val isHandled: Boolean get() = kind != GoalReminderMarkKind.Snoozed
}
