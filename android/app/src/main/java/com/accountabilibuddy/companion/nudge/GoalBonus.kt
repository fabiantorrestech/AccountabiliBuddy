package com.accountabilibuddy.companion.nudge

import com.accountabilibuddy.shared.WatchSnapshot
import java.time.Instant
import java.time.ZoneId

/**
 * After today's goal is met (ADR 29, across devices): the work card becomes a bonus card ("Goal met · Bonus round?")
 * that comes back every 15 minutes instead of 5, and "Not now" is free.
 */
data class GoalBonus(val goalMinutes: Int, val focusedMinutes: Int, val streakDays: Int) {
    val bonusMinutes: Int get() = (focusedMinutes - goalMinutes).coerceAtLeast(0)

    companion object {
        const val REPEAT_MINUTES = 15

        /** Today's goal met, with the bonus so far and the run of days met; null before it's met or without a goal. */
        fun of(focus: WatchSnapshot?, now: Instant = Instant.now(), zone: ZoneId = ZoneId.systemDefault()): GoalBonus? {
            val goal = focus?.goalMinutes?.takeIf { it > 0 } ?: return null
            val focused = focus.focusedMinutes(now, zone)
            if (focused < goal) {
                return null
            }

            // Today counts; then each day before it in the week totals, back to the first one short of the goal.
            val earlier = focus.week.filter { it.date.isBefore(focus.date) }.sortedByDescending { it.date }
            var streak = 1
            var expected = focus.date.minusDays(1)
            for (day in earlier) {
                if (day.date != expected || day.focusedMinutes < goal) break
                streak++
                expected = expected.minusDays(1)
            }

            return GoalBonus(goal, focused, streak)
        }

        /** The work card's and nudge's repeat: 15 minutes once the goal is met, else the user's setting. */
        fun repeatMinutes(focus: WatchSnapshot?, configured: Int, now: Instant = Instant.now()): Int =
            if (of(focus, now) != null && configured in 1 until REPEAT_MINUTES) REPEAT_MINUTES else configured

        fun duration(minutes: Int): String = if (minutes >= 60) "${minutes / 60}h ${"%02d".format(minutes % 60)}m" else "${minutes}m"
    }
}
