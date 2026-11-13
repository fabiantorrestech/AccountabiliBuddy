package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.ui.graphics.vector.ImageVector
import com.accountabilibuddy.shared.tasks.ShelfToastLength
import com.accountabilibuddy.shared.tasks.ShelfToasts

/**
 * A message of the Tasks page after a change (round 2 R7.3; board Tasks-Phone-Moved): what happened ([message]), an optional second
 * line ([detail]: "Tap to see them there"), an optional [undo], and where tapping it goes ([goTo]: a page's key, or [TODAY] for the
 * Today tab) with the tasks to highlight there ([flash]) and the tap's name for a screen reader ([goLabel]). [TasksToastCard] draws
 * it with Undo, ×, and the timer bar that runs down over its life ([length]: 4 s plain, 6 s with Undo, 7 s for an explanation). A
 * new one replaces the one showing. [onGone] runs once when it leaves the screen, however it left.
 */
class TasksToast(
    val message: String,
    val detail: String? = null,
    val undo: (() -> Unit)? = null,
    val goTo: String? = null,
    val flash: List<String> = emptyList(),
    val goLabel: String? = null,
    val icon: ImageVector? = null,
    val length: ShelfToastLength = ShelfToasts.lengthOf(canUndo = undo != null),
    val onGone: (() -> Unit)? = null,
) {
    companion object {
        const val TODAY = "today"
    }
}
