package com.accountabilibuddy.companion.model

import java.time.Instant
import java.util.UUID

/** How far a classification reaches (ADR 58; the PC's ClassificationScope). */
enum class ClassificationScope { ThisEvent, WholeSeries }

/**
 * A day plan category chosen on one of this phone's own calendar events or on its whole series (ADR 64; the PC's EventClassification).
 * [key] is the event's [CalendarEvent.classificationKey], or its series id. [categoryId] null = "Don't classify", which overrides a rule.
 */
data class EventClassification(val calendarId: String, val key: String, val scope: ClassificationScope, val categoryId: UUID?)

/** A looser change waiting to take over: [change] stored, or the choice for its target taken away when [removes]. */
data class PendingClassification(val change: EventClassification, val removes: Boolean, val takesOverAt: Instant)
