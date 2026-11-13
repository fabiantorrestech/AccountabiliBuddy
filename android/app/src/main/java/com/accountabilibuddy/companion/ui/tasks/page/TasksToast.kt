package com.accountabilibuddy.companion.ui.tasks.page

/**
 * A message of the Tasks page after a change (round 2 R7.3): what happened, an optional second line ("Tap to see them there"), an
 * optional [undo], and where tapping it goes ([goTo]: a page's key, or [TODAY] for the Today tab). R7.3a shows it as the app's
 * snackbar with Undo; R7.3b draws the board's toast (Undo, ×, the timer bar, tap to go) from the same value.
 */
class TasksToast(val message: String, val detail: String? = null, val undo: (() -> Unit)? = null, val goTo: String? = null) {
    companion object {
        const val TODAY = "today"
    }
}
