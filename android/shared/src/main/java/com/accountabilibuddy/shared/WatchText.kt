package com.accountabilibuddy.shared

import java.time.Duration
import java.time.Instant
import java.time.LocalTime
import java.time.format.DateTimeFormatter

/** The short texts the watch shows (ADR 21), kept here so the scratch checks can read them. */
object WatchText {
    // Data younger than this needs no "updated … ago".
    private val FRESH: Duration = Duration.ofMinutes(10)

    private val twelveHour = DateTimeFormatter.ofPattern("h:mm a")
    private val twentyFourHour = DateTimeFormatter.ofPattern("HH:mm")

    /** E.g. "2h 55m", "45m", "7h". */
    fun duration(minutes: Int): String {
        val hours = minutes / 60
        val rest = minutes % 60
        return when {
            hours == 0 -> "${rest}m"
            rest == 0 -> "${hours}h"
            else -> "${hours}h ${rest}m"
        }
    }

    /** Null while the data is fresh; else e.g. "updated 20 min ago", "updated 3 h ago", "updated 2 days ago". */
    fun updatedAgo(createdAt: Instant, now: Instant): String? {
        val age = Duration.between(createdAt, now)
        return when {
            age < FRESH -> null
            age < Duration.ofHours(1) -> "updated ${age.toMinutes()} min ago"
            age < Duration.ofDays(1) -> "updated ${age.toHours()} h ago"
            age < Duration.ofDays(2) -> "updated a day ago"
            else -> "updated ${age.toDays()} days ago"
        }
    }

    /** E.g. "1:00 PM", or "13:00" on a 24-hour watch. */
    fun clock(time: LocalTime, use24Hour: Boolean): String = time.format(if (use24Hour) twentyFourHour else twelveHour)

    /** E.g. "24:59" or "1:05:12". */
    fun timer(duration: Duration): String {
        val seconds = duration.seconds.coerceAtLeast(0)
        val hours = seconds / 3600
        val minutes = seconds % 3600 / 60
        val rest = seconds % 60
        return if (hours > 0) "%d:%02d:%02d".format(hours, minutes, rest) else "%d:%02d".format(minutes, rest)
    }

    /** What the watch says before it has anything from the phone: a title and a line, by whether the phone answered. */
    fun notLinked(phoneReachable: Boolean?): Pair<String, String> = when (phoneReachable) {
        null -> "AccountabiliBuddy" to "Asking your phone…"
        false -> "Can't reach your phone" to "Keep it nearby with AccountabiliBuddy installed."
        true -> "Not linked" to "On your phone: AccountabiliBuddy → Settings → Pixel Watch → Link this watch."
    }
}
