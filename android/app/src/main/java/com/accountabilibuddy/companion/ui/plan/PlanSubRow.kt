package com.accountabilibuddy.companion.ui.plan

/**
 * One line inside the work block of the Plan timeline: a deep-work stretch ("9:30 AM  Deep work  1h 10m done"), a break or
 * comms window, a calendar event, or today's "Now · 1:38 PM" line ([isNowLine], the one place the page uses the accent).
 */
data class PlanSubRow(
    val time: String = "",
    val title: String,
    val right: String? = null,
    val isEvent: Boolean = false,
    val isNowLine: Boolean = false,
    /** The calendar event this line stands for; a tap opens its sheet (ADR 64). */
    val event: com.accountabilibuddy.companion.model.CalendarEvent? = null,
)
