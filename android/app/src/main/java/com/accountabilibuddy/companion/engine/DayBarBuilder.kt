package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.CalendarEvent
import com.accountabilibuddy.companion.model.DayBarRange
import com.accountabilibuddy.companion.model.DayBarSettings
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.model.PlannedFocus
import com.accountabilibuddy.companion.model.ProfileDocument
import com.accountabilibuddy.companion.model.SleepSettings
import java.time.Duration
import java.time.LocalDateTime
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
    ): DayBarModel {
        val day = dayAt(now, settings, sleep)
        val span = DayTimeline.calendarSpan(day)
        val planned = DayTimeline.planned(document.schedule, span)

        var content: List<LocalWindow> = planned.map { it.window }
        if (sleep.enabled) {
            // The calendar dates also hold tomorrow's early blocks; only blocks touching this waking day stretch it.
            content = content.filter { DayTimeline.clip(it, day) != null }
        }

        val range = if (settings.range == DayBarRange.FullDay) day else DayTimeline.fitTo(day, content, span)

        val plannedSpans = planned.mapNotNull { focus ->
            DayTimeline.clip(focus.window, range)?.let {
                toSpan(it, range, "${profileName(document, focus.profileId)} · ${times(it)}", focus.profileId)
            }
        }
        val actualSpans = slept.mapNotNull { window ->
            DayTimeline.clip(window, range)?.let { toSpan(it, range, "Slept ${times(window)} (${duration(Duration.between(window.start, window.end))})") }
        }
        val (night, missed) = sleepSpans(now, range, sleep)

        return DayBarModel(
            day = day,
            dateText = day.start.format(dateFormat),
            statusText = status(now, range, planned, document),
            summaryText = summary(range, planned),
            planned = plannedSpans,
            actual = actualSpans,
            events = eventSpans(events, range),
            night = night,
            missed = missed,
            ticks = ticks(range),
            now = fraction(now, range),
            sleepLine = sleepLine(now, sleep),
        )
    }

    /** The day the bar is about at [now], before it grows to fit the blocks; the lists under the bar follow it. */
    fun dayAt(now: LocalDateTime, settings: DayBarSettings, sleep: SleepSettings): LocalWindow = when {
        settings.range == DayBarRange.FullDay -> DayTimeline.fullDay(now)
        sleep.enabled -> wakingDay(now, sleep)
        else -> DayTimeline.dayContaining(now, settings.dayStart, settings.dayEnd)
    }

    /** What comes next, for the widget and the notification: the running block's end or the next block's start. */
    fun nextChange(now: LocalDateTime, document: ProfileDocument): LocalDateTime? {
        val planned = DayTimeline.planned(document.schedule, LocalWindow(now.minusDays(1), now.plusDays(2)))
        val current = planned.firstOrNull { it.window.contains(now) }
        return current?.window?.end ?: planned.firstOrNull { it.window.start.isAfter(now) }?.window?.start
    }

    /** Wake time to bedtime, plus a little of the night (more while still up, until wake time). */
    private fun wakingDay(now: LocalDateTime, sleep: SleepSettings): LocalWindow {
        val schedule = sleep.toSchedule()
        val waking = schedule.dayAround(now)
        val night = schedule.nightAround(now)
        val afterBed = night.start.plus(NIGHT_SHOWN)
        val afterNow = now.plus(NIGHT_AFTER_NOW)
        val shownUntil = if (afterBed.isAfter(afterNow)) afterBed else afterNow
        return LocalWindow(waking.start, if (shownUntil.isBefore(night.end)) shownUntil else night.end)
    }

    private fun sleepSpans(now: LocalDateTime, range: LocalWindow, sleep: SleepSettings): Pair<List<DayBarSpan>, List<DayBarSpan>> {
        if (!sleep.enabled) {
            return emptyList<DayBarSpan>() to emptyList()
        }

        val schedule = sleep.toSchedule()
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

    /** From two hours before bedtime until wake time. */
    fun sleepLine(now: LocalDateTime, sleep: SleepSettings): SleepLine? {
        if (!sleep.enabled) {
            return null
        }

        val night = sleep.toSchedule().nightAround(now)
        if (!night.end.isAfter(night.start) || now.isBefore(night.start.minus(SLEEP_LINE_LEAD)) || !now.isBefore(night.end)) {
            return null
        }

        val left = SleepSchedule.sleepIfBedAt(now, night)
        val text = if (now.isBefore(night.start)) {
            "Bedtime ${clock.time(night.start)} · ${duration(left)} of sleep"
        } else {
            "Sleep if you go now: ${duration(left)}"
        }

        return SleepLine(text, SleepSchedule.rate(left))
    }

    private fun eventSpans(events: List<CalendarEvent>, range: LocalWindow): List<DayBarSpan> =
        events.mapNotNull { event ->
            val end = if (event.localEnd.isAfter(event.localStart)) event.localEnd else event.localStart.plusMinutes(1)
            val window = LocalWindow(event.localStart, end)
            DayTimeline.clip(window, range)?.let {
                toSpan(it, range, "${event.title} · ${if (event.isAllDay) "all day" else times(window)}")
            }
        }

    private fun status(now: LocalDateTime, range: LocalWindow, planned: List<PlannedFocus>, document: ProfileDocument): String {
        val current = planned.firstOrNull { it.window.contains(now) }
        if (current != null) {
            return "${profileName(document, current.profileId)} until ${clock.time(current.window.end)}"
        }

        val next = planned.firstOrNull { it.window.start.isAfter(now) && it.window.start.isBefore(range.end) }
        if (next != null) {
            return "Next: ${profileName(document, next.profileId)} at ${clock.time(next.window.start)}"
        }

        return if (planned.any { DayTimeline.clip(it.window, range) != null }) "No more focus planned today" else "No focus blocks today"
    }

    private fun summary(range: LocalWindow, planned: List<PlannedFocus>): String {
        val total = DayTimeline.union(planned.mapNotNull { DayTimeline.clip(it.window, range) })
            .fold(Duration.ZERO) { sum, window -> sum.plus(Duration.between(window.start, window.end)) }
        return if (total.isZero) "" else "${duration(total)} planned"
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

    private fun profileName(document: ProfileDocument, id: UUID) = document.profile(id)?.name ?: "(deleted profile)"

    companion object {
        private val TICK_STEPS = intArrayOf(1, 2, 3, 4, 6)
        private const val MAX_TICKS = 8
        private val NIGHT_SHOWN: Duration = Duration.ofHours(1)
        private val NIGHT_AFTER_NOW: Duration = Duration.ofMinutes(30)
        private val SLEEP_LINE_LEAD: Duration = Duration.ofHours(2)

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
