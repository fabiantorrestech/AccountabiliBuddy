package com.accountabilibuddy.companion.ui.today

/**
 * The compact time-left label (brief §3.11, change note §2): whole minutes ("32m") until the last minute, then minutes and seconds
 * ("0:59", "0:58"…). Minutes are whole minutes left (32:14 reads "32m"); an hour or more stays in minutes ("90m"), as the brief
 * writes it. Plain Kotlin, no Android.
 */
object TimeLeftText {
    fun compact(secondsLeft: Long): String {
        val seconds = secondsLeft.coerceAtLeast(0)
        return if (seconds >= 60) "${seconds / 60}m" else "0:%02d".format(seconds)
    }
}
