package com.accountabilibuddy.watch.timers

import com.accountabilibuddy.shared.WatchText
import java.time.Duration

/** How a timer's length reads: "25m", "1h 30m", or "45s" under a minute. */
object TimerText {
    fun length(length: Duration): String {
        val minutes = length.toMinutes().toInt()
        return if (minutes == 0) "${length.seconds}s" else WatchText.duration(minutes)
    }
}
