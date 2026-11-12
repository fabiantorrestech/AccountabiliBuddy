package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * What the goal page's day panel shows for one day (the PC's `GoalDayPanelViewModel`): the date, "8 of 15 min", a status pill, the
 * source line and what can be done (Tick it / Untick, a value by hand). [tone] colours the pill: teal counted, amber late or short,
 * grey in progress, paused or not due (brief §3.8).
 */
data class GoalDayPanel(
    val day: LocalDate,
    val isPast: Boolean,
    val title: String,
    val big: String,
    val of: String?,
    val status: String,
    val tone: Tone,
    val source: String,
    val note: String?,
    val canTick: Boolean,
    val tickText: String,
    val canEdit: Boolean,
    val editCaption: String,
) {
    enum class Tone { Teal, Amber, Grey }
}
