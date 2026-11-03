package com.accountabilibuddy.companion.calendar

import android.Manifest
import android.content.ContentUris
import android.content.Context
import android.content.pm.PackageManager
import android.provider.CalendarContract
import com.accountabilibuddy.companion.model.CalendarEvent
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId

/** One calendar of the phone, as the rule page lists it. [id] is the key rules are stored under. */
data class PhoneCalendar(val id: String, val name: String, val account: String)

/**
 * Reads the phone's own calendar through Android's calendar provider (ADR 58), read-only, on this phone only: nothing read here is sent
 * anywhere, and titles are held in memory only. Call from a background thread. Without the READ_CALENDAR permission (never granted, or
 * taken back) every call returns nothing, which is the strict way: the phone then follows the plan for its own calendars.
 * Only the calendars the caller names are read, and all-day, cancelled and zero-length events are left out.
 */
class PhoneCalendarReader(private val context: Context) {
    fun hasPermission(): Boolean =
        context.checkSelfPermission(Manifest.permission.READ_CALENDAR) == PackageManager.PERMISSION_GRANTED

    /** The phone's calendars (visible or not: hiding one changes no guard). Empty without the permission. */
    fun calendars(): List<PhoneCalendar> {
        if (!hasPermission()) return emptyList()

        val projection = arrayOf(
            CalendarContract.Calendars._ID,
            CalendarContract.Calendars.CALENDAR_DISPLAY_NAME,
            CalendarContract.Calendars.ACCOUNT_NAME,
        )
        return runCatching {
            context.contentResolver.query(CalendarContract.Calendars.CONTENT_URI, projection, null, null, CalendarContract.Calendars._ID)?.use { cursor ->
                val list = ArrayList<PhoneCalendar>()
                while (cursor.moveToNext()) {
                    list += PhoneCalendar(idOf(cursor.getLong(0)), cursor.getString(1).orEmpty(), cursor.getString(2).orEmpty())
                }
                list
            }.orEmpty()
        }.getOrDefault(emptyList())
    }

    /**
     * The timed events of [calendarIds] (the keys from [idOf]) that touch [from]..[to]. Null when the read failed or has no permission,
     * so the caller can tell "nothing there" from "could not look" and keep its strict view either way.
     */
    fun events(calendarIds: Set<String>, from: Instant, to: Instant, zone: ZoneId): List<CalendarEvent>? {
        if (calendarIds.isEmpty()) return emptyList()
        if (!hasPermission()) return null

        val rawIds = calendarIds.mapNotNull { it.removePrefix(ID_PREFIX).toLongOrNull() }
        if (rawIds.isEmpty()) return emptyList()

        val builder = CalendarContract.Instances.CONTENT_URI.buildUpon()
        ContentUris.appendId(builder, from.toEpochMilli())
        ContentUris.appendId(builder, to.toEpochMilli())
        val projection = arrayOf(
            CalendarContract.Instances.EVENT_ID,
            CalendarContract.Instances.CALENDAR_ID,
            CalendarContract.Instances.TITLE,
            CalendarContract.Instances.BEGIN,
            CalendarContract.Instances.END,
            CalendarContract.Instances.ALL_DAY,
            CalendarContract.Instances.STATUS,
            CalendarContract.Instances.RRULE,
            CalendarContract.Instances.RDATE,
            CalendarContract.Instances.ORIGINAL_ID,
        )
        val selection = "${CalendarContract.Instances.CALENDAR_ID} IN (${rawIds.joinToString(",")})"
        return runCatching {
            context.contentResolver.query(builder.build(), projection, selection, null, CalendarContract.Instances.BEGIN)?.use { cursor ->
                val list = ArrayList<CalendarEvent>()
                while (cursor.moveToNext()) {
                    if (cursor.getInt(5) != 0 || cursor.getInt(6) == CalendarContract.Events.STATUS_CANCELED) continue

                    val begin = cursor.getLong(3)
                    val end = cursor.getLong(4)
                    if (end <= begin) continue

                    val localStart = LocalDateTime.ofInstant(Instant.ofEpochMilli(begin), zone)
                    var localEnd = LocalDateTime.ofInstant(Instant.ofEpochMilli(end), zone)
                    // The hour the clocks go back is ambiguous on a wall clock: an event across it can end "before" it starts. Keep its real length.
                    if (!localEnd.isAfter(localStart)) localEnd = localStart.plus(java.time.Duration.ofMillis(end - begin))

                    val eventId = cursor.getLong(0)
                    // A repeating event (or one occurrence changed from its series) is one row per occurrence, so its begin joins the id.
                    val repeats = !cursor.getString(7).isNullOrEmpty() || !cursor.getString(8).isNullOrEmpty() || !cursor.isNull(9)
                    list += CalendarEvent(
                        id = if (repeats) "$eventId@$begin" else eventId.toString(),
                        calendarId = idOf(cursor.getLong(1)),
                        title = cursor.getString(2).orEmpty(),
                        localStart = localStart,
                        localEnd = localEnd,
                        isAllDay = false,
                        seriesId = if (repeats) eventId.toString() else null,
                        occurrenceKey = if (repeats) begin.toString() else null,
                    )
                }
                list
            }
        }.getOrNull()
    }

    companion object {
        /** What every key of a phone calendar starts with (and so every first-seen stamp of the phone's own events). */
        const val ID_PREFIX = "phone:"

        /** The key a phone calendar's rule is stored under. */
        fun idOf(calendarRowId: Long): String = "$ID_PREFIX$calendarRowId"
    }
}
