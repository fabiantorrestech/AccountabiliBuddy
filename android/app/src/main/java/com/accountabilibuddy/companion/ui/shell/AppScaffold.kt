package com.accountabilibuddy.companion.ui.shell

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.selection.selectable
import androidx.compose.material3.Icon
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The frame of the app: the content pane, the messages ([snackbar]) and the way between the five places. [layout] picks the
 * bottom bar or the rail; [showNav] hides it for the screens that take the whole app (Unlinked, the plan editor). [parkPill]
 * is the slot above the bar for the "Park a thought" pill (a later part fills it). The content gets the padding it must keep
 * clear of the system bars and the bar; it has no Scaffold of its own and no fixed width.
 */
@Composable
fun AppScaffold(
    nav: AppNav,
    layout: NavLayout,
    snackbar: SnackbarHostState,
    showNav: Boolean = true,
    parkPill: (@Composable () -> Unit)? = null,
    content: @Composable (PaddingValues) -> Unit,
) {
    when (layout) {
        NavLayout.BottomBar -> Scaffold(
            containerColor = Ab.colors.bg,
            snackbarHost = { SnackbarHost(snackbar) },
            bottomBar = {
                if (showNav) {
                    Column {
                        parkPill?.invoke()
                        AbBottomBar(selected = nav.destination, onSelect = nav::select)
                    }
                }
            },
            content = content,
        )
        // Not wired yet: the tablet run sets this up. A narrow rail beside the pane, Today pinned out of it.
        NavLayout.Rail -> Row(Modifier.background(Ab.colors.bg)) {
            if (showNav) AbRailStub(nav)
            Scaffold(containerColor = Ab.colors.bg, snackbarHost = { SnackbarHost(snackbar) }, content = content)
        }
    }
}

/** A placeholder for the tablet rail: the destinations that are not pinned, top to bottom. */
@Composable
private fun AbRailStub(nav: AppNav) {
    Column(Modifier.width(84.dp).fillMaxHeight().statusBarsPadding().padding(vertical = 12.dp), horizontalAlignment = Alignment.CenterHorizontally) {
        AppDestination.entries.filter { !it.pinnedOnWide }.forEach { destination ->
            Column(
                Modifier.padding(vertical = 8.dp).selectable(selected = nav.destination == destination, role = Role.Tab, onClick = { nav.select(destination) }),
                horizontalAlignment = Alignment.CenterHorizontally,
            ) {
                Icon(destination.icon, contentDescription = null, tint = Ab.colors.muted)
                Text(destination.label, style = Ab.type.micro, color = Ab.colors.muted)
            }
        }
    }
}
