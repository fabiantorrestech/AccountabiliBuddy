package com.accountabilibuddy.shared.tasks

import kotlinx.serialization.json.JsonObject
import java.time.OffsetDateTime

/**
 * One of a task's reminders (ADR 76; the PC's TaskReminder), at most [MAX_PER_TASK].
 *
 * @property rule One of [AT_START], [TEN_MINUTES_BEFORE], [ONE_HOUR_BEFORE], [DAY_BEFORE_AT_NINE] (relative to the due date and time)
 *   or [ABSOLUTE] (rings at [at]). An unknown rule from a newer app is kept and not rung.
 * @property occurrences Per-occurrence state; null when none has rung yet.
 * @property calendarEventId The calendar event carrying it, when exported.
 */
data class TaskReminder(
    val id: String,
    val rule: String,
    val at: OffsetDateTime? = null,
    val occurrences: List<TaskReminderOccurrence>? = null,
    val calendarEventId: String? = null,
    val extra: JsonObject = TaskUnknown.NONE,
) {
    companion object {
        const val MAX_PER_TASK = 5
        const val AT_START = "atStart"
        const val TEN_MINUTES_BEFORE = "10mBefore"
        const val ONE_HOUR_BEFORE = "1hBefore"
        const val DAY_BEFORE_AT_NINE = "1dBefore9am"
        const val ABSOLUTE = "at"
    }
}
