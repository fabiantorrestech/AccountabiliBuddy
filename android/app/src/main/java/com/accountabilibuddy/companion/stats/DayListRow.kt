package com.accountabilibuddy.companion.stats

/** A line of the day's list: a session or a stretch off track, with its time and length. */
data class DayListRow(val time: String, val title: String, val length: String, val isDrift: Boolean)
