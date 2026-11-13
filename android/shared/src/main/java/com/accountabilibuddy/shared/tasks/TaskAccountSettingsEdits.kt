package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import java.time.OffsetDateTime

/**
 * Changes to the account's Tasks settings ([SyncTaskAccountSettings]) made on this device (the PC's TaskAccountSettingsEdits, the same
 * rules). A choice is stamped now, or just after the stamp it replaces when that one is ahead (another device's fast clock), so an edit
 * made after seeing a value always wins over it on every device. Choosing the value a field already has, with a real stamp, changes
 * nothing; with only the adopted stamp ([SyncTaskAccountSettings.ADOPTED_AT]) it is stamped now, so the explicit choice can't lose a tie
 * to another device.
 */
object TaskAccountSettingsEdits {
    fun withDefaultView(settings: SyncTaskAccountSettings?, viewId: String?, now: OffsetDateTime, self: String): SyncTaskAccountSettings {
        val current = settings ?: SyncTaskAccountSettings.EMPTY
        val wanted = viewId?.takeIf { it.isNotBlank() && it != FieldStamp.NOBODY }
        if (isChosen(current.defaultViewStamp) && sameId(current.defaultViewId, wanted)) return current
        return current.copy(defaultViewId = wanted, defaultViewStamp = FieldStamp(after(current.defaultViewStamp, now), self.lowercase()))
    }

    fun withPickedCalendar(settings: SyncTaskAccountSettings?, calendarId: String?, now: OffsetDateTime, self: String): SyncTaskAccountSettings {
        val current = settings ?: SyncTaskAccountSettings.EMPTY
        val wanted = calendarId?.trim()?.takeIf { it.isNotEmpty() }
        if (isChosen(current.pickedCalendarStamp) && current.pickedCalendarId == wanted) return current
        return current.copy(pickedCalendarId = wanted, pickedCalendarStamp = FieldStamp(after(current.pickedCalendarStamp, now), self.lowercase()))
    }

    /** "Task reminders ring on": the device ids that ring (null: every device). */
    fun withRingOn(settings: SyncTaskAccountSettings?, ringOn: List<String>?, now: OffsetDateTime, self: String): SyncTaskAccountSettings {
        val current = settings ?: SyncTaskAccountSettings.EMPTY
        val wanted = TaskRingDevices.normalized(ringOn)
        if (isChosen(current.ringOnStamp) && TaskRingDevices.same(current.ringOn, wanted)) return current
        return current.copy(ringOn = wanted, ringOnStamp = FieldStamp(after(current.ringOnStamp, now), self.lowercase()))
    }

    /**
     * The values this device kept on its own before the settings were shared, taken over once: only into a field nothing was chosen
     * for yet, stamped [SyncTaskAccountSettings.ADOPTED_AT] so any real choice, on any device, wins over them.
     */
    fun adopted(settings: SyncTaskAccountSettings?, legacyDefaultViewId: String?, legacyCalendarId: String?, self: String): SyncTaskAccountSettings {
        var current = settings ?: SyncTaskAccountSettings.EMPTY
        val stamp = FieldStamp(SyncTaskAccountSettings.ADOPTED_AT, self.lowercase())
        if (current.defaultViewStamp == null && !legacyDefaultViewId.isNullOrBlank() && legacyDefaultViewId != FieldStamp.NOBODY) {
            current = current.copy(defaultViewId = legacyDefaultViewId, defaultViewStamp = stamp)
        }

        if (current.pickedCalendarStamp == null && !legacyCalendarId.isNullOrBlank()) {
            current = current.copy(pickedCalendarId = legacyCalendarId.trim(), pickedCalendarStamp = stamp)
        }

        return current
    }

    /** A stamp of a real choice (not none, not the one-time take-over at the upgrade). */
    fun isChosen(stamp: FieldStamp?): Boolean = stamp != null && DotNetTime.compare(stamp.at, SyncTaskAccountSettings.ADOPTED_AT) > 0

    private fun sameId(first: String?, second: String?): Boolean =
        if (first == null || second == null) first == null && second == null else first.equals(second, ignoreCase = true)

    private fun after(replaced: FieldStamp?, now: OffsetDateTime): OffsetDateTime =
        if (replaced != null && DotNetTime.compare(replaced.at, now) >= 0) DotNetTime.plusTick(replaced.at) else now
}
