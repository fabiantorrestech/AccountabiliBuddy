package com.accountabilibuddy.shared.goals

/**
 * The weekly review's entry card on the phone's Goals list (board Goals-Phone-Review; the PC's `GoalReviewRow`): whether it shows, whether
 * it is snoozed (ProductaBuddy's snooze keeps a quiet card), whether Snooze is offered, and its lines.
 *
 * @property title "Weekly goals review · 8 goals".
 * @property note "About 4 minutes · Sundays", or the quiet "Snoozed · start it whenever you like".
 */
data class GoalReviewRowState(
    val shown: Boolean,
    val snoozed: Boolean,
    val canSnooze: Boolean,
    val title: String,
    val note: String,
) {
    companion object {
        val HIDDEN = GoalReviewRowState(shown = false, snoozed = false, canSnooze = false, title = "", note = "")
    }
}
