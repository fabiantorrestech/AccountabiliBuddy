package com.accountabilibuddy.companion.ui.shell

import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.Saver
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue

/**
 * Where the user is in the five-place app, kept in one holder so a wide layout can read the same state. Back steps out of
 * a page inside a place first, then to Today, and then leaves the app ([back] returns false).
 *
 * It only navigates: opening a route never starts, changes or unlocks anything (the pages keep their own focus locks).
 */
class AppNav(initial: AppRoute = AppRoute.Home) {
    var route: AppRoute by mutableStateOf(initial)
        private set

    val destination: AppDestination get() = route.destination
    val detail: String? get() = route.detail

    /** True while Back has somewhere to go inside the app. */
    val canGoBack: Boolean get() = route.detail != null || route.destination != AppDestination.Today

    /** A tap on a bar item: that place's first page. */
    fun select(destination: AppDestination) {
        route = AppRoute(destination)
    }

    fun open(destination: AppDestination, detail: String? = null, anchor: String? = null) {
        route = AppRoute(destination, detail, anchor)
    }

    /** Opens a route written as `settings/devices`; false (and no change) when it is not one. */
    fun open(path: String?): Boolean {
        val parsed = AppRoute.parse(path) ?: return false
        route = parsed
        return true
    }

    /**
     * One step back: out of a page inside a place, Stats back to Goals (where it opens from; Stats opened on a goal, `stats/<goal id>`,
     * back to that goal's page), else to Today. False on Today's first page.
     */
    fun back(): Boolean = when {
        route.destination == AppDestination.Stats && route.detail?.length == GOAL_ID_LENGTH -> { route = AppRoute(AppDestination.Goals, route.detail); true }
        route.detail != null -> { route = AppRoute(route.destination); true }
        route.destination == AppDestination.Stats -> { route = AppRoute(AppDestination.Goals); true }
        route.destination != AppDestination.Today -> { route = AppRoute.Home; true }
        else -> false
    }

    companion object {
        /** The extra on MainActivity's intent that names a route (`adb shell am start ... --es ab.open settings/devices`). */
        const val EXTRA_OPEN = "ab.open"

        // A goal's id in a route (a UUID's 36 characters; a Stats view's key is shorter).
        private const val GOAL_ID_LENGTH = 36

        val Saver: Saver<AppNav, String> = Saver(save = { it.route.toPath() }, restore = { AppNav(AppRoute.parse(it) ?: AppRoute.Home) })
    }
}

@Composable
fun rememberAppNav(): AppNav = rememberSaveable(saver = AppNav.Saver) { AppNav() }
