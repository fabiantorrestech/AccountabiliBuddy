package com.accountabilibuddy.companion.ui.shell

import androidx.compose.runtime.Composable
import androidx.compose.runtime.compositionLocalOf
import androidx.compose.ui.platform.LocalConfiguration

/** How the five places are offered: a bar along the bottom (phones), or a sidebar on the left (tablets, windows 840 dp wide or more). */
enum class NavLayout {
    BottomBar,
    Rail;

    companion object {
        /** The width, in dp, from which the expanded (tablet) layout is used. Below it every screen is drawn as on a phone. */
        const val EXPANDED_MIN_DP = 840

        /** A plain function of the window width in dp (a split-screen window on a tablet is narrower, so it falls back to the bar). */
        fun forWidth(screenWidthDp: Int): NavLayout = if (screenWidthDp >= EXPANDED_MIN_DP) Rail else BottomBar
    }
}

@Composable
fun rememberNavLayout(): NavLayout = NavLayout.forWidth(LocalConfiguration.current.screenWidthDp)

/** True while the expanded (tablet) layout is in use; pages read it to choose between their phone and wide forms. */
val LocalExpanded = compositionLocalOf { false }
