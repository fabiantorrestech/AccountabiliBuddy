package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.FieldStamps
import com.accountabilibuddy.shared.sync.SharedListMerge
import java.time.OffsetDateTime

/**
 * Merges two copies of [SyncTaskAccountSettings] field by field (the PC's TaskAccountSettingsMerge): a stamped field beats an
 * unstamped one; between two stamps the later time wins, then the device id, then the value, so every device picks the same
 * whatever order the files come in. A stamp more than the future limit past the receiver's now counts as that far ahead.
 */
object TaskAccountSettingsMerge {
    fun merge(first: SyncTaskAccountSettings?, second: SyncTaskAccountSettings?, now: OffsetDateTime): SyncTaskAccountSettings {
        val limit = now.plus(SharedListMerge.FUTURE_LIMIT)
        val a = clamped(first ?: SyncTaskAccountSettings.EMPTY, limit)
        val b = clamped(second ?: SyncTaskAccountSettings.EMPTY, limit)

        val viewFromFirst = FieldStamps.firstWins(a.defaultViewStamp, b.defaultViewStamp, a.defaultViewId ?: "", b.defaultViewId ?: "")
        val calendarFromFirst = FieldStamps.firstWins(a.pickedCalendarStamp, b.pickedCalendarStamp, a.pickedCalendarId ?: "", b.pickedCalendarId ?: "")
        val ringFromFirst = FieldStamps.firstWins(a.ringOnStamp, b.ringOnStamp, TaskRingDevices.text(a.ringOn), TaskRingDevices.text(b.ringOn))
        return SyncTaskAccountSettings(
            if (viewFromFirst) a.defaultViewId else b.defaultViewId,
            if (viewFromFirst) a.defaultViewStamp else b.defaultViewStamp,
            if (calendarFromFirst) a.pickedCalendarId else b.pickedCalendarId,
            if (calendarFromFirst) a.pickedCalendarStamp else b.pickedCalendarStamp,
            if (ringFromFirst) a.ringOn else b.ringOn,
            if (ringFromFirst) a.ringOnStamp else b.ringOnStamp,
            // Not on the PC (it drops unknown fields): the first copy's when it has any, so this device's own survive a merge.
            if (a.extra.isNotEmpty()) a.extra else b.extra,
        )
    }

    /**
     * A copy read from a file or a backup (not trusted): a value without a stamp is dropped (it can't take part in a merge), the
     * empty id reads as the built-in Default, a blank calendar id as off, and a calendar id longer than
     * [SyncTaskAccountSettings.MAX_CALENDAR_ID_LENGTH] drops that field. A "rings on" list loses empty and repeated ids and is kept
     * in id order; one longer than [TaskRingDevices.MAX_DEVICES] drops that field. Null stays null.
     */
    fun clean(settings: SyncTaskAccountSettings?): SyncTaskAccountSettings? {
        if (settings == null) {
            return null
        }

        val viewStamp = usable(settings.defaultViewStamp)
        val view = if (viewStamp == null || settings.defaultViewId == FieldStamp.NOBODY) null else settings.defaultViewId

        val calendar = settings.pickedCalendarId?.takeIf { it.isNotBlank() }?.trim()
        val calendarStamp = if (calendar != null && calendar.length > SyncTaskAccountSettings.MAX_CALENDAR_ID_LENGTH) null else usable(settings.pickedCalendarStamp)
        val ringOn = TaskRingDevices.normalized(settings.ringOn)
        val ringStamp = if (ringOn != null && ringOn.size > TaskRingDevices.MAX_DEVICES) null else usable(settings.ringOnStamp)
        return SyncTaskAccountSettings(
            view, viewStamp, if (calendarStamp == null) null else calendar, calendarStamp, if (ringStamp == null) null else ringOn, ringStamp, settings.extra,
        )
    }

    // Only the time and the device take part; earlier hashes belong to a task's title and note.
    private fun usable(stamp: FieldStamp?): FieldStamp? = stamp?.let { FieldStamp(it.at, it.by) }

    private fun clamped(settings: SyncTaskAccountSettings, limit: OffsetDateTime): SyncTaskAccountSettings = settings.copy(
        defaultViewStamp = settings.defaultViewStamp?.let { FieldStamps.clamped(it, limit) },
        pickedCalendarStamp = settings.pickedCalendarStamp?.let { FieldStamps.clamped(it, limit) },
        ringOnStamp = settings.ringOnStamp?.let { FieldStamps.clamped(it, limit) },
    )
}
