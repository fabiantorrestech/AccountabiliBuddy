package com.accountabilibuddy.companion.model

import java.time.LocalDateTime

/** A calendar event from the PC's backup (in-app events and cached connected calendars), local wall-clock time. */
data class CalendarEvent(
    val id: String,
    val calendarId: String,
    val title: String,
    val localStart: LocalDateTime,
    val localEnd: LocalDateTime,
    val isAllDay: Boolean,
    /** Its calendar's colour (ARGB), when the PC sent it. */
    val color: Int? = null,
)
