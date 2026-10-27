package com.accountabilibuddy.companion.widget

import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayTimeline
import com.accountabilibuddy.companion.engine.DayWorkWindows
import com.accountabilibuddy.companion.model.LocalWindow
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter

/** Focus blocks (running or next) and events of the coming day, one line each, for the widget. */
object Upcoming {
    private val weekday: DateTimeFormatter = DateTimeFormatter.ofPattern("EEE ")

    fun items(now: LocalDateTime, state: CompanionState, clock: ClockFormat): List<String> {
        val window = LocalWindow(now, now.plusHours(24))
        val blocks = DayWorkWindows.workWindows(state.document.dayPlan, LocalWindow(now.minusDays(1), window.end), now, nightFallback = state.sleep::fallbackNightBefore)
            .filter { it.window.end.isAfter(now) }
            .map { it.window.start to "${it.name}${if (it.window.contains(now)) " · now" else ""}" }
        val events = state.events
            .filter { DayTimeline.clip(LocalWindow(it.localStart, maxOf(it.localEnd, it.localStart.plusMinutes(1))), window) != null }
            .map { it.localStart to it.title }

        return (blocks + events).sortedBy { it.first }.map { (start, text) ->
            val day = if (start.toLocalDate() != now.toLocalDate() && start.isAfter(now)) start.format(weekday) else ""
            val time = if (start.isBefore(now)) "now" else "$day${clock.time(start)}"
            "$time  $text"
        }
    }
}
