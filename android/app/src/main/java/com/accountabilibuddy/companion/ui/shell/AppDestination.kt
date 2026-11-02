package com.accountabilibuddy.companion.ui.shell

import androidx.compose.ui.graphics.vector.ImageVector
import com.accountabilibuddy.companion.ui.SectionIcons

/**
 * The five places of the app, in the same order on every device (Today, Tasks, Plan, Stats, Settings). [path] is the first
 * part of a route string such as `settings/devices` (see [AppNav]). [pinnedOnWide]: on a wide screen Today stays on the left
 * and the other four open beside it.
 */
enum class AppDestination(val path: String, val label: String, val icon: ImageVector, val pinnedOnWide: Boolean = false) {
    Today("today", "Today", SectionIcons.Today, pinnedOnWide = true),
    Tasks("tasks", "Tasks", SectionIcons.Tasks),
    Plan("plan", "Plan", SectionIcons.Plan),
    Stats("stats", "Stats", SectionIcons.Stats),
    Settings("settings", "Settings", SectionIcons.Settings);

    companion object {
        fun fromPath(path: String): AppDestination? = entries.firstOrNull { it.path == path.lowercase() }
    }
}
