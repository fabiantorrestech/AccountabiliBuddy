package com.accountabilibuddy.companion.engine

import java.util.UUID

/**
 * A stretch on the bar, as fractions of its width (0 = left edge, 1 = right edge).
 * @param color ARGB of a day plan part's category; null = the lane's own colour.
 * @param name drawn inside it when there's room (a plan part's name).
 */
data class DayBarSpan(
    val start: Double,
    val end: Double,
    val label: String,
    val profileId: UUID? = null,
    val color: Int? = null,
    val name: String? = null,
    /** An all-day event (the Calendar row): a thin sliver along the top of the row, not a full block. */
    val allDay: Boolean = false,
    /** Pre-Work over Sleep (the Plan row): see-through with stripes, so the night shows under it. */
    val overlap: Boolean = false,
    /** Work ended early by Start shutdown (ADR 65, the Plan row): its part's colour, much lighter. */
    val ended: Boolean = false,
    /** The calendar event it stands for (the Calendar row); a tap on the bar opens its sheet (ADR 64). */
    val event: com.accountabilibuddy.companion.model.CalendarEvent? = null,
)
