package com.accountabilibuddy.companion.ui.shell

/**
 * Where the app is: a [destination], a [detail] inside it (a Settings category, a task id, a Stats view) and an optional
 * [anchor] in that page. Written as `settings/devices#anchor`; only the destination is needed (`plan`).
 */
data class AppRoute(val destination: AppDestination, val detail: String? = null, val anchor: String? = null) {
    fun toPath(): String = destination.path + (detail?.let { "/$it" } ?: "") + (anchor?.let { "#$it" } ?: "")

    companion object {
        val Home = AppRoute(AppDestination.Today)

        /** Reads `destination[/detail][#anchor]`; null when the destination is not one of the five. */
        fun parse(path: String?): AppRoute? {
            val text = path?.trim()?.trim('/')?.takeIf { it.isNotEmpty() } ?: return null
            val anchor = text.substringAfter('#', "").takeIf { it.isNotEmpty() }
            val place = text.substringBefore('#')
            val destination = AppDestination.fromPath(place.substringBefore('/')) ?: return null
            val detail = place.substringAfter('/', "").takeIf { it.isNotEmpty() }
            return AppRoute(destination, detail, anchor)
        }
    }
}
