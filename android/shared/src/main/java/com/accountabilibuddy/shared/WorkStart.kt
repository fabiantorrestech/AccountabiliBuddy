package com.accountabilibuddy.shared

import java.time.Duration
import java.time.Instant

/**
 * When work starts, for the start nudge on the phone and the watch: today's planned work stretches (touching work parts
 * are one), and whether one began a moment ago with no session running on any device.
 */
object WorkStart {
    /** A stretch that began this long ago or less still gets its nudge (the phone checks every few minutes). */
    val NUDGE_WITHIN: Duration = Duration.ofMinutes(30)

    /** The starts of [parts]' work stretches, soonest first. */
    fun starts(parts: List<FocusPart>): List<Instant> {
        val work = parts.filter { it.kind == "watch" }.sortedBy { it.start }
        val starts = mutableListOf<Instant>()
        var end: Instant? = null
        for (part in work) {
            if (end == null || part.start > end) {
                starts += part.start
            }

            end = if (end == null) part.end else maxOf(end, part.end)
        }

        return starts
    }

    /** The stretch the nudge is for at [now]: begun within [NUDGE_WITHIN], with no session running. Null otherwise. */
    fun due(snapshot: WatchSnapshot, now: Instant): Instant? {
        if (snapshot.running != null) {
            return null
        }

        return starts(snapshot.parts).lastOrNull { it <= now && Duration.between(it, now) <= NUDGE_WITHIN }
    }

    /** The next stretch start after [now] (the watch's buzz), while no session runs; null for none today. */
    fun next(snapshot: WatchSnapshot?, now: Instant): Instant? {
        if (snapshot == null || snapshot.running != null) {
            return null
        }

        return starts(snapshot.parts).firstOrNull { it > now }
    }
}
