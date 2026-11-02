package com.accountabilibuddy.companion.ui.stats

import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.stats.PhoneDay
import com.accountabilibuddy.companion.stats.PhonePanels
import com.accountabilibuddy.companion.usage.PhoneUsageReader
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId

/**
 * Reads this phone's own days for the Phone view from Android's usage records: the time an app was in front, the part of it inside
 * counted focus, and the pickups (unlocks). It runs off the main thread and reads on the phone only; nothing is stored or sent. Days
 * Android no longer has records for come back with no time at all and are dropped from the start, so the view shows fewer days, never zeros.
 */
internal object PhoneDayLoader {
    // A day with under a minute on the phone is a day Android has no record of (a phone in use for a day shows more).
    private val NO_RECORD: java.time.Duration = java.time.Duration.ofMinutes(1)

    /** The last [PhonePanels.DAYS] days up to [today]; empty without Usage access. */
    fun load(reader: PhoneUsageReader, today: LocalDate, focus: List<LocalWindow>, zone: ZoneId, now: Instant): List<PhoneDay> {
        if (!reader.hasAccess()) return emptyList()
        return (PhonePanels.DAYS - 1 downTo 0)
            .map { back ->
                val date = today.minusDays(back.toLong())
                val day = LocalWindow(date.atStartOfDay(), date.plusDays(1).atStartOfDay())
                val usage = reader.read(day, focus)
                val from = day.start.atZone(zone).toInstant()
                val to = minOf(day.end.atZone(zone).toInstant(), now)
                PhoneDay(date, usage.total, usage.inFocus, reader.unlocks(from, to)?.size)
            }
            .dropWhile { it.total < NO_RECORD }
    }
}
