package com.accountabilibuddy.companion.ui.plan

/** The Plan timeline of one date: its [rows], and whether the plan has any guarded work in it (a Budget day's windows count). */
data class PlanDayTimeline(val rows: List<PlanTimelineRow>, val hasWork: Boolean)
