package com.accountabilibuddy.watch.model

import com.accountabilibuddy.shared.DayTotal
import com.accountabilibuddy.shared.FocusPart
import com.accountabilibuddy.shared.FocusSession
import com.accountabilibuddy.shared.RunningSession
import com.accountabilibuddy.shared.TimeWindow
import com.accountabilibuddy.shared.WatchSnapshot
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.time.ZoneId

/** A made-up day in the default day plan's colours, with a session running now: "Sample day" before linking. */
object SampleData {
    fun snapshot(now: Instant, zone: ZoneId): WatchSnapshot {
        val today = now.atZone(zone).toLocalDate()
        fun at(hour: Int, minute: Int = 0, date: LocalDate = today): Instant = date.atTime(LocalTime.of(hour, minute)).atZone(zone).toInstant()
        val week = (6 downTo 1).map { back ->
            val date = today.minusDays(back.toLong())
            DayTotal(date, listOf(310, 260, 400, 220, 90, 0)[6 - back], if (date.dayOfWeek.value >= 6) 120 else 420)
        }

        return WatchSnapshot(
            createdAt = now,
            linked = true,
            pcName = "your PC",
            date = today,
            parts = listOf(
                FocusPart("Sleep", "Sleep", "#334155", "sleep", at(0), at(7)),
                FocusPart("Work", "Work", "#2563EB", "watch", at(9), at(12)),
                FocusPart("Lunch", "Lunch", "#CA8A04", "rest", at(12), at(13)),
                FocusPart("Work", "Work", "#2563EB", "watch", at(13), at(17)),
                FocusPart("Extra Work", "Extra Work", "#7C3AED", "watch", at(18), at(20)),
                FocusPart("Sleep", "Sleep", "#334155", "sleep", at(23), at(0, date = today.plusDays(1))),
            ),
            sessions = listOf(
                FocusSession(at(9, 10), at(10, 40), "Coding", added = false),
                FocusSession(at(10, 50), at(11, 55), "Coding", added = false),
                FocusSession(now.minusSeconds(20 * 60), null, "Coding", added = false),
            ),
            drift = listOf(TimeWindow(at(10, 5), at(10, 7)), TimeWindow(at(11, 30), at(11, 33))),
            plannedMinutes = 540,
            week = week,
            running = RunningSession("Coding", now.minusSeconds(20 * 60), now.plusSeconds(25 * 60), onBreak = false),
        )
    }
}
