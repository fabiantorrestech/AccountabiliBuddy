package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.ui.graphics.vector.ImageVector

/**
 * What swiping a task row does (bundle spec §4 B): [rightLabel] / [onRight] for a swipe to the right, [leftLabel] / [onLeft] for one
 * to the left. A Shelf row goes right to Today and left to Later (Next when it is in Later already); a Today row goes right to Done
 * and left to the Shelf. A swipe that starts on a row is always the row's and never changes pages.
 */
class RowSwipe(
    val rightLabel: String,
    val rightIcon: ImageVector,
    val onRight: () -> Unit,
    val leftLabel: String,
    val leftIcon: ImageVector,
    val onLeft: () -> Unit,
)
