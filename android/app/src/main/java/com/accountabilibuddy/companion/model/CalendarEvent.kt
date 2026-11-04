package com.accountabilibuddy.companion.model

import java.time.Instant
import java.time.LocalDateTime
import java.util.UUID

/**
 * A calendar event, local wall-clock time: from the PC's sync file (in-app events and cached connected calendars) or, on the
 * phone, from Android's own calendar (ADR 58). The last four are the ones ADR 58 adds.
 */
data class CalendarEvent(
    val id: String,
    val calendarId: String,
    val title: String,
    val localStart: LocalDateTime,
    val localEnd: LocalDateTime,
    val isAllDay: Boolean,
    /** Its calendar's colour (ARGB), when the PC sent it. */
    val color: Int? = null,
    /** What stays the same across a repeating event's occurrences; null for a single event. */
    val seriesId: String? = null,
    /** Which occurrence of the series this is, by its original time; null when the event does not repeat. */
    val occurrenceKey: String? = null,
    /** From the PC's file only: the day plan category the PC resolved for the event (its rules stay on the PC). */
    val categoryId: UUID? = null,
    /** From the PC's file only: when the PC first saw the event at this time and category; null = not noticed. */
    val knownSince: Instant? = null,
    /** From the PC's file only: the category was chosen by hand, so it may lift Pre-Work for the event's time (ADR 64). */
    val categoryByHand: Boolean = false,
) {
    /** What a "this event" classification is stored under: the occurrence's series and original time when it has them, else its id (the PC's ClassificationKey). */
    val classificationKey: String get() = if (seriesId == null) id else "$seriesId#$occurrenceKey"
}
