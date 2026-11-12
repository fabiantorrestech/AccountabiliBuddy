package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import java.time.LocalTime
import java.time.OffsetDateTime
import java.util.UUID
import kotlinx.serialization.json.JsonObject

/**
 * A goal reminder (brief §5.1, §5.9): a suggestion that rings; nothing locks or waits. Never exported to Google Calendar.
 *
 * @property weekdays For [GoalReminderDays.Weekdays]: the days it rings on, by name; null otherwise.
 * @property date For [GoalReminderDays.Date] and [GoalReminderDays.Today]: the date; null otherwise.
 * @property time The local time it rings.
 * @property repeat Whether it repeats on its days (false: it rings once, the next time it is due).
 * @property reason Why, in the user's words. Private text: sealed at rest, never logged.
 * @property skipWhenDone Checked at ring time: no ring when the goal's day (or period) is already hit.
 * @property nudgeWhenBehind Rings once, the morning after the goal drops below pace.
 * @property extra Fields written by a newer app, kept and written back unchanged after the known ones (ADR 79 forward compatibility);
 * empty when there are none. May hold private text: sealed at rest, never logged.
 */
data class GoalReminder(
    val id: UUID,
    val goalId: UUID,
    val days: GoalReminderDays,
    val time: LocalTime,
    val position: Double,
    val editedAt: OffsetDateTime,
    val weekdays: List<GoalWeekday>? = null,
    val date: LocalDate? = null,
    val repeat: Boolean = true,
    val reason: String? = null,
    val skipWhenDone: Boolean = true,
    val nudgeWhenBehind: Boolean = false,
    val deletedAt: OffsetDateTime? = null,
    val extra: JsonObject = JsonObject(emptyMap()),
) {
    val isLive: Boolean get() = deletedAt == null
}
