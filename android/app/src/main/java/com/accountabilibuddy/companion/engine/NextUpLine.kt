package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.FixedLunch
import com.accountabilibuddy.companion.model.PlannedFocus
import com.accountabilibuddy.shared.goals.GoalTexts
import java.time.Duration
import java.time.LocalDateTime

/**
 * One line of Today's "Next up" (board Main): "Now · Work block until 12:30 PM", "12:30 PM · Lunch · 45 min". Read from the day
 * plan this phone already holds and the same work windows the headline uses; it only says what is planned and changes nothing.
 *
 * @property whenText "Now", or the start time (12-hour with AM/PM).
 * @property isNow The part running now (drawn in the accent).
 * @property text What it is and how long ("Lunch · 45 min"), or until when for the running one.
 */
data class NextUpLine(val whenText: String, val isNow: Boolean, val text: String) {
    companion object {
        /** The most lines the part shows: the running one and the next two. */
        const val MAX_LINES = 3

        /**
         * @param windows Today's work windows (the headline's own list).
         * @param lunch Today's fixed lunch, when the plan has one.
         */
        fun of(windows: List<PlannedFocus>, lunch: FixedLunch?, now: LocalDateTime, clock: ClockFormat): List<NextUpLine> {
            val parts = (windows.map { it.name to it.window } + listOfNotNull(lunch?.let { it.name to it.window })).sortedBy { it.second.start }
            val running = parts.firstOrNull { it.second.contains(now) }
            val lines = mutableListOf<NextUpLine>()
            if (running != null) lines += NextUpLine("Now", true, "${running.first} until ${clock.time(running.second.end)}")
            parts.filter { it.second.start.isAfter(now) }.forEach { (name, window) ->
                lines += NextUpLine(clock.time(window.start), false, "$name · ${GoalTexts.minutes(Duration.between(window.start, window.end).toMinutes().toDouble())}")
            }
            return lines.take(MAX_LINES)
        }
    }
}
