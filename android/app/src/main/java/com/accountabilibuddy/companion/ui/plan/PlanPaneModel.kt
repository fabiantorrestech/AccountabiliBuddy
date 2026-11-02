package com.accountabilibuddy.companion.ui.plan

import com.accountabilibuddy.companion.model.PendingDayPlan

/**
 * Everything the Plan pane shows, worked out by its host: the header, the week strip, what to say about the picked day, and
 * the timeline. No Compose and no clock in it, so a tablet can lay the timeline and its notes out side by side later.
 *
 * @property title "Tuesday · Workday".
 * @property modeLine How the day is guarded when it isn't an ordinary schedule (ADR 47): "Budget day: 3h to go.", "Free day. Nothing
 *   is guarded.", "Day off."; null for a schedule day with work.
 * @property readOnlyNote Why the picked day can't be changed from here ("no PC linked", "this day is over"); null when it can.
 * @property lockNote While a focus session runs: that a change made now waits for it to end; null otherwise.
 * @property waitNote A plan edit a PC is holding (ADR 28) and when it applies; null for none.
 * @property weekLabel "Week 2 of 3" for a plan that repeats in weeks; null for the same week every week.
 */
data class PlanPaneModel(
    val title: String,
    val cells: List<PlanDayCell>,
    val timeline: PlanDayTimeline,
    val modeLine: String? = null,
    val emptyText: String? = null,
    val readOnlyNote: String? = null,
    val lockNote: String? = null,
    val waitNote: String? = null,
    val pending: PendingDayPlan? = null,
    val weekLabel: String? = null,
    val weekCount: Int = 1,
    val weekNumber: Int = 1,
    val isToday: Boolean = true,
    val canChange: Boolean = false,
)
