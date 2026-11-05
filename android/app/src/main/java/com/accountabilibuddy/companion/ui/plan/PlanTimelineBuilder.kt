package com.accountabilibuddy.companion.ui.plan

import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayTimeline
import com.accountabilibuddy.companion.engine.DayWorkWindows
import com.accountabilibuddy.companion.model.CalendarEvent
import com.accountabilibuddy.companion.model.CategoryBehavior
import com.accountabilibuddy.companion.model.DayMode
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.model.PlannedPart
import com.accountabilibuddy.companion.model.SleepTimes
import java.time.Duration
import java.time.LocalDate
import java.time.LocalDateTime

/**
 * Builds the Plan page's timeline for one date from the parts the guards see ([DayWorkWindows], so a Budget day's windows and a
 * Free day's missing work read as the guards do, ADR 47): a Wake row for the night that ends that morning, a row per part, a
 * Bedtime row for the night that starts that evening. Work parts that follow each other (with lunch or a break in between)
 * become one "Work" row that opens into deep-work stretches (the work minus breaks, comms windows and calendar events), the
 * events themselves, the breaks, and on today a "Now" line. A port of the PC's PlanTimelineBuilder (board 5c, 5o). Times are 12-hour.
 */
object PlanTimelineBuilder {
    // Parts closer than this run into one work row.
    private val BRIDGE: Duration = Duration.ofMinutes(15)
    private val SHORTEST_STRETCH: Duration = Duration.ofMinutes(15)
    private const val ENDED_ALPHA: Long = 0x4D000000L

    /**
     * @param events every calendar event the phone has; the ones on [date] are used.
     * @param focused counted focus today, shared out over the deep-work stretches that have started.
     * @param goal the deep-work goal, shown under the first work row; zero hides it.
     */
    fun build(
        plan: DayPlan,
        date: LocalDate,
        now: LocalDateTime,
        events: List<CalendarEvent>,
        focused: Duration,
        goal: Duration,
        nightFallback: ((LocalDate) -> SleepTimes?)?,
        clock: ClockFormat,
    ): PlanDayTimeline {
        val dayStart = date.atStartOfDay()
        val day = LocalWindow(dayStart, dayStart.plusDays(1))
        val isToday = now.toLocalDate() == date
        val parts = DayWorkWindows.partsIn(plan, day, now, { other -> if (other == date) focused else Duration.ZERO }, nightFallback)
        // Work ended early on a PC (ADR 65): a faint row of its own where the part would have gone on.
        val endedEarly = DayWorkWindows.endedPartsIn(plan, day, now, { other -> if (other == date) focused else Duration.ZERO }, nightFallback)
        val template = plan.templateFor(date)
        val budget = template.budgetMinutes?.takeIf { template.mode == DayMode.Budget && it > 0 }?.let { Duration.ofMinutes(it.toLong()) }

        val timed = events.filter { !it.isAllDay }.mapNotNull { event -> DayTimeline.clip(windowOf(event), day)?.let { event to it } }
        val allDay = events.filter { it.isAllDay && DayTimeline.clip(windowOf(it), day) != null }

        val rows = mutableListOf<PlanTimelineRow>()
        // All-day events head the day.
        for (event in allDay) {
            rows += PlanTimelineRow(time = "All day", title = event.title, sub = "Calendar", isEvent = true, event = event)
        }

        var group: MutableList<PlannedPart>? = null
        val trailing = mutableListOf<PlannedPart>()
        var goalShown = false

        fun lastEnd(): LocalDateTime = if (trailing.isNotEmpty()) trailing.last().window.end else group!!.last().window.end

        fun flush() {
            group?.let {
                rows += workRow(it, if (goalShown) Duration.ZERO else goal, budget, timed, isToday, now, focused, clock)
                goalShown = true
                group = null
            }

            for (rest in trailing) {
                rows += partRow(rest, clock)
            }

            trailing.clear()
        }

        val entries = (parts.map { it to false } + endedEarly.map { it to true })
            .sortedWith(compareBy({ it.first.window.start }, { it.first.window.end }))
        for ((part, isEnded) in entries) {
            if (isEnded) {
                flush()
                rows += endedRow(part, clock)
                continue
            }

            val behavior = part.category.behavior
            val current = group
            if (behavior.isWork) {
                if (current != null && Duration.between(lastEnd(), part.window.start) <= BRIDGE) {
                    current.addAll(trailing)
                    trailing.clear()
                    current.add(part)
                } else {
                    flush()
                    group = mutableListOf(part)
                }
            } else if (behavior == CategoryBehavior.Rest && current != null && Duration.between(lastEnd(), part.window.start) <= BRIDGE) {
                trailing += part
            } else {
                flush()
                val row = if (behavior == CategoryBehavior.Sleep) sleepRow(part, day, clock) else partRow(part, clock)
                if (row != null) rows += row
            }
        }

        flush()
        // A timed event outside every work block gets a row of its own, in time order, so it can be reached too (ADR 64). Not one a part already shows: a classified
        // event's own part stands for it there.
        for ((event, window) in timed) {
            val shown = rows.any { row -> row.subRows.any { it.event === event } } ||
                parts.any { it.part.fromEvent && it.window.start < window.end && window.start < it.window.end }
            if (shown) continue
            val index = rows.indexOfFirst { row -> row.at != null && row.at > window.start }
            val row = PlanTimelineRow(
                time = clock.time(window.start), title = event.title, sub = "Calendar · until ${clock.time(window.end)}", isEvent = true, event = event, at = window.start,
            )
            if (index < 0) rows += row else rows.add(index, row)
        }

        return PlanDayTimeline(rows, hasWork = parts.any { it.category.behavior.isWork } || endedEarly.isNotEmpty())
    }

    /** "2h", "1h 10m" or "45m"; rounded to the nearest minute, as the PC does. */
    fun span(span: Duration): String {
        val minutes = ((span.seconds + 30) / 60).toInt()
        val hours = minutes / 60
        val rest = minutes % 60
        return when {
            hours == 0 -> "${rest}m"
            rest == 0 -> "${hours}h"
            else -> "${hours}h ${rest}m"
        }
    }

    // A calendar event as a window (an event with no length counts a minute).
    private fun windowOf(event: CalendarEvent) = LocalWindow(event.localStart, if (event.localEnd > event.localStart) event.localEnd else event.localStart.plusMinutes(1))

    private fun workRow(
        group: List<PlannedPart>,
        goal: Duration,
        budget: Duration?,
        timed: List<Pair<CalendarEvent, LocalWindow>>,
        isToday: Boolean,
        now: LocalDateTime,
        focused: Duration,
        clock: ClockFormat,
    ): PlanTimelineRow {
        val first = group.firstOrNull { it.category.behavior == CategoryBehavior.Watch } ?: group[0]
        val span = LocalWindow(group[0].window.start, group.last().window.end)
        val others = group.filter { it.category.behavior != CategoryBehavior.Watch }.map { it.window }
        val inside = timed.filter { it.second.end > span.start && it.second.start < span.end }

        // Deep work: the Watch parts minus breaks, comms windows and events.
        val stretches = DayTimeline.subtract(group.filter { it.category.behavior == CategoryBehavior.Watch }.map { it.window }, others + inside.map { it.second })
            .filter { Duration.between(it.start, it.end) >= SHORTEST_STRETCH }

        val lines = mutableListOf<Pair<LocalDateTime, PlanSubRow>>()
        var remaining = if (isToday) focused else Duration.ZERO
        for (stretch in stretches) {
            val elapsed = if (isToday && now > stretch.start) Duration.between(stretch.start, minOf(now, stretch.end)) else Duration.ZERO
            val done = minOf(remaining, elapsed)
            remaining = remaining.minus(done)
            lines += stretch.start to PlanSubRow(
                time = clock.time(stretch.start),
                title = "Deep work",
                right = if (done >= Duration.ofMinutes(1)) "${span(done)} done" else "Until ${clock.time(stretch.end)}",
            )
        }

        for (part in group.filter { it.category.behavior != CategoryBehavior.Watch }) {
            lines += part.window.start to PlanSubRow(time = clock.time(part.window.start), title = part.name, right = span(Duration.between(part.window.start, part.window.end)))
        }

        for ((event, window) in inside) {
            val at = if (window.start < span.start) span.start else window.start
            lines += at to PlanSubRow(time = clock.time(at), title = event.title, right = "Calendar", isEvent = true, event = event)
        }

        val sorted = lines.sortedBy { it.first }
        val ordered = sorted.map { it.second }.toMutableList()
        if (isToday && now >= span.start && now < span.end) {
            val index = sorted.takeWhile { it.first <= now }.size
            ordered.add(index, PlanSubRow(title = "Now · ${clock.time(now)}", isNowLine = true))
        }

        val until = "Until ${clock.time(span.end)}"
        val sub = when {
            budget != null -> "$until · ${span(budget)} of focus"
            goal > Duration.ZERO -> "$until · deep work goal ${span(goal)}"
            else -> until
        }
        return PlanTimelineRow(time = clock.time(span.start), title = first.name, sub = sub, argb = first.category.argb, subRows = ordered)
    }

    // Work ended early (ADR 65): the part's colour, faint, and what it would have run to.
    private fun endedRow(part: PlannedPart, clock: ClockFormat): PlanTimelineRow {
        val length = Duration.between(part.window.start, part.window.end)
        return PlanTimelineRow(
            time = clock.time(part.window.start), title = "${part.name} · ended early",
            sub = "Was until ${clock.time(part.window.end)} · ${span(length)}", argb = (part.category.argb and 0x00FFFFFFL) or ENDED_ALPHA,
        )
    }

    // A part that isn't work: a break, a pre-work window.
    private fun partRow(part: PlannedPart, clock: ClockFormat): PlanTimelineRow {
        val length = Duration.between(part.window.start, part.window.end)
        val kind = if (part.part.isFlexible) "Flexible · ${part.part.flexMinutes} min" else "Until ${clock.time(part.window.end)} · ${span(length)}"
        return PlanTimelineRow(time = clock.time(part.window.start), title = part.name, sub = kind, argb = part.category.argb)
    }

    // The night: its end is a Wake row, its start a Bedtime row; a sleep that starts and ends inside the day is one row.
    private fun sleepRow(part: PlannedPart, day: LocalWindow, clock: ClockFormat): PlanTimelineRow? {
        val endsInDay = part.window.end < day.end
        val startsInDay = part.window.start > day.start
        if (endsInDay && !(startsInDay && part.window.end.hour >= 12)) {
            return PlanTimelineRow(
                time = clock.time(part.window.end), title = "Wake",
                sub = if (part.part.wakeByAlarm) "Wake-up alarm" else "Wake-up time", isSleep = true,
            )
        }

        if (!endsInDay && startsInDay) {
            val wake = day.start.with(part.part.end)
            return PlanTimelineRow(
                time = clock.time(part.window.start), title = "Bedtime",
                sub = if (part.part.wakeByAlarm) "Wakes with your alarm" else "Sleep until ${clock.time(wake)}", isSleep = true,
            )
        }

        if (endsInDay && startsInDay) {
            return PlanTimelineRow(
                time = clock.time(part.window.start), title = part.name,
                sub = "Until ${clock.time(part.window.end)} · ${span(Duration.between(part.window.start, part.window.end))}", isSleep = true,
            )
        }

        return null
    }
}
