package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.goals.GoalLabelId
import com.accountabilibuddy.shared.sync.FieldStamp
import kotlinx.serialization.json.JsonObject
import java.time.OffsetDateTime

/**
 * A label (ADR 76; the PC's TaskLabel): the coloured dot a task has at most one of. Its id is [idFor] its name, so every device
 * that only knows the name (an old sync field) reaches the same label.
 *
 * @property name Unique among labels, case-insensitively.
 * @property color "#RRGGBB", "#RRGGBBAA" or "rgba(r,g,b,a)" ([TaskLabelColors]).
 * @property editedAt Its newest change (name, colour, rings on, position); the newest version wins (rings on: see [ringOnStamp]).
 * @property ringOn The phones its tasks' reminders ring on (device ids); null for every device.
 * @property deletedAt Set on a tombstone (kept for good); its tasks become "No label".
 * @property ringOnStamp When and on which device [ringOn] was chosen; null when never. Rings on merges on its own, newest stamp
 *   first ([TaskLabelMerge]).
 * @property onlyOn ADR 89: the devices that show this label (device ids, lower case); null for every device (shared). A device-only label
 *   still syncs but the other devices don't show it ([TaskScopes]). Several ids when devices made the same name. A list whose devices
 *   were all removed from the group counts as shared, at display only.
 * @property onlyOnStamp When and on which device [onlyOn] was chosen (a stamp with a null list is a choice of "shared"); null when never.
 *   Merges on its own like [ringOnStamp]. A tombstone keeps the stamp, not the list; a revived label is shared, stamped at the revive.
 */
data class TaskLabel(
    val id: String,
    val name: String,
    val color: String,
    val position: Double,
    val editedAt: OffsetDateTime,
    val ringOn: List<String>? = null,
    val deletedAt: OffsetDateTime? = null,
    val ringOnStamp: FieldStamp? = null,
    val onlyOn: List<String>? = null,
    val onlyOnStamp: FieldStamp? = null,
    val extra: JsonObject = TaskUnknown.NONE,
) {
    val isLive: Boolean get() = deletedAt == null

    /** The label as a tombstone: no name or colour left, and no device list (its stamp stays, ADR 89). */
    fun deleted(at: OffsetDateTime): TaskLabel = TaskLabel(id, "", TaskLabelColors.SET[0], position, at, deletedAt = at, onlyOnStamp = onlyOnStamp)

    companion object {
        /** The label id for a name, the same on every device (the PC's TaskLabel.IdFor), so "Work" and "work" are one label. */
        fun idFor(name: String): String = GoalLabelId.idFor(name).toString()

        /** Whether two names are the same label (case-insensitive, trimmed). */
        fun sameName(first: String, second: String): Boolean = first.trim().equals(second.trim(), ignoreCase = true)
    }
}
