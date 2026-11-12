package com.accountabilibuddy.companion.ui.shell

import androidx.compose.ui.graphics.vector.ImageVector
import com.accountabilibuddy.companion.ui.SectionIcons

/**
 * The places of the app. The bar (and the tablet's sidebar) shows five, in the same order on every device: Today, Tasks, Plan,
 * Goals, Settings (round 2 R6.1: Goals took Stats' slot). Stats is still a place of its own (its routes, `stats/consistency`, keep
 * working) but is not in the bar ([inBar] false): it opens from the Goals header, and while it shows, the bar marks Goals ([barItem]).
 * [path] is the first part of a route string such as `settings/devices` (see [AppNav]). [pinnedOnWide] is not used.
 */
enum class AppDestination(val path: String, val label: String, val icon: ImageVector, val pinnedOnWide: Boolean = false, val inBar: Boolean = true) {
    Today("today", "Today", SectionIcons.Today, pinnedOnWide = true),
    Tasks("tasks", "Tasks", SectionIcons.Tasks),
    Plan("plan", "Plan", SectionIcons.Plan),
    Goals("goals", "Goals", SectionIcons.Goals),
    Settings("settings", "Settings", SectionIcons.Settings),
    Stats("stats", "Stats", SectionIcons.Stats, inBar = false);

    /** The bar item that is marked while this place shows: Stats belongs to Goals, every other place to itself. */
    val barItem: AppDestination get() = if (this == Stats) Goals else this

    companion object {
        /** The five places the bar and the sidebar list, in order. */
        val bar: List<AppDestination> = entries.filter { it.inBar }

        fun fromPath(path: String): AppDestination? = entries.firstOrNull { it.path == path.lowercase() }
    }
}
