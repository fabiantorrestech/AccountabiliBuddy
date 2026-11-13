package com.accountabilibuddy.companion.ui.shell

import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.runtime.staticCompositionLocalOf

/**
 * What a screen puts in the bottom bar's place for a while (round 2 R7.3b: Tasks while a row is dragged, board
 * Tasks-Phone-Drag-Bin). [AppScaffold] draws [content] over the tabs, at exactly their size, so nothing above the bar moves; null
 * shows the tabs again. Only the phone layout has a bottom bar: with the sidebar (a tablet) there is no holder
 * ([LocalBottomBarOverride] is null) and the screen draws its bar itself.
 */
class BottomBarOverride {
    var content: (@Composable () -> Unit)? by mutableStateOf(null)
}

/** The bottom bar's override of the scaffold above; null when the layout has no bottom bar. */
val LocalBottomBarOverride = staticCompositionLocalOf<BottomBarOverride?> { null }
