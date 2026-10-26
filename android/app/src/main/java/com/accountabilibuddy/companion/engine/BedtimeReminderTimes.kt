package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.BedtimeReminder
import com.accountabilibuddy.companion.model.BedtimeReminderSettings
import java.time.Duration
import java.time.LocalDateTime

/**
 * When tonight's bedtime reminders fire (ADR 38; same as the PC's BedtimeReminderTimes): each at bedtime minus its
 * minutes, in firing order, labelled "n/N". Worked out again whenever bedtime moves.
 */
object BedtimeReminderTimes {
    data class Due(val at: LocalDateTime, val number: Int, val count: Int, val reminder: BedtimeReminder)

    /** A reminder this many minutes late still fires. */
    val GRACE: Duration = Duration.ofMinutes(2)

    fun forNight(bedtime: LocalDateTime, settings: BedtimeReminderSettings): List<Due> {
        if (!settings.on) return emptyList()
        val ordered = settings.list.filter { it.minutesBefore >= 0 }.sortedByDescending { it.minutesBefore }
        return ordered.mapIndexed { i, r -> Due(bedtime.minusMinutes(r.minutesBefore.toLong()), i + 1, ordered.size, r) }
    }

    /** The night's id for dismissals: its bedtime's evening date. */
    fun nightId(bedtime: LocalDateTime): String = (if (bedtime.hour < 12) bedtime.minusDays(1) else bedtime).toLocalDate().toString()
}
