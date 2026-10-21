package com.accountabilibuddy.watch.model

import java.time.DayOfWeek
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.temporal.TemporalAdjusters

/** A made-up day for the mockup, in the default day plan's colours, with a session running now. */
object SampleData {
    private const val WORK = 0xFF2563EBL
    private const val LUNCH = 0xFFD97706L
    private const val EXTRA = 0xFF7C3AEDL
    private const val SLEEP = 0xFF312E81L
    private const val FOCUS = 0xFF14B8A6L

    fun snapshot(now: LocalDateTime = LocalDateTime.now()): WatchSnapshot {
        val today = now.toLocalDate()
        val monday = today.with(TemporalAdjusters.previousOrSame(DayOfWeek.MONDAY))
        val week = (0 until 7).map { offset ->
            val date = monday.plusDays(offset.toLong())
            val focused = when {
                date.isAfter(today) -> 0
                date == today -> 175
                else -> listOf(310, 260, 400, 220, 90, 0, 0)[offset]
            }
            val planned = if (date.dayOfWeek in setOf(DayOfWeek.SATURDAY, DayOfWeek.SUNDAY)) 120 else 420
            DayTotal(date, focused, planned)
        }

        return WatchSnapshot(
            plan = listOf(
                RingSpan(LocalTime.of(23, 0), LocalTime.of(7, 0), SLEEP, "Sleep"),
                RingSpan(LocalTime.of(9, 0), LocalTime.of(12, 0), WORK, "Work"),
                RingSpan(LocalTime.of(12, 0), LocalTime.of(13, 0), LUNCH, "Lunch"),
                RingSpan(LocalTime.of(13, 0), LocalTime.of(17, 0), WORK, "Work"),
                RingSpan(LocalTime.of(19, 0), LocalTime.of(21, 0), EXTRA, "Extra Work"),
            ),
            focus = listOf(
                RingSpan(LocalTime.of(9, 10), LocalTime.of(10, 40), FOCUS, "Coding"),
                RingSpan(LocalTime.of(10, 50), LocalTime.of(11, 55), FOCUS, "Coding"),
                RingSpan(LocalTime.of(13, 5), LocalTime.of(13, 25), FOCUS, "Reading"),
            ),
            drifts = listOf(LocalTime.of(10, 5), LocalTime.of(11, 30)),
            wake = LocalTime.of(7, 0),
            bedtime = LocalTime.of(23, 0),
            running = RunningSession("Coding", now.minusMinutes(20), now.plusMinutes(25), FOCUS),
            week = week,
        )
    }
}
