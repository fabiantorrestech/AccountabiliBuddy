package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.CalendarEvent
import com.accountabilibuddy.companion.model.CategoryBehavior
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.model.PlannedPart
import com.accountabilibuddy.shared.WatchSnapshot
import com.accountabilibuddy.shared.WatchText
import java.time.Duration
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * What the header knows at [now]: today's plan parts (every category), calendar events, the night around now (null with
 * sleep mode off) and the sleep goal, where the wake time comes from ([wakeSource], e.g. "alarm"; null: the plan), the
 * combined focus of the user's devices (null: none yet) and the phone's screen time today (null: no Usage access).
 */
data class HeaderInput(
    val now: LocalDateTime,
    val zone: ZoneId,
    val parts: List<PlannedPart>,
    val events: List<CalendarEvent>,
    val night: LocalWindow?,
    val sleepGoal: Duration,
    val wakeSource: String?,
    val focus: WatchSnapshot?,
    val phoneMinutes: Int?,
)

/**
 * The header's two lines and its live countdown: [status] says what fits the moment, [summary] the day's numbers.
 * While something counts down, [countdownTo] is when and [countdownFormat] the status with "%s" for the ticking time
 * (the notification's chronometer); other surfaces show [status] with whole minutes.
 */
data class HeaderText(
    val status: String,
    val summary: String,
    val countdownTo: Instant? = null,
    val countdownFormat: String? = null,
    /** The text changes by the minute (the sleep left near and past bedtime): the card is redrawn every minute. */
    val refreshMinutely: Boolean = false,
    /** The collapsed card's line, e.g. "Sleep: 7h 52m • Wake: 6:45 AM"; null = [status]. */
    val compactStatus: String? = null,
)

/**
 * The context-aware header of the notification, the Today screen and the widget: a running session first (from the PC
 * over the link, or an open session in another device's sync file), then the night, the part of the plan now, what
 * starts within the hour, the evening and a day off. The summary adds focus against the plan, drifts, phone screen
 * time, the wake time and the sleep ahead in the evening, and how old the focus data is.
 */
object DayHeader {
    private val SOON: Duration = Duration.ofMinutes(60)
    private val STALE: Duration = Duration.ofMinutes(60)

    // "· Bedtime in 2h 30m" joins "Done for today" and "Day off" only this close to bedtime.
    private val EVENING: Duration = Duration.ofHours(4)

    fun build(input: HeaderInput, clock: ClockFormat): HeaderText {
        val summary = summary(input, clock)
        val status = status(input, clock)
        // A status may bring a lead for the summary line (e.g. "Bedtime in 40m").
        return status.copy(summary = listOf(status.summary, summary).filter { it.isNotEmpty() }.joinToString(" · "))
    }

    private fun status(input: HeaderInput, clock: ClockFormat): HeaderText {
        val now = input.now
        val nowInstant = now.atZone(input.zone).toInstant()
        val today = now.toLocalDate()
        val focus = input.focus?.takeIf { it.date == today }

        val running = focus?.running?.takeIf { it.endsAt?.isAfter(nowInstant) ?: true }
        if (running != null) {
            val name = if (running.onBreak) "Break" else running.profile ?: "Focus"
            val endsAt = running.endsAt
            return when {
                endsAt != null -> HeaderText("$name · ${minutesText(Duration.between(nowInstant, endsAt))} left", "", endsAt, "$name · %s left")
                running.onBreak -> HeaderText("On a break", "")
                else -> HeaderText("$name · ${minutesText(Duration.between(running.startedAt, nowInstant))} so far", "")
            }
        }

        val open = focus?.sessions?.lastOrNull { it.end == null && it.start.isBefore(nowInstant) }
        if (open != null) {
            val where = open.device?.let { " on $it" } ?: ""
            return HeaderText("${open.profile ?: "Focus"}$where · ${minutesText(Duration.between(open.start, nowInstant))} so far", "")
        }

        val night = input.night
        if (night != null && !now.isBefore(night.start) && now.isBefore(night.end)) {
            val wake = wakeText(night, input, clock)
            // "Sleep • 7h 52m • 6:45 AM": the sleep left if you go now, by the minute, and the wake time.
            val left = minutesText(Duration.between(now, night.end))
            return HeaderText("Sleep • $left • $wake", "", refreshMinutely = true, compactStatus = "Sleep: $left • Wake: $wake")
        }

        val watch = input.parts.filter { it.category.behavior.isWork }
        input.parts.firstOrNull { it.window.contains(now) && it.category.behavior != CategoryBehavior.Sleep }?.let { current ->
            if (current.category.behavior.isWork) {
                return HeaderText("${current.name} until ${clock.time(current.window.end)} · no session", "")
            }

            val nextWork = watch.firstOrNull { !it.window.start.isBefore(current.window.end) && it.window.start.toLocalDate() == today }
            return HeaderText("${current.name} until ${clock.time(current.window.end)}" + (nextWork?.let { " · ${it.name} next" } ?: ""), "")
        }

        soonest(input)?.let { (name, start, isEvent) ->
            val target = start.atZone(input.zone).toInstant()
            val left = minutesText(Duration.between(now, start))
            return if (isEvent) HeaderText("$name in $left", "", target, "$name in %s")
            else HeaderText("$name starts in $left", "", target, "$name starts in %s")
        }

        val bedtimeIn = night?.takeIf { now.isBefore(it.start) }?.let { Duration.between(now, it.start) }
        if (night != null && bedtimeIn != null && bedtimeIn <= SOON) {
            // Close to bedtime: the sleep you'd get if you went now, by the minute.
            val wake = wakeText(night, input, clock)
            val left = minutesText(Duration.between(now, night.end))
            return HeaderText("Sleep • $left • $wake", "Bedtime in ${minutesText(bedtimeIn)}", refreshMinutely = true, compactStatus = "Sleep: $left • Wake: $wake")
        }

        watch.firstOrNull { it.window.start.isAfter(now) && it.window.start.toLocalDate() == today }?.let { next ->
            return HeaderText("Next: ${next.name} at ${clock.time(next.window.start)}", "")
        }

        val bedtime = bedtimeIn?.takeIf { it <= EVENING }?.let { " · Bedtime in ${minutesText(it)}" } ?: ""
        val workedToday = watch.any { it.window.start.toLocalDate() == today || it.window.end.toLocalDate() == today }
        return HeaderText(if (workedToday) "Done for today$bedtime" else "Day off$bedtime", "")
    }

    // The next part (not sleep) or timed event starting within the hour, whichever is sooner: name, start, is an event.
    private fun soonest(input: HeaderInput): Triple<String, LocalDateTime, Boolean>? {
        val now = input.now
        val limit = now.plus(SOON)
        val part = input.parts
            .filter { it.category.behavior != CategoryBehavior.Sleep && it.window.start.isAfter(now) && !it.window.start.isAfter(limit) }
            .minByOrNull { it.window.start }
        val event = input.events
            .filter { !it.isAllDay && it.localStart.isAfter(now) && !it.localStart.isAfter(limit) }
            .minByOrNull { it.localStart }
        return when {
            event != null && (part == null || event.localStart.isBefore(part.window.start)) -> Triple(event.title, event.localStart, true)
            part != null -> Triple(part.name, part.window.start, false)
            else -> null
        }
    }

    private fun summary(input: HeaderInput, clock: ClockFormat): String {
        val now = input.now
        val nowInstant = now.atZone(input.zone).toInstant()
        val focus = input.focus?.takeIf { it.date == now.toLocalDate() }
        val plannedMinutes = focus?.plannedMinutes?.takeIf { it > 0 } ?: watchMinutesToday(input)
        val focused = focus?.focusedMinutes(nowInstant, input.zone) ?: 0
        val parts = mutableListOf<String>()
        // ADR 29: the PC's daily goal, when it sent one (a setting: an older focus day's goal still holds).
        val goal = input.focus?.goalMinutes
        when {
            goal != null && focused >= goal -> parts += "Goal met · ${minutesText(focused)}"
            goal != null -> parts += "${minutesText(focused)} of ${minutesText(goal)} goal"
            focused > 0 && plannedMinutes > 0 -> parts += "${minutesText(focused)} of ${minutesText(plannedMinutes)} focused"
            focused > 0 -> parts += "${minutesText(focused)} focused"
            plannedMinutes > 0 -> parts += "${minutesText(plannedMinutes)} planned"
        }

        focus?.drift?.size?.takeIf { it > 0 }?.let { parts += if (it == 1) "1 drift" else "$it drifts" }
        input.phoneMinutes?.takeIf { it > 0 }?.let { parts += "Phone ${minutesText(it)}" }

        // The evening (no more work today), until the "sleep if you go now" line takes over.
        val night = input.night
        val workLeft = input.parts.any { it.category.behavior.isWork && it.window.end.isAfter(now) && it.window.start.toLocalDate() == now.toLocalDate() }
        if (night != null && !workLeft && now.isBefore(night.start) && !SleepSchedule.showsCountdown(now, night, input.sleepGoal)) {
            parts += "up ${wakeText(night, input, clock)}"
            parts += "${DayBarBuilder.duration(Duration.between(night.start, night.end))} sleep"
        }

        input.focus?.dataAt?.takeIf { Duration.between(it, nowInstant) > STALE }?.let { WatchText.updatedAgo(it, nowInstant) }?.let { parts += it }
        return parts.joinToString(" · ")
    }

    private fun wakeText(night: LocalWindow, input: HeaderInput, clock: ClockFormat): String =
        clock.time(night.end) + (input.wakeSource?.let { " ($it)" } ?: "")

    // Today's Watch time in the plan, overlaps once (when no device sent today's focus yet).
    private fun watchMinutesToday(input: HeaderInput): Int {
        val start = input.now.toLocalDate().atStartOfDay()
        val today = LocalWindow(start, start.plusDays(1))
        val windows = input.parts.filter { it.category.behavior.isWork }.mapNotNull { DayTimeline.clip(it.window, today) }
        return DayTimeline.union(windows).sumOf { Duration.between(it.start, it.end).toMinutes() }.toInt()
    }

    private fun minutesText(duration: Duration): String = DayBarBuilder.duration(duration)

    private fun minutesText(minutes: Int): String = DayBarBuilder.duration(Duration.ofMinutes(minutes.toLong()))
}
