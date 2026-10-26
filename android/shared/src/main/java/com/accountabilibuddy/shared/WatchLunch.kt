package com.accountabilibuddy.shared

import java.time.Duration
import java.time.Instant

/**
 * Today's flexible lunch as the phone has it, for the watch's Lunch page: whether today is a workday, the lunch
 * length setting, and today's lunch once started ([start] and [end]; null before). Only times, never anything else.
 */
data class WatchLunch(
    val workday: Boolean,
    val lengthMinutes: Int,
    val start: Instant? = null,
    val end: Instant? = null,
) {
    /** True while lunch runs at [now]. */
    fun isRunning(now: Instant): Boolean = start != null && end != null && now.isBefore(end)

    /** True once today's lunch was taken and is over. */
    fun isDone(now: Instant): Boolean = start != null && end != null && !now.isBefore(end)

    /** The Lunch page shows on a workday, or while lunch runs. */
    fun isShown(now: Instant): Boolean = workday || isRunning(now)

    /** −5 min can still take something off: the running lunch ends more than a minute from now. */
    fun canShorten(now: Instant): Boolean = isRunning(now) && end!!.isAfter(now.plus(SHORTEST_LEFT))

    /** How much of the running lunch is left (0 to 1), for the countdown ring. */
    fun fractionLeft(now: Instant): Float {
        if (start == null || end == null || !now.isBefore(end)) return 0f
        val total = Duration.between(start, end).seconds.coerceAtLeast(1)
        return (Duration.between(now, end).seconds.toFloat() / total).coerceIn(0f, 1f)
    }

    companion object {
        /** A shortened lunch ends no sooner than this after now (as on the PC). */
        val SHORTEST_LEFT: Duration = Duration.ofMinutes(1)
    }
}
