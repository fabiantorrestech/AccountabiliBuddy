package com.accountabilibuddy.shared.tasks

import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

/**
 * The origin lines a task shows (ADR 76; the PC's TaskOrigins), made once when the task is made and kept as text. Times are 12-hour
 * with AM/PM, in the local time zone of the device that made the task.
 */
object TaskOrigins {
    const val FROM_PHONE = "Added from phone"
    const val FROM_STEP = "From a step"
    const val COME_BACK_TO = "Come back to"

    /** The open tasks the one-time move (and an older device's new items) brought into To sort (MIG1; the PC's EarlierVersion). */
    const val EARLIER_VERSION = "From the earlier version"

    private val parkedForm: DateTimeFormatter = DateTimeFormatter.ofPattern("EEE M/d h:mm a", Locale.US)
    private val dayForm: DateTimeFormatter = DateTimeFormatter.ofPattern("EEE M/d", Locale.US)

    /** "Parked Tue 11/3 11:52 AM". */
    fun parked(at: OffsetDateTime, zone: ZoneId): String = "Parked " + parkedForm.format(at.atZoneSameInstant(zone))

    /** "Auto-saved Wed 11/5", for the day the shutdown card wrapped up. */
    fun autoSaved(day: LocalDate): String = "Auto-saved " + dayForm.format(day)
}
