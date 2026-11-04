package com.accountabilibuddy.companion.ui.shell

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.widthIn
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp

/** The width wizards, setup and guard screens keep on a tablet (they stay one pane, in the middle). */
val NarrowPaneWidth = 600.dp

/** The widest a page of running text gets on a tablet; longer lines are hard to read. */
val TextPaneWidth = 840.dp

/**
 * Centres [content] in a column no wider than [maxWidth] on a tablet. On a phone ([LocalExpanded] false) it only draws [content], so
 * nothing about a phone's screen changes.
 */
@Composable
fun CenteredPane(modifier: Modifier = Modifier, maxWidth: Dp = NarrowPaneWidth, content: @Composable () -> Unit) {
    if (!LocalExpanded.current) {
        content()
        return
    }
    Box(modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
        Box(Modifier.fillMaxHeight().widthIn(max = maxWidth).fillMaxWidth()) { content() }
    }
}

