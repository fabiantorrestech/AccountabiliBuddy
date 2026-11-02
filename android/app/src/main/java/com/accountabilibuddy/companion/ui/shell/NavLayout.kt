package com.accountabilibuddy.companion.ui.shell

import androidx.compose.runtime.Composable
import androidx.compose.ui.platform.LocalConfiguration

/** How the five places are offered: a bar along the bottom (phones), or a rail on the left (tablets, a later run). */
enum class NavLayout {
    BottomBar,
    Rail;

    companion object {
        /** A plain function of the screen width in dp. The rail is not wired yet, so every width gets the bar. */
        @Suppress("UNUSED_PARAMETER")
        fun forWidth(screenWidthDp: Int): NavLayout = BottomBar
    }
}

@Composable
fun rememberNavLayout(): NavLayout = NavLayout.forWidth(LocalConfiguration.current.screenWidthDp)
