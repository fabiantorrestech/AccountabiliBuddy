package com.accountabilibuddy.companion.noise

/** What the player bar and the widget show. */
data class NoiseState(
    val playing: Boolean = false,
    /** The current sound's shown name; null when there's none. */
    val title: String? = null,
    val hasFolder: Boolean = false,
    val fileCount: Int = 0,
)
