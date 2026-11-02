package com.accountabilibuddy.companion.ui.shell

/**
 * Where the app is: a [destination], a [detail] inside it (a Settings category, a task id, a Stats view) and an optional
 * [anchor] in that page. Written as `settings/devices#anchor`; only the destination is needed (`plan`).
 */
data class AppRoute(val destination: AppDestination, val detail: String? = null, val anchor: String? = null) {
    fun toPath(): String = destination.path + (detail?.let { "/$it" } ?: "") + (anchor?.let { "#$it" } ?: "")

    companion object {
        val Home = AppRoute(AppDestination.Today)

        /** Longest route text taken: a route is a few words, and what is taken is kept in the saved state. */
        private const val MAX_LENGTH = 100

        /** Longest detail or anchor (a task id is 36 characters). */
        private const val MAX_PART = 40

        private val ALLOWED = Regex("[a-z0-9/#-]+")
        private val PART = Regex("[a-z0-9-]{1,$MAX_PART}")

        /**
         * Reads `destination[/detail][#anchor]`. Text with anything but `a-z`, `0-9`, `-`, `/` and `#` (capitals count as lower case),
         * longer than [MAX_LENGTH], or with a detail or anchor that is not 1 to [MAX_PART] of `a-z0-9-` is dropped: null, as for a
         * destination that is not one of the five. (An app can send any text to the exported activity; this keeps it small and plain.)
         */
        fun parse(path: String?): AppRoute? {
            val raw = path?.trim() ?: return null
            if (raw.length > MAX_LENGTH) return null
            val text = raw.lowercase().trim('/').takeIf { it.isNotEmpty() && ALLOWED.matches(it) } ?: return null
            val anchor = text.substringAfter('#', "").takeIf { it.isNotEmpty() }
            val place = text.substringBefore('#')
            val destination = AppDestination.fromPath(place.substringBefore('/')) ?: return null
            val detail = place.substringAfter('/', "").takeIf { it.isNotEmpty() }
            if (detail != null && !PART.matches(detail)) return null
            if (anchor != null && !PART.matches(anchor)) return null
            return AppRoute(destination, detail, anchor)
        }
    }
}
