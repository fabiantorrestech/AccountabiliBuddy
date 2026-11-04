package com.accountabilibuddy.companion.ui.shell

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxScope
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.widthIn
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp

/** The widest a full-screen guard's content gets on a tablet. */
val GuardContentWidth = 640.dp

/**
 * The root of a full-screen guard or takeover screen (the work nudge, Phones down, Essentials, the timer and bedtime alarms): a [Box] that
 * takes [modifier] (the screen's own background and insets) and fills the window, like the plain Box these screens used.
 * On a window 840 dp wide or more the content sits in a centred column at most [maxWidth] wide, with the background still filling the window;
 * under 840 dp it is exactly a [Box]. The screen stays full-screen and nothing about when it shows or how it closes changes.
 */
@Composable
fun GuardFrame(
    modifier: Modifier = Modifier,
    contentAlignment: Alignment = Alignment.TopStart,
    maxWidth: Dp = GuardContentWidth,
    content: @Composable BoxScope.() -> Unit,
) {
    val wide = LocalConfiguration.current.screenWidthDp >= NavLayout.EXPANDED_MIN_DP
    Box(modifier, contentAlignment) {
        if (wide) {
            Box(Modifier.align(Alignment.TopCenter).fillMaxHeight().widthIn(max = maxWidth).fillMaxWidth(), contentAlignment) { content() }
        } else {
            content()
        }
    }
}
