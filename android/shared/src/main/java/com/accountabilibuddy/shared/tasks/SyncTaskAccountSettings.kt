package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import kotlinx.serialization.json.JsonObject
import java.time.OffsetDateTime

/**
 * The Tasks settings that belong to the user's account, shared by all their devices (the PC's SyncTaskAccountSettings; the sync
 * file's optional `taskAccountSettings`): the view the Shelf opens with, the picked calendar for tasks with a due date and the
 * devices task reminders ring on. Each field has its own stamp and merges on its own ([TaskAccountSettingsMerge]). A field without a
 * stamp was never chosen; a stamp with a null value is a choice too. Ids only, no text; never logged.
 *
 * @property defaultViewId The saved view the Shelf opens with; null for the built-in Default.
 * @property pickedCalendarId The Google calendar tasks with a due date go to; null when off.
 * @property ringOn The device ids that ring for task reminders; null for every device ([TaskRingDevices]).
 */
data class SyncTaskAccountSettings(
    val defaultViewId: String? = null,
    val defaultViewStamp: FieldStamp? = null,
    val pickedCalendarId: String? = null,
    val pickedCalendarStamp: FieldStamp? = null,
    val ringOn: List<String>? = null,
    val ringOnStamp: FieldStamp? = null,
    val extra: JsonObject = TaskUnknown.NONE,
) {
    /** Nothing was ever chosen. */
    val isEmpty: Boolean get() = defaultViewStamp == null && pickedCalendarStamp == null && ringOnStamp == null

    companion object {
        /** The longest calendar id taken from a file. */
        const val MAX_CALENDAR_ID_LENGTH = 1024

        /** The stamp time of a value a device kept on its own before these settings were shared: older than any real choice. */
        val ADOPTED_AT: OffsetDateTime = DotNetTime.UNIX_EPOCH

        val EMPTY = SyncTaskAccountSettings()
    }
}
