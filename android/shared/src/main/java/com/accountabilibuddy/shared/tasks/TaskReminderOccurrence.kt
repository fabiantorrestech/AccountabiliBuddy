package com.accountabilibuddy.shared.tasks

import kotlinx.serialization.json.JsonObject
import java.time.OffsetDateTime

/**
 * The state of one occurrence of a [TaskReminder] (ADR 76; the PC's TaskReminderOccurrence).
 *
 * @property due When this occurrence rings (before any snooze).
 * @property snoozedUntil When a snoozed occurrence rings again; null otherwise.
 * @property snoozeCount How many times it was snoozed.
 */
data class TaskReminderOccurrence(
    val due: OffsetDateTime,
    val status: TaskReminderStatus,
    val snoozedUntil: OffsetDateTime? = null,
    val snoozeCount: Int = 0,
    val extra: JsonObject = TaskUnknown.NONE,
)
