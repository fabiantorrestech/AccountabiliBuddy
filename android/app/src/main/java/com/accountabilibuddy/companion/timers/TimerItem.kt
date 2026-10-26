package com.accountabilibuddy.companion.timers

/**
 * A timer on this phone (V1, local only): its name, its length, and while it runs its end time (wall clock, so it
 * survives a restart), or while paused the time it had left. [totalMs] is what the bar fills against: the length,
 * plus every +1 min since it started.
 */
data class TimerItem(
    val id: Int,
    val name: String,
    val lengthMs: Long,
    val state: TimerState = TimerState.IDLE,
    val endAtMs: Long? = null,
    val remainingMs: Long? = null,
    val totalMs: Long = lengthMs,
) {
    /** The time left at [nowMs]: counting down while it runs, frozen while paused, the whole length before a start. */
    fun leftMs(nowMs: Long): Long = when (state) {
        TimerState.RUNNING -> ((endAtMs ?: nowMs) - nowMs).coerceAtLeast(0)
        TimerState.PAUSED -> remainingMs ?: lengthMs
        TimerState.IDLE -> lengthMs
        TimerState.DONE -> 0
    }

    /** How far it is, 0 to 1 (the notification's and the card's bar). */
    fun progress(nowMs: Long): Float =
        if (totalMs <= 0) 1f else (1f - leftMs(nowMs).toFloat() / totalMs).coerceIn(0f, 1f)

    companion object {
        const val DEFAULT_NAME = "Timer"

        /** A time left as "4:05" or, from an hour, "1:04:05". */
        fun formatLeft(ms: Long): String {
            val seconds = (ms + 999) / 1000
            val hours = seconds / 3600
            val minutes = (seconds % 3600) / 60
            val rest = seconds % 60
            return if (hours > 0) "%d:%02d:%02d".format(hours, minutes, rest) else "%d:%02d".format(minutes, rest)
        }

        /** A length as "25 min", "1 h" or "1 h 30 min". */
        fun formatLength(ms: Long): String {
            val minutes = ms / 60_000
            val hours = minutes / 60
            val rest = minutes % 60
            return when {
                hours == 0L -> "$rest min"
                rest == 0L -> "$hours h"
                else -> "$hours h $rest min"
            }
        }
    }
}
