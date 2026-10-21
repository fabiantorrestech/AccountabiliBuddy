package com.accountabilibuddy.companion.model

/**
 * The day plan open in the phone's editor (ADR 19): what it showed when opened, the edits so far, and the PC plan the
 * edits build on, taken when the editor opened, so a PC change arriving meanwhile is a conflict, never overwritten.
 */
data class DayPlanDraft(
    val initial: DayPlan,
    val plan: DayPlan,
    val baseVersion: String,
    val basePlan: DayPlan,
) {
    val changed: Boolean get() = plan != initial
}
