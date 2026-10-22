package com.accountabilibuddy.shared

import java.time.Instant
import java.time.LocalDate

/**
 * Today's focus as the PC counts it (its "focusDay" link message, ADR 21): the plan's parts, the sessions that count,
 * drift marks, and focused and planned minutes for today and the 7 days ending today. No titles, addresses, reasons
 * or notes ever come with it.
 */
data class FocusDay(
    val date: LocalDate,
    val parts: List<FocusPart>,
    val sessions: List<FocusSession>,
    val drift: List<TimeWindow>,
    val focusedMinutes: Int,
    val plannedMinutes: Int,
    val week: List<DayTotal>,
)

/** A part of the day plan on [FocusDay.date], cut to that day. [kind] is "watch" (planned work), "rest" or "sleep". */
data class FocusPart(val name: String, val category: String, val color: String, val kind: String, val start: Instant, val end: Instant)

/**
 * A focus session that counts, cut to the day; [end] is null for the one running now, [profile] for one added by hand.
 * [device] is the device that counted it, once the day combines devices (ADR 22); null for the PC's own day.
 */
data class FocusSession(val start: Instant, val end: Instant?, val profile: String?, val added: Boolean, val device: String? = null)

/** A stretch of time, e.g. a drift mark. */
data class TimeWindow(val start: Instant, val end: Instant)

/** One day's focused minutes against its planned work. */
data class DayTotal(val date: LocalDate, val focusedMinutes: Int, val plannedMinutes: Int)
