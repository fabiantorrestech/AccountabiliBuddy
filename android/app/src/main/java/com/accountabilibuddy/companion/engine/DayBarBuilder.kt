package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.CalendarEvent
import com.accountabilibuddy.companion.model.DayBarRange
import com.accountabilibuddy.companion.model.DayBarSettings
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.model.PlannedFocus
import com.accountabilibuddy.companion.model.ProfileDocument
import com.accountabilibuddy.companion.model.SleepSettings
import com.accountabilibuddy.shared.WatchSnapshot
import java.time.Duration
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.time.temporal.ChronoUnit
import java.util.Locale
import java.util.UUID

/**
 * Works out the day bar for a moment, like the Windows app's DayBarViewModel: with sleep mode on (and "fit to my
 * day") the day runs from wake time to bedtime and grows into the night while you're still up; from two hours
 * before bedtime the sleep line says how much sleep is left if you go to bed now.
 */
class DayBarBuilder(private val clock: ClockFormat, locale: Locale = Locale.getDefault()) {
    private val dateFormat: DateTimeFormatter = DateTimeFormatter.ofPattern("EEE (M/d)", locale)

    /** @param slept sleep measured on the phone (Health Connect), drawn in the actual lane. */
    fun build(
        now: LocalDateTime,
        document: ProfileDocument,
        events: List<CalendarEvent>,
        settings: DayBarSettings,
        sleep: SleepSettings,
        slept: List<LocalWindow> = emptyList(),
        /** The context-aware header ([DayHeader]); null: the plan's own status and summary. */
        header: HeaderText? = null,
        /** The devices' combined focus today (sessions and drift); null: an empty Focus row. */
        focus: WatchSnapshot? = null,
        /** Today's lunch once started (flexible break, schema v8): solid on the Plan row. */
        lunch: LocalWindow? = null,
    ): DayBarModel {
        val day = dayAt(now, settings, sleep, document.dayPlan)
        val span = DayTimeline.calendarSpan(day)
        val focusedOn = DayWorkWindows.focusedToday(focus, now, java.time.ZoneId.systemDefault())
        val planned = DayWorkWindows.workWindows(document.dayPlan, span, now, focusedOn, sleep::fallbackNightBefore)

        var content: List<LocalWindow> = planned.map { it.window }
        if (sleep.enabled) {
            // The calendar dates also hold tomorrow's early parts; only parts touching this waking day stretch it.
            content = content.filter { DayTimeline.clip(it, day) != null }
        }

        val range = if (settings.range == DayBarRange.FullDay) day else DayTimeline.fitTo(day, content, span)

        // Every part of the day plan (Rest and Sleep too) in its category's colour; the texts still follow the Watch parts.
        // A day either side, so a night running past the bar keeps its real times in the label.
        val parts = DayWorkWindows.partsIn(document.dayPlan, LocalWindow(span.start.minusDays(1), span.end.plusDays(1)), now, focusedOn, sleep::fallbackNightBefore)
        val nights = parts.filter { it.category.behavior == com.accountabilibuddy.companion.model.CategoryBehavior.Sleep }.map { it.window }
        val plannedSpans = parts.flatMap { part ->
            val shown = DayTimeline.clip(part.window, range) ?: return@flatMap emptyList()
            val plain = toSpan(shown, range, "${part.name} · ${times(part.window)}").copy(color = part.category.argb.toInt(), name = part.name)
            if (part.part.isFlexible) {
                // A flexible break's window (schema v8): see-through over the work until it's taken.
                return@flatMap listOf(plain.copy(name = null, overlap = true, label = "${part.name} window · ${times(part.window)}"))
            }
            if (part.category.behavior != com.accountabilibuddy.companion.model.CategoryBehavior.PreWork || nights.isEmpty()) {
                return@flatMap listOf(plain)
            }

            // Pre-Work over Sleep (ADR 31): the piece over the night is see-through, the rest as usual.
            val awake = DayTimeline.subtract(listOf(shown), nights)
            val over = DayTimeline.subtract(listOf(shown), awake)
            awake.map { toSpan(it, range, plain.label).copy(color = plain.color, name = part.name) } +
                over.map { toSpan(it, range, "${plain.label} (over Sleep)").copy(color = plain.color, overlap = true) }
        }
        val lunchSpans = listOfNotNull(lunch?.let { taken ->
            DayTimeline.clip(taken, range)?.let { toSpan(it, range, "Lunch · ${times(taken)}").copy(color = document.dayPlan.flexibleBreakOn(taken.start.toLocalDate())?.color?.toInt() ?: 0xFFCA8A04.toInt(), name = "Lunch") }
        })
        val actualSpans = slept.mapNotNull { window ->
            DayTimeline.clip(window, range)?.let { toSpan(it, range, "Slept ${times(window)} (${duration(Duration.between(window.start, window.end))})") }
        }
        val (night, missed) = sleepSpans(now, range, sleep, document.dayPlan)
        val soFar = LocalWindow(range.start, minOf(now, range.end))
        val sessions = focusWindows(focus, now).mapNotNull { DayTimeline.clip(it, soFar) }
        // Work time so far with no session: its own category on the Focus row (not a day plan category).
        val gaps = if (focus == null) emptyList() else DayTimeline.subtract(planned.mapNotNull { DayTimeline.clip(it.window, soFar) }, sessions)

        return DayBarModel(
            day = day,
            dateText = day.start.format(dateFormat),
            statusText = header?.status ?: status(now, range, planned, document),
            statusCompact = header?.compactStatus,
            summaryText = header?.summary ?: summary(range, planned),
            planned = plannedSpans + lunchSpans,
            actual = actualSpans,
            events = eventSpans(events.filter { it.calendarId !in settings.calendarsOff }, range),
            night = night,
            missed = missed,
            ticks = ticks(range),
            now = fraction(now, range),
            sleepLine = sleepLine(now, sleep, document.dayPlan, slept),
            countdownTo = header?.countdownTo,
            countdownFormat = header?.countdownFormat,
            rows = settings.visibleRows(),
            focus = focusSpans(focus, now, range),
            drift = focus?.drift.orEmpty().mapNotNull { mark ->
                DayTimeline.clip(local(mark.start, mark.end), range)?.let { toSpan(it, range, "Drift ${times(local(mark.start, mark.end))}") }
            },
            gaps = gaps.map { toSpan(it, range, "Not in a session · ${times(it)} (${duration(Duration.between(it.start, it.end))})") },
            focusLegend = if (focus == null) emptyList() else focusLegend(focus, sessions, gaps),
        )
    }

    /** The day the bar is about at [now], before it grows to fit the blocks; the lists under the bar follow it. */
    fun dayAt(now: LocalDateTime, settings: DayBarSettings, sleep: SleepSettings, plan: DayPlan): LocalWindow = when {
        settings.range == DayBarRange.FullDay -> DayTimeline.fullDay(now)
        sleep.enabled -> wakingDay(now, sleep, plan)
        else -> DayTimeline.dayContaining(now, settings.dayStart, settings.dayEnd)
    }

    /** What comes next, for the widget and the notification: the current work's end or the next work's start. */
    fun nextChange(now: LocalDateTime, document: ProfileDocument, sleep: SleepSettings? = null): LocalDateTime? {
        val planned = DayWorkWindows.workWindows(document.dayPlan, LocalWindow(now.minusDays(1), now.plusDays(2)), now, nightFallback = sleep?.let { it::fallbackNightBefore })
        val current = planned.firstOrNull { it.window.contains(now) }
        return current?.window?.end ?: planned.firstOrNull { it.window.start.isAfter(now) }?.window?.start
    }

    /** Wake time to bedtime, plus a little of the night (more while still up, until wake time). */
    private fun wakingDay(now: LocalDateTime, sleep: SleepSettings, plan: DayPlan): LocalWindow {
        val schedule = sleep.toSchedule(plan)
        val waking = schedule.dayAround(now)
        val night = schedule.nightAround(now)
        val afterBed = night.start.plus(NIGHT_SHOWN)
        val afterNow = now.plus(NIGHT_AFTER_NOW)
        val shownUntil = if (afterBed.isAfter(afterNow)) afterBed else afterNow
        return LocalWindow(waking.start, if (shownUntil.isBefore(night.end)) shownUntil else night.end)
    }

    private fun sleepSpans(now: LocalDateTime, range: LocalWindow, sleep: SleepSettings, plan: DayPlan): Pair<List<DayBarSpan>, List<DayBarSpan>> {
        if (!sleep.enabled) {
            return emptyList<DayBarSpan>() to emptyList()
        }

        val schedule = sleep.toSchedule(plan)
        val nights = mutableListOf<DayBarSpan>()
        var evening = range.start.toLocalDate().minusDays(1)
        while (evening.atStartOfDay().isBefore(range.end)) {
            val night = schedule.night(evening)
            DayTimeline.clip(night, range)?.let {
                nights.add(toSpan(it, range, "Sleep ${times(night)} (${duration(Duration.between(night.start, night.end))})"))
            }

            evening = evening.plusDays(1)
        }

        val missed = mutableListOf<DayBarSpan>()
        val current = schedule.nightAround(now)
        if (now.isAfter(current.start)) {
            val lost = LocalWindow(current.start, if (now.isBefore(current.end)) now else current.end)
            DayTimeline.clip(lost, range)?.let {
                missed.add(toSpan(it, range, "Up past bedtime: ${duration(Duration.between(lost.start, lost.end))} less sleep"))
            }
        }

        return nights to missed
    }

    /**
     * From when going to bed would give the sleep goal until wake time (the header then says the sleep left and the wake
     * time): this week's measured sleep from [slept], "Last night 6h 40m · week avg 7h 05m", coloured against the goal.
     * Null with nothing measured.
     */
    fun sleepLine(now: LocalDateTime, sleep: SleepSettings, plan: DayPlan, slept: List<LocalWindow> = emptyList()): SleepLine? {
        if (!sleep.enabled) {
            return null
        }

        val night = sleep.toSchedule(plan).nightAround(now)
        if (!night.end.isAfter(night.start) || !SleepSchedule.showsCountdown(now, night, sleep.goal)) {
            return null
        }

        val week = SleepWeek.of(slept, now) ?: return null
        val parts = listOfNotNull(
            week.lastNight?.let { "Last night ${duration(it)}" },
            week.average?.takeIf { week.nights > 1 }?.let { "week avg ${duration(it)}" },
        )
        val rated = week.average ?: week.lastNight ?: return null
        return SleepLine(parts.joinToString(" · "), SleepSchedule.rate(rated, sleep.goal))
    }

    // Focused, distracted (drift inside the sessions, overlaps once) and not in a session, each from 1 minute.
    private fun focusLegend(focus: WatchSnapshot, sessions: List<LocalWindow>, gaps: List<LocalWindow>): List<LegendEntry> {
        val distracted = focus.drift.flatMap { mark -> sessions.mapNotNull { DayTimeline.clip(local(mark.start, mark.end), it) } }
        val legend = mutableListOf(LegendEntry("${duration(total(sessions))} focused", FOCUS_COLOR))
        total(distracted).takeIf { it >= Duration.ofMinutes(1) }?.let { legend.add(LegendEntry("Distracted ${duration(it)}", DISTRACTED_COLOR)) }
        total(gaps).takeIf { it >= Duration.ofMinutes(1) }?.let { legend.add(LegendEntry("Not in a session ${duration(it)}", NOT_IN_SESSION_COLOR)) }
        return legend
    }

    private fun total(windows: List<LocalWindow>): Duration =
        DayTimeline.union(windows).fold(Duration.ZERO) { sum, window -> sum.plus(Duration.between(window.start, window.end)) }

    // The sessions' real times; one with no end runs until now (or the running session's end, once passed).
    private fun focusWindows(focus: WatchSnapshot?, now: LocalDateTime): List<LocalWindow> {
        focus ?: return emptyList()
        val until = focus.openUntil(now.atZone(ZoneId.systemDefault()).toInstant())
        return focus.sessions.map { local(it.start, it.end ?: until) }.filter { it.end.isAfter(it.start) }
    }

    // Each session cut to the bar; one with no end runs until now (or the running session's end, once passed).
    private fun focusSpans(focus: WatchSnapshot?, now: LocalDateTime, range: LocalWindow): List<DayBarSpan> {
        focus ?: return emptyList()
        val until = focus.openUntil(now.atZone(ZoneId.systemDefault()).toInstant())
        return focus.sessions.mapNotNull { session ->
            val window = local(session.start, session.end ?: until)
            if (!window.end.isAfter(window.start)) null
            else DayTimeline.clip(window, range)?.let { toSpan(it, range, "${session.profile ?: "Focus"} · ${times(window)}").copy(name = session.profile) }
        }
    }

    private fun local(start: Instant, end: Instant): LocalWindow {
        val zone = ZoneId.systemDefault()
        return LocalWindow(LocalDateTime.ofInstant(start, zone), LocalDateTime.ofInstant(end, zone))
    }

    private fun eventSpans(events: List<CalendarEvent>, range: LocalWindow): List<DayBarSpan> =
        events.mapNotNull { event ->
            val end = if (event.localEnd.isAfter(event.localStart)) event.localEnd else event.localStart.plusMinutes(1)
            val window = LocalWindow(event.localStart, end)
            DayTimeline.clip(window, range)?.let {
                toSpan(it, range, "${event.title} · ${if (event.isAllDay) "all day" else times(window)}").copy(color = event.color, allDay = event.isAllDay)
            }
        }.sortedByDescending { it.allDay } // All-day slivers first, so timed events draw over them.

    private fun status(now: LocalDateTime, range: LocalWindow, planned: List<PlannedFocus>, document: ProfileDocument): String {
        val current = planned.firstOrNull { it.window.contains(now) }
        if (current != null) {
            return "${current.name} until ${clock.time(current.window.end)}"
        }

        val next = planned.firstOrNull { it.window.start.isAfter(now) && it.window.start.isBefore(range.end) }
        if (next != null) {
            return "Next: ${next.name} at ${clock.time(next.window.start)}"
        }

        return if (planned.any { DayTimeline.clip(it.window, range) != null }) "No more work planned today" else "No work planned today"
    }

    private fun summary(range: LocalWindow, planned: List<PlannedFocus>): String {
        val total = DayTimeline.union(planned.mapNotNull { DayTimeline.clip(it.window, range) })
            .fold(Duration.ZERO) { sum, window -> sum.plus(Duration.between(window.start, window.end)) }
        return if (total.isZero) "" else "${duration(total)} of work planned"
    }

    private fun ticks(range: LocalWindow): List<DayBarTick> {
        val hours = Duration.between(range.start, range.end).toMinutes() / 60.0
        val step = TICK_STEPS.firstOrNull { hours / it <= MAX_TICKS } ?: TICK_STEPS.last()
        var tick = range.start.truncatedTo(ChronoUnit.HOURS)
        if (tick.isBefore(range.start)) {
            tick = tick.plusHours(1)
        }

        val ticks = mutableListOf<DayBarTick>()
        while (!tick.isAfter(range.end)) {
            if (tick.hour % step == 0) {
                ticks.add(DayBarTick(fraction(tick, range), clock.hour(tick.hour)))
            }

            tick = tick.plusHours(1)
        }

        return ticks
    }

    private fun toSpan(window: LocalWindow, range: LocalWindow, label: String, profileId: UUID? = null) =
        DayBarSpan(fraction(window.start, range), fraction(window.end, range), label, profileId)

    private fun times(window: LocalWindow) = "${clock.time(window.start)} – ${clock.time(window.end)}"

    companion object {
        private val TICK_STEPS = intArrayOf(1, 2, 3, 4, 6)
        private const val MAX_TICKS = 8
        private val NIGHT_SHOWN: Duration = Duration.ofHours(1)
        private val NIGHT_AFTER_NOW: Duration = Duration.ofMinutes(30)

        /** The legend's colours, as on the bar (and the PC): teal focus, red distracted, slate not in a session. */
        const val FOCUS_COLOR = 0xFF0F766E.toInt()
        const val DISTRACTED_COLOR = 0xFFEF4444.toInt()
        const val NOT_IN_SESSION_COLOR = 0xFF64748B.toInt()

        fun fraction(time: LocalDateTime, range: LocalWindow): Double {
            val length = Duration.between(range.start, range.end).seconds.toDouble()
            return if (length <= 0) 0.0 else Duration.between(range.start, time).seconds / length
        }

        /** "6h 35m" or "40m", like the Windows app. */
        fun duration(duration: Duration): String {
            val minutes = Math.round(duration.seconds / 60.0)
            return if (minutes >= 60) "${minutes / 60}h %02dm".format(minutes % 60) else "${minutes}m"
        }
    }
}
