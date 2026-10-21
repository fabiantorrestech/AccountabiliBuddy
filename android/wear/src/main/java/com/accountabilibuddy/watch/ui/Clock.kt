package com.accountabilibuddy.watch.ui

import java.time.LocalTime
import java.time.format.DateTimeFormatter

/** Follows the watch's 12/24-hour setting; set when the app starts. */
var use24HourClock: Boolean = false

private val twelveHour = DateTimeFormatter.ofPattern("h:mm a")
private val twentyFourHour = DateTimeFormatter.ofPattern("HH:mm")

/** E.g. "1:00 PM", or "13:00" on a 24-hour watch. */
fun clock(time: LocalTime): String = time.format(if (use24HourClock) twentyFourHour else twelveHour)
