package com.accountabilibuddy.companion.stats

import java.time.temporal.ChronoUnit

/**
 * How much history is behind a view, said plainly. Sync carries about the last 14 days of sessions and the phone keeps what it
 * received for up to 90 days, so a newly linked phone has about 14: a Year or 12-week view is shallow, and never padded with zeros.
 */
object StatsDepth {
    /** Days of history the phone holds: from the first day with a session to today. */
    fun historyDays(data: StatsData): Int = ChronoUnit.DAYS.between(data.firstData, data.today).toInt() + 1

    /** The line to show when a view that looks back [wantedDays] days has less than that behind it; null when it has enough. */
    fun caption(data: StatsData, wantedDays: Int): String? {
        val have = historyDays(data)
        // The phone never holds more than it keeps, so a year-long range is "deep enough" once it has all of those days.
        if (!data.hasHistory || have >= minOf(wantedDays, StatsData.KEPT_DAYS)) return null
        return "Only ${have} ${StatsFormat.plural(have, "day")} of history so far. Your PC syncs about 14 days to a new phone, and this phone keeps up to ${StatsData.KEPT_DAYS}."
    }
}
