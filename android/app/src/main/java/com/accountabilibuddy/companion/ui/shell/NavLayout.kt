package com.accountabilibuddy.companion.ui.shell

import androidx.compose.runtime.Composable
import androidx.compose.runtime.compositionLocalOf
import androidx.compose.ui.platform.LocalConfiguration

/** How the five places are offered: a bar along the bottom (phones), or a sidebar on the left (tablets, windows 840 dp wide or more). */
enum class NavLayout {
    BottomBar,
    Rail;

    companion object {
        /** The window width, in dp, from which the expanded (tablet) layout is used. Below it every screen is drawn as on a phone. */
        const val EXPANDED_MIN_DP = 840

        /**
         * The device's smallest screen width, in dp, the expanded layout needs as well. A phone turned to landscape is wider than
         * [EXPANDED_MIN_DP] (a 412 x 915 dp phone reports about 915) but its smallest width stays near 412, so it keeps the phone layout.
         */
        const val EXPANDED_MIN_SMALLEST_DP = 600

        /** The one check every screen shares: a wide window on a device that is a tablet. A split-screen window on a tablet is narrower, so it falls back to the bar. */
        fun isExpanded(screenWidthDp: Int, smallestScreenWidthDp: Int): Boolean =
            screenWidthDp >= EXPANDED_MIN_DP && smallestScreenWidthDp >= EXPANDED_MIN_SMALLEST_DP

        fun forWidth(screenWidthDp: Int, smallestScreenWidthDp: Int): NavLayout =
            if (isExpanded(screenWidthDp, smallestScreenWidthDp)) Rail else BottomBar
    }
}

/** True when this window gets the expanded (tablet) layout ([NavLayout.isExpanded]). */
@Composable
fun isExpandedWindow(): Boolean {
    val configuration = LocalConfiguration.current
    return NavLayout.isExpanded(configuration.screenWidthDp, configuration.smallestScreenWidthDp)
}

@Composable
fun rememberNavLayout(): NavLayout = if (isExpandedWindow()) NavLayout.Rail else NavLayout.BottomBar

/** True while the expanded (tablet) layout is in use; pages read it to choose between their phone and wide forms. */
val LocalExpanded = compositionLocalOf { false }
