package com.accountabilibuddy.companion.ui.kit

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.asPaddingValues
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.statusBars
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The one header of every tab (brief §3.1 rule 1, §3.3; change note §1.1): the title sits at the same place on Today, Tasks, Plan,
 * Goals, Settings and Stats, so switching tabs never moves it or the first row under it. As the boards draw it: the title's top
 * 56 dp from the top of the screen (the status bar included; never less than 16 dp under it), its left edge 20 dp from the pane's
 * edge, a muted [subtitle] line under it whose height is kept even when a tab has none, and the tab's [actions] at the end.
 *
 * Put it first in a pane that has 16 dp at its sides and nothing above it ([AbPageHeaderDefaults.PaneEdge]).
 */
@Composable
fun AbPageHeader(
    title: String,
    modifier: Modifier = Modifier,
    subtitle: (@Composable () -> Unit)? = null,
    actions: (@Composable RowScope.() -> Unit)? = null,
) {
    Row(
        modifier.fillMaxWidth().padding(start = AbPageHeaderDefaults.TitleEdge - AbPageHeaderDefaults.PaneEdge, top = AbPageHeaderDefaults.topSpace(), bottom = 12.dp),
        verticalAlignment = Alignment.Bottom,
    ) {
        Column(Modifier.weight(1f)) {
            Text(title, style = Ab.type.title, color = Ab.colors.ink, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.semantics { heading() })
            // The subtitle's line is always there (a blank one when the tab has none), so every tab's first row starts at the same y.
            Box(Modifier.padding(top = 4.dp).heightIn(min = AbPageHeaderDefaults.SubtitleLine), contentAlignment = Alignment.CenterStart) {
                if (subtitle != null) subtitle() else Text(" ", style = Ab.type.small, maxLines = 1)
            }
        }
        if (actions != null) {
            // Icon buttons are 48 dp with a 24 dp glyph: shifted so the last glyph sits where the boards draw it (8 dp of pane edge left).
            Row(Modifier.heightIn(min = 48.dp).offset(x = AbPageHeaderDefaults.PaneEdge - 8.dp), verticalAlignment = Alignment.CenterVertically, content = actions)
        }
    }
}

/** The numbers of [AbPageHeader], for a pane that must line something up with it. */
object AbPageHeaderDefaults {
    /** The side padding a pane gives its content. */
    val PaneEdge: Dp = 16.dp

    /** The title's left edge from the pane's edge. */
    val TitleEdge: Dp = 20.dp

    /** The boards' space above the title, from the top of the screen. */
    val TopFromScreen: Dp = 56.dp

    /** The least space between the status bar and the title (a tall status bar). */
    val MinTop: Dp = 16.dp

    /** The subtitle line's kept height. */
    val SubtitleLine: Dp = 20.dp

    /** The space above the title inside a pane that already sits under the status bar. */
    @Composable
    fun topSpace(): Dp {
        val statusBar = WindowInsets.statusBars.asPaddingValues().calculateTopPadding()
        return (TopFromScreen - statusBar).coerceAtLeast(MinTop)
    }
}
