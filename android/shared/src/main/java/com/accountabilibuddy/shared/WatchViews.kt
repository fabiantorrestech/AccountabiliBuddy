package com.accountabilibuddy.shared

import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId

private const val MINUTES_PER_DAY = 24 * 60

/** The ring colour of focus sessions (teal); the running one is drawn brighter. */
const val FOCUS_COLOR: Long = 0xFF14B8A6L

/** A stretch on a 24-hour ring, in minutes after midnight on the watch (an end of 1440 is midnight at the end). */
data class RingArc(val startMinute: Int, val endMinute: Int, val color: Long, val name: String, val bright: Boolean = false)

/**
 * The Today page: the plan and the focus sessions on a 24-hour ring, drift marks and the now hand, the time focused
 * against the time planned, what runs or comes next, and how old the data is. A snapshot from another day draws
 * nothing (its sessions aren't today's).
 */
data class TodayView(
    val plan: List<RingArc>,
    val focus: List<RingArc>,
    val driftMinutes: List<Int>,
    val nowMinute: Int,
    val headline: String,
    val detail: String,
    val status: String?,
    val updated: String?,
) {
    companion object {
        fun from(snapshot: WatchSnapshot, now: Instant, zone: ZoneId, use24Hour: Boolean): TodayView {
            val today = now.atZone(zone).toLocalDate()
            val dayStart = today.atStartOfDay(zone).toInstant()
            fun minuteOf(time: Instant): Int = Duration.between(dayStart, time).toMinutes().toInt().coerceIn(0, MINUTES_PER_DAY)
            val updated = WatchText.updatedAgo(snapshot.createdAt, now)
            val nowMinute = minuteOf(now)
            if (snapshot.date != today) {
                return TodayView(emptyList(), emptyList(), emptyList(), nowMinute, "No data for today", "", null, updated)
            }

            val plan = snapshot.parts.map { RingArc(minuteOf(it.start), minuteOf(it.end), argb(it.color), it.name) }
            val focus = snapshot.sessions.mapNotNull { session ->
                val end = session.end ?: now
                if (!end.isAfter(session.start)) null else RingArc(minuteOf(session.start), minuteOf(end), FOCUS_COLOR, session.profile ?: "Added", bright = session.end == null)
            }

            val focused = snapshot.focusedMinutes(now, zone)
            val planned = snapshot.plannedMinutes
            val (headline, detail) = when {
                focused == 0 -> "No focus yet" to if (planned > 0) "${WatchText.duration(planned)} planned" else "Nothing planned today"
                planned > 0 -> WatchText.duration(focused) to "of ${WatchText.duration(planned)} planned"
                else -> WatchText.duration(focused) to "focused today"
            }

            return TodayView(plan, focus, snapshot.drift.map { minuteOf(it.start) }, nowMinute, headline, detail,
                status(snapshot, now, zone, use24Hour), updated)
        }

        /** What runs ("Coding · 25m left") or, when nothing does, the next planned part that isn't sleep. */
        private fun status(snapshot: WatchSnapshot, now: Instant, zone: ZoneId, use24Hour: Boolean): String? {
            val running = snapshot.running?.takeIf { it.endsAt == null || it.endsAt.isAfter(now) }
            if (running != null) {
                val name = if (running.onBreak) "Break" else running.profile ?: "Focus"
                val endsAt = running.endsAt ?: return if (running.onBreak) "On a break" else "$name · running"
                val left = Duration.between(now, endsAt).toMinutes().toInt().coerceAtLeast(1)
                return "$name · ${WatchText.duration(left)} left"
            }

            val next = snapshot.parts.filter { it.kind != "sleep" && it.start.isAfter(now) }.minByOrNull { it.start } ?: return null
            return "Next: ${next.name} at ${WatchText.clock(next.start.atZone(zone).toLocalTime(), use24Hour)}"
        }
    }
}

/** The Session page: one ring that fills as the session goes, with the time left (or so far, with no set end). */
data class SessionView(val title: String, val big: String, val small: String, val fraction: Float, val active: Boolean) {
    companion object {
        fun from(running: RunningSession?, now: Instant, zone: ZoneId, use24Hour: Boolean): SessionView {
            val session = running?.takeIf { it.endsAt == null || it.endsAt.isAfter(now) }
                ?: return SessionView("Session", "—", "No focus session", 0f, active = false)
            val title = if (session.onBreak) "Break" else session.profile ?: "Focus"
            val endsAt = session.endsAt
            if (endsAt == null) {
                val elapsed = Duration.between(session.startedAt, now).coerceAtLeast(Duration.ZERO)
                return SessionView(title, WatchText.timer(elapsed), "since ${WatchText.clock(session.startedAt.atZone(zone).toLocalTime(), use24Hour)}",
                    (elapsed.toMinutes() % 60) / 60f, active = true)
            }

            val total = Duration.between(session.startedAt, endsAt).seconds.coerceAtLeast(1)
            val left = Duration.between(now, endsAt).coerceAtLeast(Duration.ZERO)
            return SessionView(title, WatchText.timer(left), "ends ${WatchText.clock(endsAt.atZone(zone).toLocalTime(), use24Hour)}",
                (1f - left.seconds.toFloat() / total).coerceIn(0f, 1f), active = true)
        }
    }
}

/** One of the week page's small rings. */
data class WeekDay(val date: LocalDate, val focusedMinutes: Int, val plannedMinutes: Int, val isToday: Boolean)

/** The Week page: the 7 days ending today (today live), each focused against planned, and the totals. */
data class WeekView(val days: List<WeekDay>, val headline: String, val detail: String) {
    companion object {
        fun from(snapshot: WatchSnapshot, now: Instant, zone: ZoneId): WeekView {
            val today = now.atZone(zone).toLocalDate()
            val days = (6 downTo 0).map { back ->
                val date = today.minusDays(back.toLong())
                if (date == today && snapshot.date == today) {
                    WeekDay(date, snapshot.focusedMinutes(now, zone), snapshot.plannedMinutes, isToday = true)
                } else {
                    val total = snapshot.week.firstOrNull { it.date == date }
                    WeekDay(date, total?.focusedMinutes ?: 0, total?.plannedMinutes ?: 0, isToday = date == today)
                }
            }

            val focused = days.sumOf { it.focusedMinutes }
            val planned = days.sumOf { it.plannedMinutes }
            val (headline, detail) = when {
                focused == 0 -> "No focus yet" to if (planned > 0) "${WatchText.duration(planned)} planned" else "Nothing planned this week"
                planned > 0 -> WatchText.duration(focused) to "of ${WatchText.duration(planned)} planned"
                else -> WatchText.duration(focused) to "focused this week"
            }
            return WeekView(days, headline, detail)
        }
    }
}

/** "#RRGGBB" as an opaque ARGB colour; slate when it can't be read. */
fun argb(color: String): Long = 0xFF000000L or (color.removePrefix("#").toLongOrNull(16) ?: 0x64748BL)
