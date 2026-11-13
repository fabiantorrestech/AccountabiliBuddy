package com.accountabilibuddy.companion.ui.shell

import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.input.pointer.pointerInput
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The frame of the app: the content pane, the messages ([snackbar]) and the way between the five places. [layout] picks the
 * bottom bar (phones) or the sidebar (tablets); [showNav] hides it for the screens that take the whole app (Unlinked, the plan editor).
 * [parkPill] is the slot above the bar (beside the sidebar, at the foot of the pane) for the "Park a thought" pill; [railFooter] is the
 * sidebar's lower part (the noise player and the sync light) and is not drawn on a phone. The content gets the padding it must keep
 * clear of the system bars and the bar; it has no Scaffold of its own and no fixed width.
 */
@Composable
fun AppScaffold(
    nav: AppNav,
    layout: NavLayout,
    snackbar: SnackbarHostState,
    showNav: Boolean = true,
    parkPill: (@Composable () -> Unit)? = null,
    railFooter: (@Composable () -> Unit)? = null,
    content: @Composable (PaddingValues) -> Unit,
) {
    // What a screen puts in the tabs' place for a while (Tasks while a row is dragged): only where there is a bottom bar.
    val barOverride = remember { BottomBarOverride() }
    when (layout) {
        NavLayout.BottomBar -> CompositionLocalProvider(LocalBottomBarOverride provides (if (showNav) barOverride else null)) {
            Scaffold(
                containerColor = Ab.colors.bg,
                snackbarHost = { SnackbarHost(snackbar) },
                bottomBar = {
                    if (showNav) {
                        Column {
                            parkPill?.invoke()
                            Box {
                                AbBottomBar(selected = nav.destination, onSelect = nav::select)
                                // Drawn over the tabs at exactly their size (so nothing above the bar moves); touches stop here.
                                barOverride.content?.let { over ->
                                    Box(Modifier.matchParentSize().background(Ab.colors.bg).pointerInput(Unit) { detectTapGestures { } }) { over() }
                                }
                            }
                        }
                    }
                },
                content = content,
            )
        }
        // The tablet: a sidebar beside the pane. The pane keeps its own Scaffold (messages, and the park pill at its foot).
        NavLayout.Rail -> Row(Modifier.background(Ab.colors.bg)) {
            if (showNav) AbSideBar(nav.destination, nav::select, footer = railFooter)
            Scaffold(
                modifier = Modifier.weight(1f),
                containerColor = Ab.colors.bg,
                snackbarHost = { SnackbarHost(snackbar) },
                bottomBar = { if (showNav) parkPill?.invoke() },
                content = content,
            )
        }
    }
}
