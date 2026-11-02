package com.accountabilibuddy.companion.stats

import java.time.LocalDate

/** What a bar of the 12 AM to 12 AM strip is. */
enum class DayStripKind { Focus, Drift, Sleep }

/** A stretch on the day strip: [from] and [to] are 0 (12 AM) to 1 (the next 12 AM). */
data class DayStripSpan(val from: Float, val to: Float, val kind: DayStripKind, val description: String)

/** A line of the day's list: a session or a stretch off track, with its time and length. */
data class DayListRow(val time: String, val title: String, val length: String, val isDrift: Boolean)

/**
 * One day in detail (board 5k's right column): the strip, deep work against the goal, drift, and the day's sessions.
 *
 * @property focus The counted focus ("2h 15m").
 * @property focusNote "goal met", "1h 10m to go" or "goal 2h"; empty when no PC has sent a goal.
 * @property driftCount Visits off track inside the day's sessions.
 * @property hasData There is data for this date: it falls on or after the first day any device sent a session.
 */
data class DayPanel(
    val date: LocalDate,
    val title: String,
    val spans: List<DayStripSpan>,
    val focus: String,
    val focusNote: String,
    val met: Boolean,
    val driftCount: Int,
    val driftTime: String,
    val rows: List<DayListRow>,
    val hasData: Boolean,
)
