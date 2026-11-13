package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import java.time.LocalTime
import java.time.OffsetDateTime
import java.time.ZoneId
import java.util.UUID
import kotlinx.serialization.json.JsonObject

/**
 * One value for a goal on a day (brief §5.1): a hand tick or log, time added by hand, or a reading. Every entry is kept. How a
 * day's value comes from them depends on the goal's kind ([GoalCounting]).
 *
 * @property day The local date it is for (a past date when back-filled).
 * @property time The local time of the reading, or when time added by hand started; null = no time (added on top).
 * @property value In the goal's unit. A habit's tick is 1; "Untick" is always a new live hand entry with value 0, never a tombstone.
 * @property editedAt When it was made or last edited: the item stamp, and what "latest hand edit" means.
 * @property position Its place in the day's list; 0 when unordered.
 * @property deletedAt Set on a tombstone: a removed log. Never used to untick.
 * @property extra Fields written by a newer app, kept and written back unchanged after the known ones (ADR 79 forward compatibility);
 * empty when there are none. May hold private text: sealed at rest, never logged.
 */
data class GoalEntry(
    override val id: UUID,
    val goalId: UUID,
    val day: LocalDate,
    val time: LocalTime?,
    val value: Double,
    val source: GoalEntrySource,
    override val editedAt: OffsetDateTime,
    override val position: Double = 0.0,
    override val deletedAt: OffsetDateTime? = null,
    val extra: JsonObject = JsonObject(emptyMap()),
    /** The device that measured it (a derived value: Health Connect, screen time, #tag extras, calendar); null = by hand or an older app. */
    val deviceId: UUID? = null,
) : GoalMergeItem {
    override val tieBreak: String get() = GoalJson.text(GoalJson.writeEntry(this))

    override fun withStamps(editedAt: OffsetDateTime, deletedAt: OffsetDateTime?): GoalEntry = copy(editedAt = editedAt, deletedAt = deletedAt)

    /** Its newest time: the deletion when later than the edit. */
    val stamp: OffsetDateTime get() = deletedAt?.let { if (it.toInstant().isAfter(editedAt.toInstant())) it else editedAt } ?: editedAt

    val isLive: Boolean get() = deletedAt == null

    /** Whether it was made after its day (a back-fill): it updates that week and the streak, never a guard. */
    fun isBackFill(zone: ZoneId): Boolean = editedAt.atZoneSameInstant(zone).toLocalDate() > day
}
