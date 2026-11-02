package com.accountabilibuddy.companion.ui.plan

/**
 * One row of the Plan timeline: a part of the day (Wake, Morning, Work, Wind down, Bedtime) with its start time, a muted
 * line, and, for the work block, the lines inside it. [argb] is the part's category colour (shown in Standard colour, the
 * plan tone in Quiet); a sleep row ([isSleep]) always takes the sleep tone.
 */
data class PlanTimelineRow(
    val time: String,
    val title: String,
    val sub: String? = null,
    val argb: Long? = null,
    val isSleep: Boolean = false,
    val isEvent: Boolean = false,
    val subRows: List<PlanSubRow> = emptyList(),
)
