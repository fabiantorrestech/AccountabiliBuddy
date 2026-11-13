package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import kotlinx.serialization.json.JsonObject
import java.time.OffsetDateTime

/**
 * "Changed on both devices" (ADR 76; the PC's TaskConflict): the title or the note was changed on two devices since the last value
 * they agreed on. The task shows the newer version; this keeps the other one until the user picks. Sealed text, like the field.
 *
 * @property field [TITLE] or [NOTE].
 * @property otherValue The version not shown (null for a removed note).
 */
data class TaskConflict(
    val field: String,
    val otherValue: String?,
    val otherDeviceId: String,
    val otherEditedAt: OffsetDateTime,
    val extra: JsonObject = TaskUnknown.NONE,
) {
    /** The same conflict as the PC compares two (the moment, not the offset it was written with). */
    fun sameAs(other: TaskConflict): Boolean =
        field == other.field && otherValue == other.otherValue && otherDeviceId == other.otherDeviceId && DotNetTime.same(otherEditedAt, other.otherEditedAt)

    companion object {
        const val TITLE = "title"
        const val NOTE = "note"
    }
}
