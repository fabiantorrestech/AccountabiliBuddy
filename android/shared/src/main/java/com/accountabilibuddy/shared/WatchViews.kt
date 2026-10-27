package com.accountabilibuddy.shared

import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId

private const val MINUTES_PER_DAY = 24 * 60

/** The ring colour of focus sessions (teal); the running one is drawn brighter. */
const val FOCUS_COLOR: Long = 0xFF14B8A6L

/** The ring colour of the PC's planning stage (periwinkle blue), the same as the bar on the PC. */
const val PLANNING_COLOR: Long = 0xFF7C9EF2L

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
            val updated = WatchText.updatedAgo(snapshot.dataAt, now)
            val nowMinute = minuteOf(now)
            if (snapshot.date != today) {
                return TodayView(emptyList(), emptyList(), emptyList(), nowMinute, "No data for today", "", null, updated)
            }

            val plan = snapshot.parts.map { RingArc(minuteOf(it.start), minuteOf(it.end), argb(it.color), it.name) }
            val focus = snapshot.sessions.mapNotNull { session ->
                val end = session.end ?: snapshot.openUntil(now)
                if (!end.isAfter(session.start)) null else RingArc(minuteOf(session.start), minuteOf(end), FOCUS_COLOR, session.profile ?: "Added", bright = session.end == null)
            }

            val focused = snapshot.focusedMinutes(now, zone)
            val planned = snapshot.plannedMinutes
            val goal = snapshot.goalMinutes
            val (headline, detail) = when {
                goal != null && focused >= goal -> WatchText.duration(focused) to "Goal met"
                goal != null -> (if (focused == 0) "No focus yet" else WatchText.duration(focused)) to "of ${WatchText.duration(goal)} goal"
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
                running.planningEndsAt?.takeIf { running.isPlanning }?.let { planningEnd ->
                    return if (planningEnd.isAfter(now)) "Planning · ${WatchText.duration(Duration.between(now, planningEnd).toMinutes().toInt().coerceAtLeast(1))} left"
                    else "Planning · time's up"
                }

                val endsAt = running.endsAt ?: return if (running.onBreak) "On a break" else "$name · running"
                val left = Duration.between(now, endsAt).toMinutes().toInt().coerceAtLeast(1)
                return "$name · ${WatchText.duration(left)} left"
            }

            val next = snapshot.parts.filter { it.kind != "sleep" && it.start.isAfter(now) }.minByOrNull { it.start } ?: return null
            return "Next: ${next.name} at ${WatchText.clock(next.start.atZone(zone).toLocalTime(), use24Hour)}"
        }
    }
}

/**
 * The Session page: one ring that fills as the session goes, with the time left (or so far, with no set end), and how
 * old the data is ([updated], null while fresh).
 */
data class SessionView(
    val title: String,
    val big: String,
    val small: String,
    val fraction: Float,
    val active: Boolean,
    val updated: String? = null,
    /** The guided checklist: "3. Fix tests · 1/3", "All tasks done · 3/3"; null without one. */
    val task: String? = null,
    /** Today's focus as a share of the daily goal (ADR 29): the thin outer ring; null without a goal. */
    val goalFraction: Float? = null,
    /** The PC is in its planning stage (not focus time): the ring and the title read in the planning colour. */
    val planning: Boolean = false,
) {
    companion object {
        fun from(running: RunningSession?, now: Instant, zone: ZoneId, use24Hour: Boolean, dataAt: Instant? = null, goalFraction: Float? = null): SessionView =
            build(running, now, zone, use24Hour).copy(updated = dataAt?.let { WatchText.updatedAgo(it, now) }, goalFraction = goalFraction)

        private fun planningTask(session: RunningSession): String? = when {
            session.tasksTotal == 0 -> null
            session.task == null -> "All tasks done · ${session.tasksDone}/${session.tasksTotal}"
            else -> "${if (session.taskNumber > 0) "${session.taskNumber}. " else ""}${session.task} · ${session.tasksDone}/${session.tasksTotal}"
        }

        private fun build(running: RunningSession?, now: Instant, zone: ZoneId, use24Hour: Boolean): SessionView {
            val session = running?.takeIf { it.endsAt == null || it.endsAt.isAfter(now) }
                ?: return SessionView("Session", "—", "No focus session", 0f, active = false)
            val title = if (session.onBreak) "Break" else session.profile ?: "Focus"
            val planningEnd = session.planningEndsAt?.takeIf { session.isPlanning }
            if (planningEnd != null) {
                // Planning (before the focus countdown) has its own ring and time; it goes on past its end until the PC's "Ready?" is answered.
                val start = session.planningStartedAt ?: now
                val left = Duration.between(now, planningEnd).coerceAtLeast(Duration.ZERO)
                val total = Duration.between(start, planningEnd).seconds.coerceAtLeast(1)
                return SessionView("Planning", WatchText.timer(left),
                    if (left.isZero) "waiting for Ready? on the PC" else "ends ${WatchText.clock(planningEnd.atZone(zone).toLocalTime(), use24Hour)}",
                    (1f - left.seconds.toFloat() / total).coerceIn(0f, 1f), active = true, task = planningTask(session), planning = true)
            }

            val task = when {
                session.tasksTotal == 0 -> null
                session.task == null -> "All tasks done · ${session.tasksDone}/${session.tasksTotal}"
                else -> "${if (session.taskNumber > 0) "${session.taskNumber}. " else ""}${session.task} · ${session.tasksDone}/${session.tasksTotal}"
            }
            val endsAt = session.endsAt
            if (endsAt == null) {
                val elapsed = Duration.between(session.startedAt, now).coerceAtLeast(Duration.ZERO)
                return SessionView(title, WatchText.timer(elapsed), "since ${WatchText.clock(session.startedAt.atZone(zone).toLocalTime(), use24Hour)}",
                    (elapsed.toMinutes() % 60) / 60f, active = true, task = task)
            }

            val total = Duration.between(session.startedAt, endsAt).seconds.coerceAtLeast(1)
            val left = Duration.between(now, endsAt).coerceAtLeast(Duration.ZERO)
            return SessionView(title, WatchText.timer(left), "ends ${WatchText.clock(endsAt.atZone(zone).toLocalTime(), use24Hour)}",
                (1f - left.seconds.toFloat() / total).coerceIn(0f, 1f), active = true, task = task)
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
