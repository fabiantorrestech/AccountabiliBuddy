package com.accountabilibuddy.shared.sync

import kotlinx.serialization.json.JsonObject
import java.time.OffsetDateTime

/**
 * When and where one part of a shared item was last changed (the PC's TaskFieldStamp, ADR 76): the stamp the field-by-field merges
 * compare. Tasks use it per [com.accountabilibuddy.shared.tasks.TaskField]; a label's "Rings on" and the account's Tasks settings
 * carry one per value; the goals sync (ADR 79) reuses it for its field stamps. The rules are in [FieldStamps].
 *
 * @property at When it was changed (compared by its moment; the offset is kept as written).
 * @property by The device that changed it, lower case; [NOBODY] for a value a migration made.
 * @property bases A task's title and note only: hashes of the values this one was edited from, oldest first.
 * @property extra Fields a newer app wrote that this version doesn't know, written back unchanged.
 */
data class FieldStamp(
    val at: OffsetDateTime,
    val by: String,
    val bases: List<String>? = null,
    val extra: JsonObject = JsonObject(emptyMap()),
) {
    companion object {
        /** The PC's Guid.Empty: nobody (a migration, a field never stamped). */
        const val NOBODY = "00000000-0000-0000-0000-000000000000"
    }
}
